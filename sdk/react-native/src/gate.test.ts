// Unit tests for the startup sign-in gate.
//
// The gate reaches the WebView through the module-level modal host, so a fake
// host stands in for <LevelMomentAdModal /> and the whole contract — URL
// building, message mapping, terminal-once, the watchdog, mock mode, and the
// not-mounted path — is exercised as plain TypeScript. Mirrors
// sdk/web/src/gate.test.ts.

import { afterEach, describe, expect, it, vi } from "vitest";
import {
  buildGateUrl,
  checkAccess,
  ensureAccess,
  ensureSignedIn,
  gateResultFor,
  isSignedIn,
  signOut,
} from "./gate.js";
import { deviceCredentials } from "./tokenStore.js";
import type { HostMessage } from "./hostMessage.js";
import { _registerModalHandler, type ModalShowParams } from "./modalHost.js";

const OPTIONS = {
  unsafeTesting: {},
  breakUrl: "https://app.levelmoment.com/break",
  placementId: "game-42",
  apiUrl: "https://api.levelmoment.com",
  studentToken: "tok abc/&=",
};
const PRODUCTION_OPTIONS = {
  breakUrl: "https://levelmoment.com/break",
  placementId: "game-42",
};

/**
 * Register a fake host and hand back the params the gate passed it, plus the
 * live occupants of the two slots.
 *
 * It reproduces the one-visible-slot / one-hidden-slot rule LevelMomentAdModal
 * enforces — displacement and the returned close handle included — so the gate
 * is exercised against the same contract the real host offers.
 */
function mountFakeHost(): {
  current: ModalShowParams | null;
  occupants: Record<string, ModalShowParams | null>;
} {
  const captured: {
    current: ModalShowParams | null;
    occupants: Record<string, ModalShowParams | null>;
  } = { current: null, occupants: { visible: null, hidden: null } };
  _registerModalHandler((params) => {
    const slot = params.hidden ? "hidden" : "visible";
    captured.occupants[slot]?.onDisplaced?.();
    captured.occupants[slot] = params;
    captured.current = params;
    return {
      close: () => {
        if (captured.occupants[slot] !== params) return;
        captured.occupants[slot] = null;
      },
    };
  });
  return captured;
}

afterEach(() => {
  _registerModalHandler(null);
  vi.useRealTimers();
});

describe("buildGateUrl", () => {
  it("carries mode, placement and apiUrl in live mode — and no credential", () => {
    const url = new URL(buildGateUrl(OPTIONS, "gate"));
    expect(url.origin + url.pathname).toBe("https://app.levelmoment.com/break");
    expect(url.searchParams.get("mode")).toBe("gate");
    expect(url.searchParams.get("placementId")).toBe("game-42");
    expect(url.searchParams.get("apiUrl")).toBe("https://api.levelmoment.com");
    expect(url.searchParams.has("mock")).toBe(false);
    // The credential travels over the bridge. A token in a URL ends up in
    // launch links, crash reports, and web logs; this is the assertion that
    // keeps it out.
    expect(url.searchParams.has("token")).toBe(false);
    expect(buildGateUrl(OPTIONS, "gate")).not.toContain("tok%20abc");
  });

  it("uses mode=check for the headless check", () => {
    const url = new URL(buildGateUrl(OPTIONS, "check"));
    expect(url.searchParams.get("mode")).toBe("check");
  });

  it("omits apiUrl and token in mock mode", () => {
    const url = new URL(buildGateUrl({ ...OPTIONS, mock: true }, "gate"));
    expect(url.searchParams.get("mock")).toBe("true");
    expect(url.searchParams.has("apiUrl")).toBe(false);
    expect(url.searchParams.has("token")).toBe(false);
  });

  it("announces openExternal support — an old shell that cannot open a browser must not be offered the button", () => {
    const url = new URL(buildGateUrl(OPTIONS, "gate"));
    expect(url.searchParams.get("caps")).toBe("openExternal");
  });

  it("rejects a break URL carrying a query", () => {
    expect(() =>
      buildGateUrl(
        {
          ...OPTIONS,
          unsafeTesting: {
            ...OPTIONS.unsafeTesting,
            breakUrl: "https://app.levelmoment.com/break?theme=dark",
          },
        },
        "gate",
      ),
    ).toThrow(/query|fragment|HTTP\(S\)/i);
  });
});

describe("learning access", () => {
  it("uses /access for both visible and headless flows", async () => {
    const host = mountFakeHost();
    const visible = ensureAccess(OPTIONS);
    await vi.waitFor(() => expect(host.current).not.toBeNull());
    expect(new URL(host.current!.url).pathname).toBe("/access");
    expect(new URL(host.current!.url).searchParams.get("mode")).toBe("gate");
    host.current!.onMessage({ type: "signedIn" });
    await expect(visible).resolves.toBe("ready");

    const check = checkAccess(OPTIONS);
    await vi.waitFor(() => expect(host.current).not.toBeNull());
    expect(host.current!.hidden).toBe(true);
    expect(new URL(host.current!.url).pathname).toBe("/access");
    host.current!.onMessage({ type: "dismissed" });
    await expect(check).resolves.toBe(false);
  });
});

describe("gateResultFor", () => {
  it("maps the three terminal messages", () => {
    expect(gateResultFor({ type: "signedIn" })).toBe("ready");
    expect(gateResultFor({ type: "dismissed" })).toBe("canceled");
    expect(
      gateResultFor({ type: "error", payload: { code: "x", message: "y" } }),
    ).toBe("technicalFailure");
  });

  it("returns null for messages that do not end the gate", () => {
    expect(gateResultFor({ type: "ready" })).toBeNull();
    expect(
      gateResultFor({ type: "earnedReward", payload: { amount: 1 } }),
    ).toBeNull();
  });
});

describe("ensureSignedIn", () => {
  it("opens the gate-mode surface visibly", async () => {
    const host = mountFakeHost();
    const pending = ensureSignedIn(OPTIONS);
    expect(host.current?.hidden).toBeFalsy();
    expect(new URL(host.current!.url).searchParams.get("mode")).toBe("gate");
    host.current!.onMessage({ type: "signedIn" });
    await expect(pending).resolves.toBe("ready");
  });

  it("resolves canceled on dismissed", async () => {
    const host = mountFakeHost();
    const pending = ensureSignedIn(OPTIONS);
    host.current!.onMessage({ type: "dismissed" });
    await expect(pending).resolves.toBe("canceled");
  });

  it("resolves technicalFailure on error", async () => {
    const host = mountFakeHost();
    const pending = ensureSignedIn(OPTIONS);
    host.current!.onMessage({
      type: "error",
      payload: { code: "network_error", message: "offline" },
    });
    await expect(pending).resolves.toBe("technicalFailure");
  });

  it("ignores ready and resolves once, on the first terminal message", async () => {
    const host = mountFakeHost();
    const pending = ensureSignedIn(OPTIONS);
    host.current!.onMessage({ type: "ready" });
    host.current!.onMessage({ type: "signedIn" });
    host.current!.onMessage({ type: "dismissed" });
    host.current!.onMessage({
      type: "error",
      payload: { code: "late", message: "ignored" },
    });
    await expect(pending).resolves.toBe("ready");
  });

  it("resolves technicalFailure when the watchdog fires", async () => {
    const host = mountFakeHost();
    const pending = ensureSignedIn(OPTIONS);
    host.current!.onLoadTimeout!();
    await expect(pending).resolves.toBe("technicalFailure");
  });

  it("resolves technicalFailure when the modal is not mounted", async () => {
    await expect(ensureSignedIn(OPTIONS)).resolves.toBe("technicalFailure");
  });

  it("resolves technicalFailure instead of throwing when the host cannot mount", async () => {
    _registerModalHandler(() => {
      throw new Error("no WebView available");
    });
    await expect(ensureSignedIn(OPTIONS)).resolves.toBe("technicalFailure");
  });

  it("resolves ready in mock mode without opening anything", async () => {
    const host = mountFakeHost();
    await expect(ensureSignedIn({ ...OPTIONS, mock: true })).resolves.toBe(
      "ready",
    );
    expect(host.current).toBeNull();
  });

  it("resolves technicalFailure when a second surface takes the slot", async () => {
    const host = mountFakeHost();
    const first = ensureSignedIn(OPTIONS);
    const second = ensureSignedIn(OPTIONS);

    await expect(first).resolves.toBe("technicalFailure");

    host.current!.onMessage({ type: "signedIn" });
    await expect(second).resolves.toBe("ready");
  });
});

describe("isSignedIn", () => {
  it("opens the check-mode surface hidden", async () => {
    const host = mountFakeHost();
    const pending = isSignedIn(OPTIONS);
    expect(host.current?.hidden).toBe(true);
    expect(new URL(host.current!.url).searchParams.get("mode")).toBe("check");
    host.current!.onMessage({ type: "signedIn" });
    await expect(pending).resolves.toBe(true);
  });

  it("resolves false on dismissed", async () => {
    const host = mountFakeHost();
    const pending = isSignedIn(OPTIONS);
    host.current!.onMessage({ type: "dismissed" });
    await expect(pending).resolves.toBe(false);
  });

  it("rejects rather than answering false on a technical failure", async () => {
    const host = mountFakeHost();
    const pending = isSignedIn(OPTIONS);
    host.current!.onMessage({
      type: "error",
      payload: { code: "network_error", message: "offline" },
    });
    await expect(pending).rejects.toThrow(/network_error/);
  });

  it("rejects when the check never answers", async () => {
    const host = mountFakeHost();
    const pending = isSignedIn({ ...OPTIONS, loadTimeoutMs: 250 });
    host.current!.onLoadTimeout!();
    await expect(pending).rejects.toThrow(/timed out after 250ms/);
  });

  it("rejects when the modal is not mounted", async () => {
    await expect(isSignedIn(OPTIONS)).rejects.toThrow(
      /LevelMomentAdModal is not mounted/,
    );
  });

  it("rejects instead of throwing when the host cannot mount", async () => {
    _registerModalHandler(() => {
      throw new Error("no WebView available");
    });
    await expect(isSignedIn(OPTIONS)).rejects.toThrow(/no WebView available/);
  });

  it("settles once — a second message after the first is ignored", async () => {
    const host = mountFakeHost();
    const pending = isSignedIn(OPTIONS);
    host.current!.onMessage({ type: "signedIn" });
    host.current!.onMessage({
      type: "error",
      payload: { code: "late", message: "ignored" },
    });
    host.current!.onLoadTimeout!();
    await expect(pending).resolves.toBe(true);
  });

  it("resolves true in mock mode without opening anything", async () => {
    const host = mountFakeHost();
    await expect(isSignedIn({ ...OPTIONS, mock: true })).resolves.toBe(true);
    expect(host.current).toBeNull();
  });

  it("rejects the first check when a second one takes the slot", async () => {
    const host = mountFakeHost();
    const first = isSignedIn(OPTIONS);
    const second = isSignedIn(OPTIONS);

    await expect(first).rejects.toThrow(/replaced this sign-in check/);

    // The survivor still answers normally on the slot it now owns.
    host.current!.onMessage({ type: "signedIn" });
    await expect(second).resolves.toBe(true);
  });

  it("ignores a displacement that arrives after the check answered", async () => {
    const host = mountFakeHost();
    const pending = isSignedIn(OPTIONS);
    host.current!.onMessage({ type: "dismissed" });
    host.current!.onDisplaced!();
    await expect(pending).resolves.toBe(false);
  });

  it("rejects when the check loads and then never answers", async () => {
    // `ready` cancels the load watchdog, so past that point only the total
    // deadline can free a check whose page went silent.
    vi.useFakeTimers();
    const host = mountFakeHost();
    const pending = isSignedIn({ ...OPTIONS, checkTimeoutMs: 8000 });
    host.current!.onMessage({ type: "ready" });
    vi.advanceTimersByTime(8000);
    await expect(pending).rejects.toThrow(/did not answer within 8000ms/);
  });

  it("takes the silent WebView down when the total deadline fires", async () => {
    // Nothing else will: this path fires precisely because no message is
    // coming, so the hidden surface would keep running JS until another check
    // displaced it.
    vi.useFakeTimers();
    const host = mountFakeHost();
    const pending = isSignedIn({ ...OPTIONS, checkTimeoutMs: 8000 });
    expect(host.occupants.hidden).not.toBeNull();

    host.current!.onMessage({ type: "ready" });
    vi.advanceTimersByTime(8000);

    await expect(pending).rejects.toThrow(/did not answer/);
    expect(host.occupants.hidden).toBeNull();
  });

  it("leaves the survivor's slot alone when a check is displaced", async () => {
    const host = mountFakeHost();
    const first = isSignedIn({ ...OPTIONS, checkTimeoutMs: 8000 });
    const second = isSignedIn({ ...OPTIONS, checkTimeoutMs: 8000 });

    // Displacement settles the first check, which clears its deadline — so
    // nothing of the first check's can ever reach the slot the second now owns.
    await expect(first).rejects.toThrow(/replaced this sign-in check/);
    expect(host.occupants.hidden).toBe(host.current);

    host.current!.onMessage({ type: "signedIn" });
    await expect(second).resolves.toBe(true);
  });

  it("cancels the total deadline once a verdict arrives", async () => {
    vi.useFakeTimers();
    const host = mountFakeHost();
    const pending = isSignedIn({ ...OPTIONS, checkTimeoutMs: 8000 });
    host.current!.onMessage({ type: "signedIn" });
    vi.advanceTimersByTime(60_000);
    await expect(pending).resolves.toBe(true);
  });
});

describe("the deadline contract the two entry points do not share", () => {
  it("leaves ensureSignedIn pending after ready — pairing has no deadline", async () => {
    vi.useFakeTimers();
    const host = mountFakeHost();
    const pending = ensureSignedIn({ ...OPTIONS, checkTimeoutMs: 8000 });
    host.current!.onMessage({ type: "ready" });
    vi.advanceTimersByTime(600_000);

    const race = await Promise.race([
      pending,
      Promise.resolve("still-pending" as const),
    ]);
    expect(race).toBe("still-pending");

    host.current!.onMessage({ type: "signedIn" });
    await expect(pending).resolves.toBe("ready");
  });
});

describe("host messages the gate must not act on", () => {
  it("leaves the gate pending on earnedReward", async () => {
    const host = mountFakeHost();
    const pending = ensureSignedIn(OPTIONS);
    const reward: HostMessage = {
      type: "earnedReward",
      payload: { amount: 1 },
    };
    host.current!.onMessage(reward);
    const race = await Promise.race([
      pending,
      Promise.resolve("still-pending" as const),
    ]);
    expect(race).toBe("still-pending");
    host.current!.onMessage({ type: "dismissed" });
    await expect(pending).resolves.toBe("canceled");
  });

  describe("signOut", () => {
    it("opens the hosted clear surface off-screen, with no credential on the URL", async () => {
      const host = mountFakeHost();
      const clearSpy = vi
        .spyOn(deviceCredentials, "clear")
        .mockResolvedValue(undefined);

      const pending = signOut(PRODUCTION_OPTIONS);
      await vi.waitFor(() => expect(host.current).not.toBeNull());

      const url = new URL(host.current!.url);
      expect(url.searchParams.get("mode")).toBe("clear");
      expect(url.searchParams.get("placementId")).toBe("game-42");
      expect(url.searchParams.has("token")).toBe(false);
      expect(host.current!.hidden).toBe(true);

      host.current!.onMessage({ type: "dismissed" });
      await expect(pending).resolves.toBeUndefined();
      clearSpy.mockRestore();
    });

    it("clears the keychain before opening the hosted surface", async () => {
      // Ordering matters: the hosted clear answers with `credentialInvalid`,
      // which re-clears the keychain on its way out. Running the durable half
      // first means the fragile half repairs it rather than racing it.
      const host = mountFakeHost();
      const order: string[] = [];
      const clearSpy = vi
        .spyOn(deviceCredentials, "clear")
        .mockImplementation(async () => {
          order.push("keychain");
        });

      const pending = signOut(PRODUCTION_OPTIONS);
      await vi.waitFor(() => expect(host.current).not.toBeNull());
      order.push("hosted");

      host.current!.onMessage({ type: "dismissed" });
      await pending;
      expect(order).toEqual(["keychain", "hosted"]);
      clearSpy.mockRestore();
    });

    it("rejects when the hosted clear fails — the household is still signed in", async () => {
      // The keychain is empty by now but the hosted copy is not, so the next
      // ensureSignedIn() would sign the same learner back in. Resolving would
      // claim a sign-out that did not happen.
      const host = mountFakeHost();
      const clearSpy = vi
        .spyOn(deviceCredentials, "clear")
        .mockResolvedValue(undefined);

      const pending = signOut(PRODUCTION_OPTIONS);
      await vi.waitFor(() => expect(host.current).not.toBeNull());
      host.current!.onMessage({
        type: "error",
        payload: { code: "network_error", message: "offline" },
      });

      await expect(pending).rejects.toThrow(/could not clear the hosted/i);
      clearSpy.mockRestore();
    });

    it("rejects when the modal host is not mounted", async () => {
      await expect(signOut(OPTIONS)).rejects.toThrow(/not mounted/i);
    });

    it("resolves without touching either store in mock mode", async () => {
      const clearSpy = vi.spyOn(deviceCredentials, "clear");
      await expect(
        signOut({ ...OPTIONS, mock: true }),
      ).resolves.toBeUndefined();
      expect(clearSpy).not.toHaveBeenCalled();
      clearSpy.mockRestore();
    });
  });
});
