import {
  resolveHostedOptions,
  addBridgeVersion,
  type HostedOptions,
} from "@levelmoment/sdk-core";
// The startup sign-in gate for the React Native adapter.
//
// Both entry points open the hosted /break page through the same modal host a
// break uses (./modalHost), differing only in the `mode` param and whether the
// WebView is visible:
//
//   ensureSignedIn → ?mode=gate   fullscreen modal; pairs if needed
//   isSignedIn     → ?mode=check  off-screen; validates and closes
//   signOut        → ?mode=clear  off-screen; drops the hosted credential
//
// Why isSignedIn() needs a WebView at all: the device credential lives in the
// hosted page's storage, on the hosted origin. The SDK cannot read it — that is
// the point of putting it there — so the only place that can answer
// authoritatively is the hosted page itself. It calls POST /device-checks and
// posts the verdict back.
//
// Mirrors sdk/web/src/gate.ts message-for-message.

import type { EnsureSignedInResult } from "@levelmoment/sdk-core";
import {
  HOST_CAPABILITIES,
  HOST_CAPABILITIES_PARAM,
  type HostMessage,
} from "./hostMessage.js";
import {
  _hasModalHandler,
  _triggerModal,
  type ModalHandle,
} from "./modalHost.js";
import {
  applyCredentialMessage,
  credentialResponder,
} from "./credentialBridge.js";
import { deviceCredentials } from "./tokenStore.js";

/** Default pre-`ready` load-timeout for the hosted page (ms). */
export const DEFAULT_LOAD_TIMEOUT_MS = 15_000;

/**
 * Default total deadline for the headless check (ms) — load, credential walk,
 * verdict.
 *
 * The load watchdog only covers the page coming up, and the hosted page's own
 * `check_timeout` only covers the page still running. Neither survives a
 * WebView that loads and then goes silent, so `isSignedIn()` keeps one deadline
 * over the whole thing. With default settings, the 30-second total deadline is
 * the final backstop after the 15-second load watchdog and the page's
 * 10-second deadline.
 *
 * `ensureSignedIn()` has no equivalent: past `ready` a parent is finding their
 * phone, and no deadline belongs on that.
 */
export const DEFAULT_CHECK_TIMEOUT_MS = 30_000;

export interface SignInOptions extends HostedOptions {
  /** Public placement ID from the developer portal. */
  placementId: string;
  /** Resolve without opening anything, for offline demos and tests. */
  mock?: boolean;
  /** Pre-`ready` watchdog in ms. Defaults to 15000. */
  loadTimeoutMs?: number;
  /**
   * Total deadline for `isSignedIn()` in ms — load, check, verdict. Defaults to
   * 30000; `0` (or negative) disables it. Ignored by `ensureSignedIn()`, which
   * has no post-`ready` deadline by design.
   */
  checkTimeoutMs?: number;
}

/**
 * Build the hosted gate URL. Exported for the unit tests; treat as internal.
 */
export function buildGateUrl(
  options: SignInOptions,
  mode: "gate" | "check" | "clear",
  coordinatorPath?: "/access",
): string {
  const resolved = resolveHostedOptions(options);
  const hostedUrl = coordinatorPath
    ? new URL(coordinatorPath, resolved.breakUrl).toString()
    : resolved.breakUrl;
  const params = new URLSearchParams();
  addBridgeVersion(params, !!options.unsafeTesting);
  params.set("mode", mode);
  params.set("placementId", options.placementId);
  if (options.mock) {
    params.set("mock", "true");
  } else if (resolved.apiUrl) {
    params.set("apiUrl", resolved.apiUrl);
  }
  // No credential on the URL — the page asks for one over the bridge.
  params.set(HOST_CAPABILITIES_PARAM, HOST_CAPABILITIES);
  const sep = hostedUrl.includes("?") ? "&" : "?";
  return `${hostedUrl}${sep}${params.toString()}`;
}

/**
 * Map a hosted-page message to a gate result. Returns null for the messages
 * that do not end the gate (`ready`, `earnedReward`).
 */
export function gateResultFor(msg: HostMessage): EnsureSignedInResult | null {
  if (msg.type === "signedIn") return "ready";
  if (msg.type === "dismissed") return "canceled";
  if (msg.type === "error") return "technicalFailure";
  return null;
}

const NOT_MOUNTED =
  "LevelMomentAdModal is not mounted. Add <LevelMomentAdModal /> at your app root.";

/**
 * Run the startup sign-in gate. Call it once before enabling gameplay and start
 * the game only on `"ready"`.
 *
 * It opens the hosted Level Moment surface in a fullscreen modal. A device that
 * already holds a valid credential for this game passes through in a moment;
 * otherwise the surface runs the ask-a-parent pairing flow and waits for the
 * answer.
 *
 * Resolves exactly once:
 * - `"ready"` — start the game.
 * - `"canceled"` — a person closed the gate or a parent denied the connection.
 *   Show your own "learning breaks are off" state or a retry action; do not
 *   retry automatically.
 * - `"technicalFailure"` — the gate could not run. Retry later.
 *
 * None of the three reveals subscription, tier, or quota. It never rejects: a
 * missing <LevelMomentAdModal /> or a malformed `breakUrl` resolves
 * `"technicalFailure"`, because a publisher writes `switch (await
 * ensureSignedIn())` in their boot path and a throw there would crash a game
 * over a bad config string. With `mock: true` it resolves `"ready"` without
 * showing anything.
 */
function runEnsureGate(
  options: SignInOptions,
  coordinatorPath?: "/access",
): Promise<EnsureSignedInResult> {
  if (options.mock) return Promise.resolve<EnsureSignedInResult>("ready");

  return new Promise<EnsureSignedInResult>((resolve) => {
    // Terminal-once: the hosted page can post more than one message on a
    // teardown race, and a publisher's startup branch must run exactly once.
    let settled = false;
    const settle = (result: EnsureSignedInResult): void => {
      if (settled) return;
      settled = true;
      resolve(result);
    };

    if (!_hasModalHandler()) {
      settle("technicalFailure");
      return;
    }

    try {
      _triggerModal({
        url: buildGateUrl(options, "gate", coordinatorPath),
        loadTimeoutMs: options.loadTimeoutMs,
        onNeedCredential: credentialResponder(
          options.placementId,
          resolveHostedOptions(options).studentToken,
          undefined,
          undefined,
          !!options.unsafeTesting,
        ),
        onMessage: (msg) => {
          // Pairing runs inside the gate, so this is where a first credential
          // is usually minted — keep it before deciding the gate's result.
          if (
            !options.unsafeTesting &&
            applyCredentialMessage(msg, options.placementId)
          )
            return;
          const result = gateResultFor(msg);
          if (result) settle(result);
        },
        onLoadTimeout: () => settle("technicalFailure"),
        // A second gate, or a break, took the visible slot. This one will never
        // hear a verdict, and a startup branch left pending forever is worse
        // than one told to try again.
        onDisplaced: () => settle("technicalFailure"),
      });
    } catch {
      settle("technicalFailure");
    }
  });
}

export function ensureSignedIn(
  options: SignInOptions,
): Promise<EnsureSignedInResult> {
  return runEnsureGate(options);
}

/** Open the hosted access flow after a player deliberately chooses learning. */
export function ensureAccess(
  options: SignInOptions,
): Promise<EnsureSignedInResult> {
  return runEnsureGate(options, "/access");
}

/**
 * Ask whether this device holds a valid Level Moment credential for the game.
 * This is an authoritative server-checked answer, not a cached flag: it opens an
 * off-screen WebView that validates the stored credential through the API,
 * because that credential lives on the Level Moment origin where this SDK cannot
 * read it.
 *
 * Rejects — rather than resolving `false` — if the check fails technically,
 * exceeds the load timeout, exceeds the total `checkTimeoutMs` deadline
 * (including load time), or <LevelMomentAdModal /> is not mounted. `false` is a
 * claim about the
 * household, and guessing it from a network failure would push a linked player
 * back through pairing. Treat a rejection as "unknown, try again", not as signed
 * out. With `mock: true` it resolves `true`.
 */
function runCheck(
  options: SignInOptions,
  coordinatorPath?: "/access",
): Promise<boolean> {
  if (options.mock) return Promise.resolve(true);

  return new Promise<boolean>((resolve, reject) => {
    let settled = false;
    let deadline: ReturnType<typeof setTimeout> | null = null;
    const settle = (fn: () => void): void => {
      if (settled) return;
      settled = true;
      if (deadline !== null) clearTimeout(deadline);
      fn();
    };

    if (!_hasModalHandler()) {
      settle(() =>
        reject(
          new Error(
            `Level Moment could not open the sign-in check: ${NOT_MOUNTED}`,
          ),
        ),
      );
      return;
    }

    const timeoutMs = options.loadTimeoutMs ?? DEFAULT_LOAD_TIMEOUT_MS;
    const totalMs = options.checkTimeoutMs ?? DEFAULT_CHECK_TIMEOUT_MS;
    let handle: ModalHandle | null = null;
    if (totalMs > 0) {
      // The backstop for a check that loaded and then said nothing terminal.
      // The load watchdog is already cancelled by then, and the page's own
      // deadline cannot fire if the page is the thing that stopped running.
      //
      // This fires exactly when no message is coming, so nothing else will
      // clear the hidden slot. Close it here, the way a terminal message and
      // the watchdog both do, or the silent WebView keeps running JS and
      // holding a network context until another check displaces it.
      deadline = setTimeout(() => {
        settle(() => {
          handle?.close();
          reject(
            new Error(
              `Level Moment sign-in check did not answer within ${totalMs}ms.`,
            ),
          );
        });
      }, totalMs);
    }

    try {
      handle = _triggerModal({
        url: buildGateUrl(options, "check", coordinatorPath),
        hidden: true,
        loadTimeoutMs: options.loadTimeoutMs,
        onNeedCredential: credentialResponder(
          options.placementId,
          resolveHostedOptions(options).studentToken,
          undefined,
          undefined,
          !!options.unsafeTesting,
        ),
        onMessage: (msg) => {
          // The check does not pair, but it does discard credentials the
          // server refuses — the keychain has to hear about that.
          if (
            !options.unsafeTesting &&
            applyCredentialMessage(msg, options.placementId)
          )
            return;
          if (msg.type === "signedIn") settle(() => resolve(true));
          else if (msg.type === "dismissed") settle(() => resolve(false));
          else if (msg.type === "error") {
            settle(() =>
              reject(
                new Error(
                  `Level Moment could not check this device: ${msg.payload.code}`,
                ),
              ),
            );
          }
        },
        onLoadTimeout: () =>
          settle(() =>
            reject(
              new Error(
                `Level Moment sign-in check timed out after ${timeoutMs}ms.`,
              ),
            ),
          ),
        // A second check started before this one answered. There is one hidden
        // slot, so this request is gone — reject it rather than leave the caller
        // awaiting a WebView that is no longer mounted. Same rule as the other
        // no-answer paths: this is "unknown", never "signed out".
        onDisplaced: () =>
          settle(() =>
            reject(
              new Error(
                "Level Moment replaced this sign-in check with a newer one.",
              ),
            ),
          ),
      });
    } catch (err) {
      // Unlike ensureSignedIn, this one rejects: its contract is a boolean, and
      // there is no honest boolean for "the check never ran".
      settle(() =>
        reject(
          new Error(
            `Level Moment could not open the sign-in check: ${
              err instanceof Error ? err.message : String(err)
            }`,
          ),
        ),
      );
    }
  });
}

export function isSignedIn(options: SignInOptions): Promise<boolean> {
  return runCheck(options);
}

/** Check whether learning is available. `false` means an action is needed. */
export function checkAccess(options: SignInOptions): Promise<boolean> {
  return runCheck(options, "/access");
}

/**
 * Sign this device out of Level Moment for one placement.
 *
 * The credential lives in two places, and clearing one is worse than clearing
 * neither: wipe only the keychain and the next `ensureSignedIn()` finds the
 * copy still sitting in the hosted origin's storage, validates it, and signs
 * the previous learner straight back in. On a shared device that is the whole
 * problem this call exists to solve. So it clears both.
 *
 * Order is keychain first, hosted second. The hosted clear answers with
 * `credentialInvalid`, which drives a second keychain wipe through the normal
 * message path — so the fragile half runs last and repairs the durable half on
 * its way out.
 *
 * It is not atomic. If the hosted clear cannot run, this rejects with the
 * keychain already emptied, and the household is still signed in; call it again
 * when there is a network. Resolving would claim a sign-out that did not
 * happen.
 *
 * Rejects — rather than resolving quietly — when <LevelMomentAdModal /> is not
 * mounted, the surface will not open, or the page never confirms.
 */
export function signOut(options: SignInOptions): Promise<void> {
  if (options.mock) return Promise.resolve();

  return new Promise<void>((resolve, reject) => {
    let settled = false;
    let deadline: ReturnType<typeof setTimeout> | null = null;
    let handle: ModalHandle | null = null;
    const settle = (fn: () => void): void => {
      if (settled) return;
      settled = true;
      if (deadline !== null) clearTimeout(deadline);
      handle?.close();
      fn();
    };

    if (!_hasModalHandler()) {
      reject(
        new Error(
          `Level Moment could not sign this device out: ${NOT_MOUNTED}`,
        ),
      );
      return;
    }

    void (
      options.unsafeTesting
        ? Promise.resolve()
        : deviceCredentials.clear(options.placementId)
    ).then(() => {
      const totalMs = options.checkTimeoutMs ?? DEFAULT_CHECK_TIMEOUT_MS;
      if (totalMs > 0) {
        deadline = setTimeout(() => {
          settle(() =>
            reject(
              new Error(
                `Level Moment sign-out did not finish within ${totalMs}ms. The device credential on this app is cleared; the hosted one may remain.`,
              ),
            ),
          );
        }, totalMs);
      }

      try {
        handle = _triggerModal({
          url: buildGateUrl(options, "clear"),
          hidden: true,
          loadTimeoutMs: options.loadTimeoutMs,
          onMessage: (msg) => {
            // `credentialInvalid` re-clears the keychain through the shared
            // path; `dismissed` is the page's terminal for this mode.
            if (!options.unsafeTesting)
              applyCredentialMessage(msg, options.placementId);
            if (msg.type === "dismissed") settle(resolve);
            else if (msg.type === "error") {
              settle(() =>
                reject(
                  new Error(
                    `Level Moment could not clear the hosted credential: ${msg.payload.code}`,
                  ),
                ),
              );
            }
          },
          onLoadTimeout: () =>
            settle(() =>
              reject(
                new Error(
                  "Level Moment could not reach the hosted page to finish signing out.",
                ),
              ),
            ),
          onDisplaced: () =>
            settle(() =>
              reject(
                new Error(
                  "Level Moment replaced this sign-out with a newer request.",
                ),
              ),
            ),
        });
      } catch (err) {
        settle(() =>
          reject(
            new Error(
              `Level Moment could not sign this device out: ${
                err instanceof Error ? err.message : String(err)
              }`,
            ),
          ),
        );
      }
    });
  });
}
