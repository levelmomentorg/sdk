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
import { _hasModalHandler, _triggerModal } from "./LevelMomentAdModal.js";

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

export interface LevelMomentAdOptions {
  /** API base URL — required unless `mock: true`. */
  apiUrl?: string;
  /** Student session token — required unless `mock: true`. */
  studentToken?: string;
  /** Hosted break page URL, e.g. https://app.levelmoment.com/break. */
  breakUrl: string;
  /** Break format: flashcard | quiz | deep_dive (default: flashcard). */
  format?: BreakFormat;
  /** Use bundled mock questions instead of the live API. */
  mock?: boolean;
  /**
   * SSV-parity custom data (see `LevelMomentConfig.customData` in sdk-core).
   * Forwarded to the hosted break page as the `customData` query param so the
   * page stamps it on every impression it records (and the server echoes it on
   * reward.earned). Omitted from the URL when unset.
   */
  customData?: string;
}

export class LevelMomentAd {
  private readonly placementId: string;
  private readonly options: LevelMomentAdOptions;
  private _loaded = false;
  private _shown = false;
  private _disposed = false;
  private readonly _listeners: Map<
    LevelMomentAdEvent,
    Set<Listener<LevelMomentAdEvent>>
  > = new Map();

  private constructor(placementId: string, options: LevelMomentAdOptions) {
    this.placementId = placementId;
    this.options = options;
  }

  static createForAdRequest(
    placementId: string,
    options: LevelMomentAdOptions,
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
    if (this._disposed) {
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
    } else {
      if (this.options.apiUrl) params.set("apiUrl", this.options.apiUrl);
      if (this.options.studentToken)
        params.set("token", this.options.studentToken);
    }
    // SSV-parity: carry the host-supplied customData to the hosted page in both
    // modes (opaque correlation data, not a credential) so the page stamps it
    // on every impression it records.
    if (this.options.customData)
      params.set("customData", this.options.customData);
    const sep = this.options.breakUrl.includes("?") ? "&" : "?";
    return `${this.options.breakUrl}${sep}${params.toString()}`;
  }

  private _handleMessage(msg: HostMessage): void {
    switch (msg.type) {
      case "ready":
        return;
      case "earnedReward":
        this._emit("earnedReward", {
          type: "question_answered",
          amount: msg.payload.amount,
        });
        return;
      case "dismissed":
        this._shown = false;
        this._emit("closed", undefined);
        return;
      case "error":
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

export type HostMessage =
  | { type: "ready" }
  | { type: "earnedReward"; payload: { amount: 0 | 1 } }
  | { type: "dismissed" }
  | { type: "error"; payload: { code: string; message: string } };
