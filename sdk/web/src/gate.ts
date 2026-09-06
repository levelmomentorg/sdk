import {
  addBridgeVersion,
  type UnsafeTestingOptions,
} from "@levelmoment/sdk-core";
// The startup sign-in gate for the web adapter.
//
// Both entry points open the hosted /break page in the same iframe + bridge
// machinery a break uses (./breakFrame), differing only in the `mode` param
// and whether the frame is visible:
//
//   ensureSignedIn → ?mode=gate   fullscreen; pairs if needed
//   isSignedIn     → ?mode=check  0x0 hidden; validates and closes
//
// Why isSignedIn() needs a frame at all: in the web adapter the device
// credential lives in the hosted page's localStorage, on the hosted origin.
// The SDK cannot read it — that is the point of putting it there — so the only
// place that can answer authoritatively is the hosted page itself. It calls
// POST /device-checks and posts the verdict back.

import type { EnsureSignedInResult } from "@levelmoment/sdk-core";
import {
  BreakFrame,
  DEFAULT_LOAD_TIMEOUT_MS,
  type HostMessage,
} from "./breakFrame.js";

export interface GateSpec {
  breakUrl: string;
  placementId: string;
  apiUrl?: string;
  /**
   * A device credential this game already holds — a sandbox token, or one read
   * from a parent-portal link. Handed to the hosted page over the bridge when
   * it asks, never on the URL. The page tests it alongside its own stored
   * credential. Normally unset: a paired device has its own.
   */
  studentToken?: string;
  unsafeTesting?: UnsafeTestingOptions;
  mock?: boolean;
  loadTimeoutMs?: number;
  /**
   * Total deadline for `isSignedIn()` (ms). Applies to the headless check only
   * — `ensureSignedIn()` has no post-`ready` deadline by design.
   */
  checkTimeoutMs?: number;
}

function accessSpec(spec: GateSpec): GateSpec {
  return { ...spec, breakUrl: new URL("/access", spec.breakUrl).toString() };
}

/**
 * Total deadline for the headless check (ms) — load, credential walk, verdict.
 *
 * The load watchdog only covers the page coming up, and the hosted page's own
 * `check_timeout` only covers the page still running. Neither survives a page
 * that loads and then goes silent for a reason the page cannot notice, so the
 * shell keeps one deadline over the whole thing. With default settings, the
 * 30-second total deadline is the final backstop after the 15-second load
 * watchdog and the page's 10-second deadline.
 *
 * Deliberately not applied to `ensureSignedIn()`: past `ready` a parent is
 * finding their phone, and no deadline belongs on that.
 */
export const DEFAULT_CHECK_TIMEOUT_MS = 30000;

function gateUrl(spec: GateSpec, mode: "gate" | "check"): string {
  const params = new URLSearchParams();
  addBridgeVersion(params, !!spec.unsafeTesting);
  params.set("mode", mode);
  params.set("placementId", spec.placementId);
  if (spec.mock) {
    params.set("mock", "true");
  } else if (spec.apiUrl) {
    params.set("apiUrl", spec.apiUrl);
  }
  // No credential on the URL — the page asks for one over the bridge. See
  // deliverGateCredential below.
  const sep = spec.breakUrl.includes("?") ? "&" : "?";
  return `${spec.breakUrl}${sep}${params.toString()}`;
}

/**
 * Answer the page's `needCredential`. The web adapter never takes custody: a
 * credential the hosted page mints belongs in that origin's own storage, which
 * is exactly where the page already keeps it, and copying it into the game's
 * page would only widen where it lives.
 */
function deliverGateCredential(frame: BreakFrame | null, spec: GateSpec): void {
  frame?.deliverCredential({
    token: spec.studentToken ?? "",
    custody: false,
  });
}

/**
 * Open the hosted gate and resolve once. `ready` on `signedIn`, `canceled` on
 * `dismissed`, `technicalFailure` on `error` or on the pre-`ready` watchdog.
 *
 * In mock mode there is nothing to link, so it resolves `ready` without
 * mounting anything.
 */
export function runEnsureSignedIn(
  spec: GateSpec,
): Promise<EnsureSignedInResult> {
  if (spec.mock) return Promise.resolve<EnsureSignedInResult>("ready");

  return new Promise<EnsureSignedInResult>((resolve) => {
    // Terminal-once: the hosted page can post more than one message on a
    // teardown race, and a publisher's startup branch must run exactly once.
    let settled = false;
    let frame: BreakFrame | null = null;
    const settle = (result: EnsureSignedInResult): void => {
      if (settled) return;
      settled = true;
      frame?.close();
      resolve(result);
    };

    try {
      frame = BreakFrame.open({
        url: gateUrl(spec, "gate"),
        title: "Level Moment sign-in",
        loadTimeoutMs: spec.loadTimeoutMs,
        onMessage: (msg: HostMessage) => {
          if (msg.type === "needCredential") {
            deliverGateCredential(frame, spec);
          } else if (msg.type === "signedIn") settle("ready");
          else if (msg.type === "dismissed") settle("canceled");
          else if (msg.type === "error") settle("technicalFailure");
        },
        onLoadTimeout: () => settle("technicalFailure"),
      });
    } catch {
      // A malformed breakUrl or a DOM that will not take the frame is a
      // technical failure, not an exception. This method's contract is three
      // values and nothing else: a publisher writes `switch (await
      // ensureSignedIn())` at startup, and a throw from it would crash the
      // boot path of a game whose only sin was a bad config string.
      settle("technicalFailure");
    }
  });
}

/** Open the hosted access coordinator without selecting or reserving content. */
export function runEnsureAccess(spec: GateSpec): Promise<EnsureSignedInResult> {
  try {
    return runEnsureSignedIn(accessSpec(spec));
  } catch {
    return Promise.resolve("technicalFailure");
  }
}

/**
 * Open the hidden hosted check and resolve the boolean it reports.
 *
 * Rejects — rather than resolving `false` — if the check fails technically,
 * exceeds the load timeout, or exceeds the total `checkTimeoutMs` deadline,
 * including load time. `false` is a claim about the household, and guessing it
 * from a network failure would push a linked player back through pairing.
 * Callers that want a lenient answer can catch and decide; the SDK will not
 * decide for them.
 */
export function runIsSignedIn(spec: GateSpec): Promise<boolean> {
  if (spec.mock) return Promise.resolve(true);

  return new Promise<boolean>((resolve, reject) => {
    let settled = false;
    let frame: BreakFrame | null = null;
    let deadline: ReturnType<typeof setTimeout> | null = null;
    const settle = (fn: () => void): void => {
      if (settled) return;
      settled = true;
      if (deadline !== null) clearTimeout(deadline);
      frame?.close();
      fn();
    };

    const timeoutMs = spec.loadTimeoutMs ?? DEFAULT_LOAD_TIMEOUT_MS;
    const totalMs = spec.checkTimeoutMs ?? DEFAULT_CHECK_TIMEOUT_MS;
    if (totalMs > 0) {
      // The backstop for a check that loaded and then said nothing terminal.
      // The load watchdog is already cancelled in that case, and the page's own
      // deadline cannot fire if the page is the thing that stopped running.
      deadline = setTimeout(() => {
        settle(() =>
          reject(
            new Error(
              `Level Moment sign-in check did not answer within ${totalMs}ms.`,
            ),
          ),
        );
      }, totalMs);
    }

    try {
      frame = BreakFrame.open({
        url: gateUrl(spec, "check"),
        title: "Level Moment sign-in check",
        hidden: true,
        loadTimeoutMs: timeoutMs,
        onMessage: (msg: HostMessage) => {
          if (msg.type === "needCredential") {
            deliverGateCredential(frame, spec);
          } else if (msg.type === "signedIn") settle(() => resolve(true));
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
      });
    } catch (err) {
      // Unlike ensureSignedIn, this one rejects: its contract is a boolean, and
      // there is no honest boolean for "the check never ran". Same rule as the
      // timeout and the error paths.
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

/** Check current learning access without selecting or reserving content. */
export function runCheckAccess(spec: GateSpec): Promise<boolean> {
  try {
    return runIsSignedIn(accessSpec(spec));
  } catch (err) {
    return Promise.reject(err);
  }
}
