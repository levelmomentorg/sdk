import {
  resolveHostedOptions,
  addBridgeVersion,
  type HostedOptions,
} from "@levelmoment/sdk-core";
// Mirrors the react-native-google-mobile-ads RewardedAd API for drop-in replacement.
// See MIGRATION.md for a line-by-line swap guide.
//
// The SDK is a thin shell over a WebView pointing at the hosted break page.
// The page (platform/web/app/break) renders all question UI and posts back
// terminal events. See docs/ADR-001-webview-rendering.md.
//
// Add <LevelMomentAdModal /> once at your app root so show() can present the WebView.

import type {
  LevelMomentAdError,
  RewardItem,
  BreakFormat,
} from "@levelmoment/sdk-core";
import { _hasModalHandler, _triggerModal } from "./modalHost.js";
import {
  HOST_CAPABILITIES,
  HOST_CAPABILITIES_PARAM,
  type HostMessage,
} from "./hostMessage.js";
import {
  applyCredentialMessage,
  credentialResponder,
} from "./credentialBridge.js";

export type LevelMomentAdEvent =
  | "loaded"
  | "error"
  | "opened"
  | "closed"
  | "earnedReward";

type EventPayloadMap = {
  loaded: undefined;
  error: LevelMomentAdError;
  opened: undefined;
  closed: undefined;
  earnedReward: RewardItem;
};

type Listener<E extends LevelMomentAdEvent> = (
  payload: EventPayloadMap[E],
) => void;

export interface LevelMomentAdOptions extends HostedOptions {
  /** Break format: flashcard | quiz | deep_dive (default: flashcard). */
  format?: BreakFormat;
  /** Use bundled mock questions instead of the live API. */
  mock?: boolean;
  /** Opaque game-server context, sent privately with the break handshake. */
  customData?: string;
}

export class LevelMomentAd {
  private readonly placementId: string;
  private readonly options: ReturnType<
    typeof resolveHostedOptions<LevelMomentAdOptions>
  >;
  private _loaded = false;
  private _shown = false;
  private _finished = false;
  private _rewardIds = new Set<string>();
  private _disposed = false;
  private readonly _listeners: Map<
    LevelMomentAdEvent,
    Set<Listener<LevelMomentAdEvent>>
  > = new Map();

  private constructor(placementId: string, options: LevelMomentAdOptions) {
    this.placementId = placementId;
    this.options = resolveHostedOptions(options);
  }

  static createForAdRequest(
    placementId: string,
    options: LevelMomentAdOptions = {},
  ): LevelMomentAd {
    return new LevelMomentAd(placementId, options);
  }

  addAdEventListener<E extends LevelMomentAdEvent>(
    event: E,
    listener: Listener<E>,
  ): () => void {
    if (!this._listeners.has(event)) this._listeners.set(event, new Set());
    this._listeners.get(event)!.add(listener as Listener<LevelMomentAdEvent>);
    return () =>
      this._listeners
        .get(event)
        ?.delete(listener as Listener<LevelMomentAdEvent>);
  }

  /**
   * Mark the ad ready to show. The actual question fetch happens inside the
   * WebView when show() is called — there is no separate native preload step
   * in this architecture. Kept as a separate call so consumers can stay on the
   * familiar AdMob load → show pattern.
   */
  load(): void {
    if (this._disposed) return;
    this._loaded = true;
    queueMicrotask(() => this._emit("loaded", undefined));
  }

  /**
   * Open the hosted break page in a fullscreen WebView. The page handles all
   * question rendering and posts back terminal events: 'earnedReward' fires
   * once per answer, then 'closed' fires when the session ends.
   */
  show(): void {
    if (this._disposed || this._finished) {
      return;
    }
    if (!this._loaded) {
      this._emit("error", {
        code: "not_loaded",
        message: "Call load() before show().",
      });
      return;
    }
    if (!_hasModalHandler()) {
      this._emit("error", {
        code: "unknown",
        message:
          "LevelMomentAdModal is not mounted. Add <LevelMomentAdModal /> at your app root.",
      });
      return;
    }
    if (this._shown) return;
    this._shown = true;

    _triggerModal({
      url: this._buildUrl(),
      onMessage: (msg) => this._handleMessage(msg),
      onNeedCredential: credentialResponder(
        this.placementId,
        this.options.studentToken,
        undefined,
        this.options.customData,
        !!this.options.unsafeTesting || !!this.options.mock,
      ),
    });

    this._emit("opened", undefined);
  }

  dispose(): void {
    this._disposed = true;
    this._listeners.clear();
  }

  private _buildUrl(): string {
    const params = new URLSearchParams();
    params.set("placementId", this.placementId);
    params.set("format", this.options.format ?? "flashcard");
    if (this.options.mock) {
      params.set("mock", "true");
    } else if (this.options.apiUrl) {
      params.set("apiUrl", this.options.apiUrl);
    }
    // No credential rides on this URL. The page asks over the bridge
    // (`needCredential`) and the answer comes from the keychain, so a live
    // token never reaches a launch URL, a crash report, or a web log.
    // SSV-parity: carry the host-supplied customData to the hosted page in both
    // modes (opaque correlation data, not a credential) so the page stamps it
    // on every impression it records.
    addBridgeVersion(params, !!this.options.unsafeTesting);
    params.set(HOST_CAPABILITIES_PARAM, HOST_CAPABILITIES);
    const sep = this.options.breakUrl.includes("?") ? "&" : "?";
    return `${this.options.breakUrl}${sep}${params.toString()}`;
  }

  private _handleMessage(msg: HostMessage): void {
    // Keep the keychain in step with the page: store what pairing minted,
    // forget what the server refused. The host answers `needCredential`
    // itself — it is the only side that can reach into the WebView.
    if (
      !this.options.unsafeTesting &&
      !this.options.mock &&
      applyCredentialMessage(msg, this.placementId)
    )
      return;
    switch (msg.type) {
      case "ready":
      case "needCredential":
        return;
      case "earnedReward":
        if (msg.payload.rewardId) {
          if (this._rewardIds.has(msg.payload.rewardId)) return;
          this._rewardIds.add(msg.payload.rewardId);
        }
        this._emit("earnedReward", {
          type: "question_answered",
          amount: msg.payload.amount,
          ...(msg.payload.rewardId ? { rewardId: msg.payload.rewardId } : {}),
        });
        return;
      // `signedIn` belongs to the sign-in gate and never reaches a break. If one
      // ever arrives the modal has closed, so resume the game rather than
      // leaving it waiting for a `closed` that will not come.
      case "signedIn":
      case "dismissed":
        this._shown = false;
        this._finished = true;
        this._emit("closed", undefined);
        return;
      case "error":
        this._finished = true;
        this._emit("error", {
          code: (msg.payload.code as LevelMomentAdError["code"]) ?? "unknown",
          message: msg.payload.message,
        });
        return;
    }
  }

  private _emit<E extends LevelMomentAdEvent>(
    event: E,
    payload: EventPayloadMap[E],
  ): void {
    this._listeners.get(event)?.forEach((listener) => {
      (listener as (p: EventPayloadMap[E]) => void)(payload);
    });
  }
}

export type { HostMessage } from "./hostMessage.js";
