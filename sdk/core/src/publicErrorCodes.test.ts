// ---------------------------------------------------------------------------
// What this package tells a publisher is a boundary, not just a type.
//
// `LevelMomentAdErrorCode` is what a publisher's game branches on. A household's
// entitlement is private Level Moment state (the platform plan's "Publisher
// visibility" decision), so no member of this union may name it. The guard is
// both compile-time and run-time because the compile-time half only holds while
// someone keeps reading the type, and the run-time half only holds while the
// literal spelling stays the same.
//
// The second half of the boundary is that the package offers no side door
// either. An "internal" export is not a boundary — it ships to npm with
// everything else, and an adapter author reads the source. Whatever the SDK
// knows, every consumer can know; so it must not know why a break was refused.
// ---------------------------------------------------------------------------

import { describe, it, expect } from "vitest";
import { readFileSync, readdirSync } from "node:fs";
import { fileURLToPath } from "node:url";
import * as publicApi from "./index.js";
import type { LevelMomentAdErrorCode } from "./types.js";
import {
  HOSTED_BREAK_URL,
  isBridgeMessage,
  resolveHostedOptions,
  sameHostedOrigin,
} from "./hosted.js";

/** Compiles only while `T` is `never`. */
type Empty<T extends never> = T;

// Re-adding "subscription_required" (or any entitlement code) to the union
// fails `npm run type-check` here, before any test runs.
export type NoEntitlementCode = Empty<
  Extract<
    LevelMomentAdErrorCode,
    "subscription_required" | "trial_expired" | "past_due" | "quota_exceeded"
  >
>;

const TYPES_SRC = readFileSync(
  fileURLToPath(new URL("./types.ts", import.meta.url)),
  "utf8",
);

describe("public ad error codes", () => {
  it("is exactly the five neutral codes", () => {
    const union = /export type LevelMomentAdErrorCode =([\s\S]*?);/.exec(
      TYPES_SRC,
    );
    expect(union).not.toBeNull();

    const members = [...union![1].matchAll(/"([a-z_]+)"/g)].map((m) => m[1]);
    expect(members).toEqual([
      "network_error",
      "no_fill",
      "invalid_token",
      "not_loaded",
      "unknown",
    ]);
  });

  it("names no entitlement state anywhere in the public types", () => {
    // `types.ts` is the whole public type surface — every interface a game
    // touches lives here. A word about billing appearing in it is the signal
    // that private state has leaked into the publisher's contract.
    for (const word of [
      "subscription",
      "trial",
      "past_due",
      "entitlement",
      "quota",
    ]) {
      expect(TYPES_SRC.toLowerCase()).not.toContain(`"${word}`);
    }
  });
});

describe("no side door out of the SDK", () => {
  it("exports nothing that could carry a refusal reason", () => {
    // Every name a consumer can reach through the package entry. An
    // `internalRefusalReason` or `recordRefusalReason` here would be a
    // published API however it were labelled.
    for (const name of Object.keys(publicApi)) {
      expect(name.toLowerCase()).not.toContain("internal");
      expect(name.toLowerCase()).not.toContain("refusal");
    }
  });

  it("ships no module the entry does not reach", () => {
    // A second entry point (an `internal.ts` behind a `./internal` subpath)
    // would be invisible to the check above while being just as public. The
    // package has exactly one entry, so every non-test module must be
    // reachable from it — and a new top-level module is a deliberate act that
    // this list makes someone confirm.
    const dir = fileURLToPath(new URL(".", import.meta.url));
    const modules = readdirSync(dir)
      .filter((f) => f.endsWith(".ts") && !f.endsWith(".test.ts"))
      .sort();
    expect(modules).toEqual(["hosted.ts", "index.ts", "types.ts"]);
  });
});

describe("hosted surface boundary", () => {
  it("defaults to the canonical hosted break", () => {
    expect(resolveHostedOptions({}).breakUrl).toBe(HOSTED_BREAK_URL);
  });

  it("rejects custom destinations unless unsafeTesting is explicit", () => {
    expect(() =>
      resolveHostedOptions({
        placementId: "pl-1",
        breakUrl: "https://localhost:3000/break",
      }),
    ).toThrow(/unsafeTesting/);
    expect(
      resolveHostedOptions({
        placementId: "pl-1",
        unsafeTesting: {
          breakUrl: "https://localhost:3000/break",
          apiUrl: "https://localhost:8080",
          token: "test-token",
        },
      }),
    ).toMatchObject({ breakUrl: "https://localhost:3000/break" });
  });

  it("rejects credentials, queries, and fragments in test URLs", () => {
    for (const breakUrl of [
      "https://user:pass@localhost/break",
      "https://localhost/break?x=1",
      "https://localhost/break#x",
    ]) {
      expect(() =>
        resolveHostedOptions({
          placementId: "pl-1",
          unsafeTesting: { breakUrl },
        }),
      ).toThrow(/HTTP\(S\) test URL/);
    }
  });

  it("requires the exact hosted origin", () => {
    expect(sameHostedOrigin(HOSTED_BREAK_URL, HOSTED_BREAK_URL)).toBe(true);
    for (const url of [
      "https://levelmoment.com.evil.example/break",
      "https://user@levelmoment.com/break",
      "https://levelmoment.com:8443/break",
    ]) {
      expect(sameHostedOrigin(url, HOSTED_BREAK_URL)).toBe(false);
    }
  });
});

describe("hosted bridge validation", () => {
  it("accepts only a well-formed graded reward", () => {
    expect(
      isBridgeMessage({
        type: "earnedReward",
        payload: { amount: 1, rewardId: "opaque-reward-id" },
        protocolVersion: 1,
      }),
    ).toBe(true);
  });

  it("rejects malformed earned rewards before an adapter can grant one", () => {
    for (const message of [
      { type: "earnedReward" },
      { type: "earnedReward", payload: {} },
      { type: "earnedReward", payload: { amount: 2 } },
      { type: "earnedReward", payload: { amount: "1" } },
      { type: "earnedReward", payload: { amount: 1, rewardId: "" } },
      { type: "earnedReward", payload: { amount: 1, rewardId: 42 } },
      {
        type: "earnedReward",
        payload: { amount: 1, rewardId: "a".repeat(257) },
      },
    ]) {
      expect(isBridgeMessage(message)).toBe(false);
    }
  });

  it("rejects a message from another bridge protocol version", () => {
    expect(
      isBridgeMessage({
        type: "earnedReward",
        payload: { amount: 1 },
        protocolVersion: 2,
      }),
    ).toBe(false);
  });
});
