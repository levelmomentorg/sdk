// LevelMomentWebAd — thin loader over the hosted /break page (ADR-001).
//
// Mirrors the AdMob RewardedAd API: load() in background, show() at pause point.
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
//   // Show at a natural pause point — the hosted page handles everything
//   pending?.show({
//     onUserEarnedReward: (r) => r.amount === 1 && grantBonus(),
//     onAdDismissed: () => resumeGame(),
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
}

// The terminal-event protocol posted by the hosted /break page. Kept in sync
// with the HostMessage union documented at the top of
// platform/web/app/break/page.tsx (and mirrors sdk/react-native's HostMessage).
export type HostMessage =
  | { type: "ready" }
  | { type: "earnedReward"; payload: { amount: 0 | 1 } }
  | { type: "dismissed" }
  | { type: "error"; payload: { code: string; message: string } };

/** Default pre-`ready` load-timeout for the hosted /break page (ms). */
const DEFAULT_LOAD_TIMEOUT_MS = 15000;

interface WebAdSpec {
  placementId: string;
  format: BreakFormat;
  breakUrl: string;
  apiUrl?: string;
  studentToken?: string;
  mock?: boolean;
  /**
   * Pre-`ready` watchdog timeout (ms). Defaults to DEFAULT_LOAD_TIMEOUT_MS
   * when unset. 0 or negative disables the watchdog entirely.
   */
  loadTimeoutMs?: number;
}

export class LevelMomentWebAd {
  private _loaded = false;
  private _shown = false;
  private _disposed = false;
  private _iframe: HTMLIFrameElement | null = null;
  private _messageListener: ((event: MessageEvent) => void) | null = null;
  private _loadTimer: ReturnType<typeof setTimeout> | null = null;

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
      breakUrl: string;
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

    const ad = new LevelMomentWebAd({
      placementId: config.placementId,
      format: options.format ?? "flashcard",
      breakUrl: config.breakUrl,
      apiUrl: config.apiUrl,
      studentToken: config.studentToken,
      mock: config.mock,
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
    if (this._disposed || this._shown) return;
    if (!this._loaded) {
      // Mirrors the old onAdFailedToShow fallback: resume cleanly.
      callbacks.onAdDismissed?.();
      return;
    }
    this._shown = true;

    const url = this._buildUrl();
    const expectedOrigin = this._expectedOrigin();

    const teardown = (): void => {
      if (this._loadTimer !== null) {
        clearTimeout(this._loadTimer);
        this._loadTimer = null;
      }
      if (this._messageListener) {
        window.removeEventListener("message", this._messageListener);
        this._messageListener = null;
      }
      if (this._iframe && this._iframe.parentNode) {
        this._iframe.parentNode.removeChild(this._iframe);
      }
      this._iframe = null;
      this._shown = false;
    };

    const listener = (event: MessageEvent): void => {
      // Validate origin — ignore messages from any other frame/window.
      if (event.origin !== expectedOrigin) return;
      const msg = event.data as HostMessage;
      if (!msg || typeof msg.type !== "string") return;
      switch (msg.type) {
        case "ready":
          // The hosted page is up and now owns the lifecycle — cancel the
          // pre-`ready` watchdog. There is intentionally NO post-`ready`
          // timeout: a student legitimately thinking through a quiz must
          // never be force-closed.
          if (this._loadTimer !== null) {
            clearTimeout(this._loadTimer);
            this._loadTimer = null;
          }
          return;
        case "earnedReward":
          callbacks.onUserEarnedReward?.({
            type: "question_answered",
            amount: msg.payload.amount,
          });
          return;
        case "dismissed":
        case "error":
          teardown();
          callbacks.onAdDismissed?.();
          return;
      }
    };
    this._messageListener = listener;
    window.addEventListener("message", listener);

    const iframe = document.createElement("iframe");
    iframe.src = url;
    iframe.setAttribute("title", "LevelMoment break");
    iframe.style.cssText =
      "position:fixed;inset:0;width:100%;height:100%;border:0;z-index:2147483647";
    this._iframe = iframe;
    document.body.appendChild(iframe);

    // Pre-`ready` load-timeout watchdog. If the hosted page never posts
    // `ready` (it crashed, navigated away, or the network dropped it) and
    // the game never calls dispose(), the fullscreen iframe would otherwise
    // permanently cover the game and the listener would leak. On fire: clean
    // resume — identical to the not-loaded fallback at the top of show().
    const timeoutMs = this._spec.loadTimeoutMs ?? DEFAULT_LOAD_TIMEOUT_MS;
    if (timeoutMs > 0) {
      this._loadTimer = setTimeout(() => {
        // No-op if we were already torn down/disposed (race with a terminal
        // message or dispose()): teardown clears _loadTimer, but guard anyway.
        if (this._disposed || !this._shown) return;
        teardown();
        callbacks.onAdDismissed?.();
      }, timeoutMs);
    }
  }

  /** Release resources: tear down the iframe and remove the message listener. */
  dispose(): void {
    this._disposed = true;
    if (this._loadTimer !== null) {
      clearTimeout(this._loadTimer);
      this._loadTimer = null;
    }
    if (this._messageListener) {
      window.removeEventListener("message", this._messageListener);
      this._messageListener = null;
    }
    if (this._iframe && this._iframe.parentNode) {
      this._iframe.parentNode.removeChild(this._iframe);
    }
    this._iframe = null;
    this._shown = false;
  }

  private _buildUrl(): string {
    const params = new URLSearchParams();
    params.set("placementId", this._spec.placementId);
    params.set("format", this._spec.format);
    if (this._spec.mock) {
      params.set("mock", "true");
    } else {
      if (this._spec.apiUrl) params.set("apiUrl", this._spec.apiUrl);
      if (this._spec.studentToken) params.set("token", this._spec.studentToken);
    }
    const sep = this._spec.breakUrl.includes("?") ? "&" : "?";
    return `${this._spec.breakUrl}${sep}${params.toString()}`;
  }

  // The origin we accept postMessage from. Resolve breakUrl against the
  // current location so a relative same-origin "/break" yields this page's
  // origin (matching how the hosted page posts to window.parent).
  private _expectedOrigin(): string {
    const base =
      typeof window !== "undefined" ? window.location.href : undefined;
    return new URL(this._spec.breakUrl, base).origin;
  }
}
