// unsafeTesting.realPairing in the React Native shell. The contract is in
// docs/decisions/sdk-real-pairing-testing-2026-10-01.md: no sandbox flag, no
// apiUrl, a debug build only, and a keychain slot that can never be the
// production one.

import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import {
  buildGateUrl,
  checkAccess,
  ensureAccess,
  ensureSignedIn,
  isSignedIn,
  signOut,
} from "./gate.js";
import { LevelMomentAd } from "./LevelMomentAd.js";
import { deviceCredentials } from "./tokenStore.js";
import { _registerModalHandler, type ModalShowParams } from "./modalHost.js";

const PRODUCTION_ORIGIN = "https://levelmoment.com";
const LOCAL_ORIGIN = "http://localhost:3000";
const OPTIONS = {
  placementId: "game-42",
  unsafeTesting: {
    realPairing: true,
    breakUrl: "http://localhost:3000/break",
  },
};

let shown: ModalShowParams[];
let origins: string[];

beforeEach(() => {
  vi.stubGlobal("__DEV__", true);
  vi.spyOn(console, "warn").mockImplementation(() => {});
  shown = [];
  origins = [];
  _registerModalHandler((params) => {
    shown.push(params);
    return { close: () => {} };
  });
  // Record the origin of every keychain access; nothing is really stored.
  vi.spyOn(deviceCredentials, "isAvailable").mockResolvedValue(true);
  vi.spyOn(deviceCredentials, "get").mockImplementation(async (origin) => {
    origins.push(origin);
    return "";
  });
  vi.spyOn(deviceCredentials, "set").mockImplementation(async (origin) => {
    origins.push(origin);
  });
  vi.spyOn(deviceCredentials, "clear").mockImplementation(async (origin) => {
    origins.push(origin);
  });
});

afterEach(() => {
  _registerModalHandler(null);
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

async function showLocalBreak(): Promise<void> {
  const ad = LevelMomentAd.createForAdRequest("game-42", OPTIONS);
  ad.load();
  await Promise.resolve();
  ad.show();
}

/** Answer the page's credential request, then pair and refuse, as a page can. */
async function driveCredentialTraffic(params: ModalShowParams): Promise<void> {
  await params.onNeedCredential?.();
  params.onMessage({ type: "credentialIssued", payload: { token: "minted" } });
  params.onMessage({ type: "credentialInvalid" });
  params.onMessage({ type: "dismissed" });
  await Promise.resolve();
}

describe("realPairing URLs", () => {
  it("builds the gate URL with neither sandbox nor apiUrl", () => {
    const url = new URL(buildGateUrl(OPTIONS, "gate"));
    expect(url.origin).toBe(LOCAL_ORIGIN);
    expect(url.searchParams.has("sandbox")).toBe(false);
    expect(url.searchParams.has("apiUrl")).toBe(false);
  });

  it("builds the break URL with neither sandbox nor apiUrl", async () => {
    await showLocalBreak();
    const url = new URL(shown[0]!.url);
    expect(url.origin).toBe(LOCAL_ORIGIN);
    expect(url.searchParams.has("sandbox")).toBe(false);
    expect(url.searchParams.has("apiUrl")).toBe(false);
  });

  it("is refused in a release build", () => {
    vi.stubGlobal("__DEV__", false);
    expect(() => buildGateUrl(OPTIONS, "gate")).toThrow(/debug build/);
    expect(() => LevelMomentAd.createForAdRequest("game-42", OPTIONS)).toThrow(
      /debug build/,
    );
  });
});

describe("realPairing never touches the production keychain slot", () => {
  it.each([
    ["ensureSignedIn", () => void ensureSignedIn(OPTIONS)],
    ["ensureAccess", () => void ensureAccess(OPTIONS)],
    ["isSignedIn", () => void isSignedIn(OPTIONS).catch(() => {})],
    ["checkAccess", () => void checkAccess(OPTIONS).catch(() => {})],
  ])("%s uses only the test slot", async (_name, start) => {
    start();
    await driveCredentialTraffic(shown[0]!);
    expect(origins.length).toBeGreaterThan(0);
    expect(origins).not.toContain(PRODUCTION_ORIGIN);
    expect(new Set(origins)).toEqual(new Set([LOCAL_ORIGIN]));
  });

  it("signOut clears only the test slot", async () => {
    const done = signOut(OPTIONS).catch(() => {});
    await vi.waitFor(() => expect(shown).toHaveLength(1));
    await driveCredentialTraffic(shown[0]!);
    await done;
    expect(origins).not.toContain(PRODUCTION_ORIGIN);
    expect(origins).toContain(LOCAL_ORIGIN);
  });

  it("a break uses only the test slot", async () => {
    await showLocalBreak();
    await driveCredentialTraffic(shown[0]!);
    expect(origins.length).toBeGreaterThan(0);
    expect(new Set(origins)).toEqual(new Set([LOCAL_ORIGIN]));
  });

  it("production still uses the production slot", async () => {
    void ensureSignedIn({ placementId: "game-42" });
    await driveCredentialTraffic(shown[0]!);
    expect(new Set(origins)).toEqual(new Set([PRODUCTION_ORIGIN]));
  });

  it("sandbox still touches no slot at all", async () => {
    void ensureSignedIn({
      placementId: "game-42",
      unsafeTesting: { breakUrl: "https://localhost:3000/break" },
    });
    await driveCredentialTraffic(shown[0]!);
    expect(origins).toEqual([]);
  });
});
