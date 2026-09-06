import { resolveHostedOptions, HOSTED_BREAK_URL } from "@levelmoment/sdk-core";
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
  type EnsureSignedInResult,
} from "@levelmoment/sdk-core";
import { LevelMomentWebAd } from "./LevelMomentWebAd.js";
import {
  runCheckAccess,
  runEnsureAccess,
  runEnsureSignedIn,
  runIsSignedIn,
  type GateSpec,
} from "./gate.js";

/** Configure a game placement and optional test behavior. */
export type WebClientConfig = LevelMomentConfig & {
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
  /**
   * Total deadline for `isSignedIn()` (ms): load, credential check, verdict.
   * The default 30000 ms deadline rejects a stalled check instead of leaving it
   * pending. Set `0` or a negative value to disable this deadline. It covers
   * the one case `breakLoadTimeoutMs` cannot see — a check page that loads and
   * then goes silent. It does NOT apply to `ensureSignedIn()`, which waits
   * without a deadline once the pairing card is up, because a parent takes as
   * long as a parent takes.
   */
  signInCheckTimeoutMs?: number;
};

export class LevelMomentWebClient {
  readonly config: WebClientConfig;

  private constructor(config: WebClientConfig) {
    this.config = resolveHostedOptions(config);
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
   * Preload the next Level Moment in the background. Call this while the game
   * runs so it is ready to display instantly in the next ad slot.
   *
   * The returned LevelMomentWebAd opens the hosted /break page (which renders all
   * UI and owns the impression queue) on show() — no game-side question code.
   */
  /**
   * Run the startup sign-in gate. Call it once before enabling gameplay and
   * start the game only on `"ready"`.
   *
   * It opens the hosted Level Moment surface in a fullscreen iframe. When the
   * device already holds a valid credential for this game the surface closes
   * almost immediately; otherwise it runs the ask-a-parent pairing flow and
   * waits for approval.
   *
   * Resolves exactly once:
   * - `"ready"` — start the game.
   * - `"canceled"` — a person closed the gate or a parent denied the
   *   connection. Show your own "learning breaks are off" state or a retry
   *   action; do not retry automatically.
   * - `"technicalFailure"` — the gate could not run. Retry later.
   *
   * None of the three reveals subscription, tier, or quota. With
   * `mock: true` it resolves `"ready"` immediately and shows no UI.
   */
  ensureSignedIn(): Promise<EnsureSignedInResult> {
    return runEnsureSignedIn(this._gateSpec());
  }

  /** Open the access flow after a player deliberately chooses learning. */
  ensureAccess(): Promise<EnsureSignedInResult> {
    return runEnsureAccess(this._gateSpec());
  }

  /**
   * Ask whether this device holds a valid Level Moment credential for the
   * game. This is an authoritative server-checked answer, not a cached flag:
   * it opens a hidden hosted frame that validates the stored credential
   * through the API, because that credential lives on the Level Moment origin
   * where this SDK cannot read it.
   *
   * Rejects if the check fails technically, exceeds `breakLoadTimeoutMs`, or
   * exceeds the total `signInCheckTimeoutMs` deadline, including load time. It
   * does not resolve `false`
   * in that case: `false` is a claim about the household, and guessing it from
   * a network failure would push a linked player back through pairing. Treat a
   * rejection as "unknown, try again", not as signed out.
   *
   * With `mock: true` it resolves `true`.
   */
  isSignedIn(): Promise<boolean> {
    return runIsSignedIn(this._gateSpec());
  }

  /** Check whether learning is available. `false` means an action is needed. */
  checkAccess(): Promise<boolean> {
    return runCheckAccess(this._gateSpec());
  }

  loadAd(
    callbacks: {
      onAdLoaded: (ad: LevelMomentWebAd) => void;
      onAdFailedToLoad?: (error: LevelMomentAdError) => void;
    },
    options?: { format?: BreakFormat },
  ): void {
    LevelMomentWebAd.load(
      { ...this.config, breakUrl: this._breakUrl() },
      {
        onAdLoaded: callbacks.onAdLoaded,
        onAdFailedToLoad: callbacks.onAdFailedToLoad ?? (() => {}),
      },
      options,
    );
  }

  /**
   * Where the hosted surface lives. The production URL is owned by Level Moment.
   */
  private _breakUrl(): string {
    return this.config.breakUrl ?? HOSTED_BREAK_URL;
  }

  private _gateSpec(): GateSpec {
    return {
      breakUrl: this._breakUrl(),
      placementId: this.config.placementId,
      apiUrl: this.config.apiUrl,
      studentToken: this.config.studentToken,
      unsafeTesting: this.config.unsafeTesting,
      mock: this.config.mock,
      loadTimeoutMs: this.config.breakLoadTimeoutMs,
      checkTimeoutMs: this.config.signInCheckTimeoutMs,
    };
  }
}
