// LevelMomentWebClient — one-time initialisation + loadAd() convenience.
//
// This is a thin shell over the hosted /break page (ADR-001). The hosted
// page owns the impression queue and its flush loop, so this client no longer
// holds an ImpressionQueue — it just resolves the break URL and forwards to
// LevelMomentWebAd.load(). Structurally mirrors sdk/react-native (queue-less shell).

import {
  type LevelMomentConfig,
  type LevelMomentAdError,
  type BreakFormat,
} from "@levelmoment/sdk-core";
import { LevelMomentWebAd } from "./LevelMomentWebAd.js";

/**
 * Web client config: the shared core config plus an optional `breakUrl`.
 * Games served same-origin as the hosted Next app can omit `breakUrl`
 * (it defaults to `<origin>/break`); cross-origin games must set it.
 */
export type WebClientConfig = LevelMomentConfig & {
  breakUrl?: string;
  /** Use the hosted page's bundled mock questions instead of the live API. */
  mock?: boolean;
  /**
   * Pre-`ready` load-timeout watchdog (ms). If the hosted /break page never
   * posts `ready` within this window after show() — it crashed, navigated
   * away, or the network dropped it — the fullscreen iframe is torn down and
   * onAdDismissed fires so the game can resume. Default 15000. A value of 0
   * (or negative) disables the watchdog. The watchdog only guards the
   * pre-`ready` phase: once `ready` arrives the hosted page owns the
   * lifecycle and is never force-closed (a student thinking through a quiz
   * must not be interrupted).
   */
  breakLoadTimeoutMs?: number;
};

export class LevelMomentWebClient {
  readonly config: WebClientConfig;

  private constructor(config: WebClientConfig) {
    this.config = config;
  }

  static initialize(config: WebClientConfig): LevelMomentWebClient {
    return new LevelMomentWebClient(config);
  }

  /**
   * Kept for API compatibility — games still call client.start() at boot.
   * The impression flush loop moved into the hosted /break page (ADR-001),
   * so there is nothing for the shell to start. No-op.
   */
  start(): void {}

  /**
   * Kept for API compatibility — games still call client.stop() on teardown.
   * The impression flush loop lives in the hosted page now. No-op.
   */
  stop(): void {}

  /**
   * Preload the next break in the background. Call this while the game runs
   * so the ad is ready to display instantly at the natural pause point.
   *
   * The returned LevelMomentWebAd opens the hosted /break page (which renders all
   * UI and owns the impression queue) on show() — no game-side question code.
   */
  loadAd(
    callbacks: {
      onAdLoaded: (ad: LevelMomentWebAd) => void;
      onAdFailedToLoad?: (error: LevelMomentAdError) => void;
    },
    options?: { format?: BreakFormat },
  ): void {
    const breakUrl =
      this.config.breakUrl ??
      (typeof window !== "undefined"
        ? `${window.location.origin}/break`
        : "/break");

    LevelMomentWebAd.load(
      { ...this.config, breakUrl },
      {
        onAdLoaded: callbacks.onAdLoaded,
        onAdFailedToLoad: callbacks.onAdFailedToLoad ?? (() => {}),
      },
      options,
    );
  }
}
