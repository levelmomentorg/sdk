import { resolveHostedOptions, addBridgeVersion } from "@levelmoment/sdk-core";
// LevelMomentWebAd — thin loader over the hosted /break page (ADR-001).
//
// Mirrors the AdMob RewardedAd API: load() in the background, then show() in the ad slot.
// This SDK ships NO DOM renderer — show() opens the hosted /break page in a
// fullscreen iframe and bridges its postMessage events. All question rendering
// and the impression queue live in the hosted page (platform/web/app/break).
// Structurally mirrors sdk/react-native/src/LevelMomentAd.ts (WebView shell).
//
// Usage:
//   const client = LevelMomentWebClient.initialize(config);
//   client.start();
//
//   // Preload during gameplay
//   client.loadAd({ onAdLoaded: (ad) => (pending = ad) });
//
//   // Show in an existing ad slot. The hosted page handles everything.
//   // earnedReward fires once per graded answer — accumulate, apply on dismiss.
//   let reward = 0;
//   pending?.show({
//     onUserEarnedReward: (r) => (reward = Math.max(reward, r.amount)),
//     onAdDismissed: () => { if (reward === 1) grantBonus(); resumeGame(); },
//   });

import type {
  LevelMomentConfig,
  LevelMomentAdError,
  RewardItem,
  BreakFormat,
} from "@levelmoment/sdk-core";

export interface WebAdLoadCallbacks {
  onAdLoaded: (ad: LevelMomentWebAd) => void;
  onAdFailedToLoad: (error: LevelMomentAdError) => void;
}

export interface WebAdShowCallbacks {
  onUserEarnedReward?: (reward: RewardItem) => void;
  onAdDismissed?: () => void;
  /**
   * Fired instead of onAdDismissed when the hosted page reports a terminal
   * error (mirrors the RN / Flutter / Unity shells). Optional: when absent,
   * an error falls back to onAdDismissed so the game still resumes.
   */
  onAdFailedToShow?: (error: LevelMomentAdError) => void;
}

// The iframe + postMessage plumbing (origin validation, pre-`ready` watchdog,
// once-only teardown) lives in ./breakFrame so the startup gate reuses exactly
// the same guarantees. HostMessage is re-exported here because it was part of
// this module's public surface before the split.
export type { HostMessage } from "./breakFrame.js";
import { BreakFrame, type HostMessage } from "./breakFrame.js";

interface WebAdSpec {
  placementId: string;
  format: BreakFormat;
  breakUrl: string;
  apiUrl?: string;
  studentToken?: string;
  testing?: boolean;
  mock?: boolean;
  /** Opaque game-server context, sent through the host handshake. */
  customData?: string;
  /**
   * Pre-`ready` watchdog timeout (ms). Defaults to DEFAULT_LOAD_TIMEOUT_MS
   * when unset. 0 or negative disables the watchdog entirely.
   */
  loadTimeoutMs?: number;
}

export class LevelMomentWebAd {
  private _loaded = false;
  private _shown = false;
  private _consumed = false;
  private _rewardIds = new Set<string>();
  private _disposed = false;
  private _frame: BreakFrame | null = null;

  private constructor(private readonly _spec: WebAdSpec) {}

  /**
   * Mark the ad ready to show. The actual question fetch happens inside the
   * hosted /break page when show() is called — there is no separate native
   * preload step in this architecture. Kept as a separate call so consumers
   * can stay on the familiar AdMob load → show pattern.
   *
   * Prefer calling this via LevelMomentWebClient.loadAd() so you never have to
   * pass config and breakUrl manually.
   */
  static load(
    config: LevelMomentConfig & {
      breakUrl?: string;
      mock?: boolean;
      breakLoadTimeoutMs?: number;
    },
    callbacks: WebAdLoadCallbacks,
    options: { format?: BreakFormat } = {},
  ): void {
    if (!config.placementId) {
      queueMicrotask(() =>
        callbacks.onAdFailedToLoad({
          code: "unknown",
          message: "placementId is required to load an LevelMoment break.",
        }),
      );
      return;
    }

    config = resolveHostedOptions(config);
    const ad = new LevelMomentWebAd({
      placementId: config.placementId,
      format: options.format ?? "flashcard",
      breakUrl: resolveHostedOptions(config).breakUrl,
      testing: !!config.unsafeTesting,
      apiUrl: config.apiUrl,
      studentToken: config.studentToken,
      mock: config.mock,
      customData: config.customData,
      loadTimeoutMs: config.breakLoadTimeoutMs,
    });
    ad._loaded = true;
    queueMicrotask(() => {
      if (ad._disposed) return;
      callbacks.onAdLoaded(ad);
    });
  }

  /** True once onAdLoaded has fired and show() can be called. */
  isLoaded(): boolean {
    return this._loaded;
  }

  /**
   * Always false. Throttling is now decided server-side inside the hosted
   * /break page (it fetches the break session), so the loader shell cannot
   * and need not know about it — kept for AdMob-mirroring API compatibility.
   */
  isThrottled(): boolean {
    return false;
  }

  /**
   * Open the hosted /break page in a fullscreen iframe. The page handles all
   * question rendering and posts back terminal events: 'earnedReward' fires
   * once per answer, then 'dismissed' (or 'error') fires when the session ends.
   */
  show(callbacks: WebAdShowCallbacks): void {
    if (this._disposed || this._consumed) return;
    if (!this._loaded) {
      // Mirrors the old onAdFailedToShow fallback: resume cleanly.
      callbacks.onAdDismissed?.();
      return;
    }
    this._shown = true;
    this._consumed = true;

    const teardown = (): void => {
      this._frame?.close();
      this._frame = null;
      this._shown = false;
    };

    this._frame = BreakFrame.open({
      url: this._buildUrl(),
      title: "LevelMoment break",
      loadTimeoutMs: this._spec.loadTimeoutMs,
      onMessage: (msg: HostMessage) => {
        switch (msg.type) {
          case "earnedReward":
            if (msg.payload.rewardId) {
              if (this._rewardIds.has(msg.payload.rewardId)) return;
              this._rewardIds.add(msg.payload.rewardId);
            }
            callbacks.onUserEarnedReward?.({
              type: "question_answered",
              amount: msg.payload.amount,
              ...(msg.payload.rewardId
                ? { rewardId: msg.payload.rewardId }
                : {}),
            });
            return;
          case "dismissed":
            teardown();
            callbacks.onAdDismissed?.();
            return;
          case "needCredential":
            // The page is asking rather than reading a token off its URL.
            // Answer with whatever this game configured — usually nothing,
            // because a paired device's credential already lives on the hosted
            // origin. `custody: false`: this adapter has nowhere better to keep
            // a credential than the hosted origin's own storage, so it never
            // asks for one back.
            this._frame?.deliverCredential({
              token: this._spec.studentToken ?? "",
              custody: false,
              customData: this._spec.customData,
            });
            return;
          case "error":
            teardown();
            if (callbacks.onAdFailedToShow) {
              callbacks.onAdFailedToShow({
                code:
                  (msg.payload.code as LevelMomentAdError["code"]) ?? "unknown",
                message: msg.payload.message,
              });
            } else {
              callbacks.onAdDismissed?.();
            }
            return;
          // `ready` cancels the watchdog inside BreakFrame; `signedIn` belongs
          // to the startup gate and never reaches a break.
          default:
            return;
        }
      },
      // The hosted page never posted `ready` (it crashed, navigated away, or
      // the network dropped it) and the game never called dispose(). Without
      // this the fullscreen iframe would permanently cover the game. Resume
      // exactly as the not-loaded fallback at the top of show() does.
      onLoadTimeout: () => {
        if (this._disposed) return;
        teardown();
        callbacks.onAdDismissed?.();
      },
    });
  }

  /** Release resources: tear down the iframe and remove the message listener. */
  dispose(): void {
    this._disposed = true;
    this._frame?.close();
    this._frame = null;
    this._shown = false;
  }

  private _buildUrl(): string {
    const params = new URLSearchParams();
    params.set("placementId", this._spec.placementId);
    params.set("format", this._spec.format);
    if (this._spec.mock) {
      params.set("mock", "true");
    } else if (this._spec.apiUrl) {
      params.set("apiUrl", this._spec.apiUrl);
    }
    // No credential rides on this URL. The page asks for one over the bridge
    // (`needCredential`) and gets it from deliverCredential() above, so a live
    // token never reaches a game's launch URL, a crash report, or a web log.
    addBridgeVersion(params, this._spec.testing);
    const sep = this._spec.breakUrl.includes("?") ? "&" : "?";
    return `${this._spec.breakUrl}${sep}${params.toString()}`;
  }
}
