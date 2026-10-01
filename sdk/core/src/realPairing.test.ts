import { afterEach, describe, expect, it, vi } from "vitest";
import {
  addBridgeVersion,
  credentialStoreKey,
  isRealPairingUrl,
  resolveHostedOptions,
  type HostEnvironment,
} from "./hosted.js";

// Shared vectors: the Flutter and Unity SDKs pin the same table. See
// docs/decisions/sdk-real-pairing-testing-2026-10-01.md, invariant 1.
const ALLOWED = [
  "http://localhost:3000/break",
  "http://127.0.0.1:3000/break",
  "http://LOCALHOST:3000/break",
];
const REFUSED = [
  "https://levelmoment.com/break",
  "https://dev.levelmoment.com/break",
  "http://localhost/break",
  "http://localhost:80/break",
  "http://localhost:0/break",
  "https://localhost:3000/break",
  "http://localhost.:3000/break",
  "http://127.1:3000/break",
  "http://[::1]:3000/break",
  "http://10.0.2.2:3000/break",
  "http://192.168.1.20:3000/break",
  "http://user@localhost:3000/break",
  "http://localhost:3000/break?x=1",
  "http://localhost:3000/break#x",
  "http://localhost:3000/break?",
  "http://localhost:3000/break#",
  "http:\\\\localhost:3000/break",
  "http://local\thost:3000/break",
  "http://localhost.evil.example:3000/break",
  "capacitor://localhost/break",
];

const NATIVE_DEBUG: HostEnvironment = { kind: "native", debugBuild: true };
const NATIVE_RELEASE: HostEnvironment = { kind: "native", debugBuild: false };
const WEB_DEV: HostEnvironment = {
  kind: "web",
  pageUrl: "http://127.0.0.1:5173/",
};

function realPairing(breakUrl: string, extra: object = {}) {
  return {
    placementId: "pl-1",
    unsafeTesting: { realPairing: true, breakUrl },
    ...extra,
  };
}

afterEach(() => {
  vi.restoreAllMocks();
});

describe("real pairing URL vectors", () => {
  it.each(ALLOWED)("allows %s", (url) => {
    expect(isRealPairingUrl(url)).toBe(true);
  });

  it.each(REFUSED)("refuses %s", (url) => {
    expect(isRealPairingUrl(url)).toBe(false);
  });
});

describe("resolveHostedOptions under realPairing", () => {
  it("resolves to realPairing with no apiUrl and no token", () => {
    vi.spyOn(console, "warn").mockImplementation(() => {});
    const resolved = resolveHostedOptions(
      realPairing("http://localhost:3000/break"),
      NATIVE_DEBUG,
    );
    expect(resolved.mode).toBe("realPairing");
    expect(resolved.breakUrl).toBe("http://localhost:3000/break");
    expect(resolved.apiUrl).toBeUndefined();
    expect(resolved.studentToken).toBeUndefined();
  });

  it("loads the re-serialised URL, not the caller's string", () => {
    vi.spyOn(console, "warn").mockImplementation(() => {});
    expect(
      resolveHostedOptions(
        realPairing("http://LOCALHOST:3000/break"),
        NATIVE_DEBUG,
      ).breakUrl,
    ).toBe("http://localhost:3000/break");
  });

  it("resolving twice returns the first result and warns once", () => {
    const warn = vi.spyOn(console, "warn").mockImplementation(() => {});
    const once = resolveHostedOptions(
      realPairing("http://127.0.0.1:4100/break"),
      NATIVE_DEBUG,
    );
    expect(resolveHostedOptions(once, NATIVE_RELEASE)).toBe(once);
    expect(warn).toHaveBeenCalledTimes(1);
  });

  it("is refused outside a debug native build", () => {
    expect(() =>
      resolveHostedOptions(
        realPairing("http://localhost:3000/break"),
        NATIVE_RELEASE,
      ),
    ).toThrow(/debug build/);
  });

  it.each([
    "https://my-game.example/",
    "http://localhost/",
    "https://localhost/",
    "capacitor://localhost/",
    "http://192.168.1.20:5173/",
    "",
    "http://localhost:3000@evil.example/",
    "http://user@localhost:3000/",
    "https://localhost:3000/",
    "http://localhost.evil.example:3000/",
    "http://localhost.:3000/",
    "http://evil.example/?u=http://localhost:3000/",
    "http://localhost:80/",
  ])("is refused on the web page %s", (pageUrl) => {
    expect(() =>
      resolveHostedOptions(realPairing("http://localhost:3000/break"), {
        kind: "web",
        pageUrl,
      }),
    ).toThrow(/page served from/);
  });

  it("is allowed on a local web dev server", () => {
    vi.spyOn(console, "warn").mockImplementation(() => {});
    expect(
      resolveHostedOptions(realPairing("http://localhost:3000/break"), WEB_DEV)
        .mode,
    ).toBe("realPairing");
  });

  it.each([
    [
      "a token",
      {
        unsafeTesting: {
          realPairing: true,
          breakUrl: "http://localhost:3000/break",
          token: "eply_sbx_x",
        },
      },
    ],
    ["a studentToken", { studentToken: "tok" }],
    [
      "an apiUrl",
      {
        unsafeTesting: {
          realPairing: true,
          breakUrl: "http://localhost:3000/break",
          apiUrl: "http://localhost:8080",
        },
      },
    ],
    ["a top-level apiUrl", { apiUrl: "http://localhost:8080" }],
    ["mock", { mock: true }],
    [
      "a different top-level breakUrl",
      { breakUrl: "http://localhost:4000/break" },
    ],
  ])("refuses %s", (_label, extra) => {
    expect(() =>
      resolveHostedOptions(
        realPairing("http://localhost:3000/break", extra),
        NATIVE_DEBUG,
      ),
    ).toThrow(/realPairing/);
  });

  it.each(REFUSED)("refuses the break URL %s", (breakUrl) => {
    expect(() =>
      resolveHostedOptions(realPairing(breakUrl), NATIVE_DEBUG),
    ).toThrow(/realPairing/);
  });

  it("refuses realPairing with no breakUrl", () => {
    expect(() =>
      resolveHostedOptions(
        { placementId: "pl-1", unsafeTesting: { realPairing: true } },
        NATIVE_DEBUG,
      ),
    ).toThrow(/breakUrl/);
  });
});

describe("resolved values cannot be forged or carried over", () => {
  it("re-validates a spread copy with a changed breakUrl", () => {
    const resolved = resolveHostedOptions({}, NATIVE_RELEASE);
    expect(() =>
      resolveHostedOptions(
        { ...resolved, breakUrl: "https://elsewhere.example/break" },
        NATIVE_RELEASE,
      ),
    ).toThrow(/owns its hosted URLs/);
  });

  it("ignores a mode a caller names itself", () => {
    expect(() =>
      resolveHostedOptions(
        {
          mode: "realPairing",
          unsafeTesting: {
            realPairing: true,
            breakUrl: "http://localhost:3000/break",
          },
        } as object,
        NATIVE_RELEASE,
      ),
    ).toThrow(/debug build/);
  });

  it("resolves a JSON copy of a real-pairing value again", () => {
    vi.spyOn(console, "warn").mockImplementation(() => {});
    const resolved = resolveHostedOptions(
      realPairing("http://localhost:3000/break"),
      NATIVE_DEBUG,
    );
    const copy = JSON.parse(JSON.stringify(resolved));
    expect(resolveHostedOptions(copy, NATIVE_DEBUG).mode).toBe("realPairing");
  });
});

describe("the local game page", () => {
  it.each([
    "http://127.0.0.1:5173/?mock=0",
    "http://127.0.0.1:5173/#/play",
    "http://localhost:3003/game/index.html?level=4",
  ])("allows %s", (pageUrl) => {
    vi.spyOn(console, "warn").mockImplementation(() => {});
    expect(
      resolveHostedOptions(realPairing("http://localhost:3000/break"), {
        kind: "web",
        pageUrl,
      }).mode,
    ).toBe("realPairing");
  });
});

describe("mode and the break URL", () => {
  it("adds sandbox only in sandbox mode", () => {
    for (const [mode, sandbox] of [
      ["production", null],
      ["sandbox", "true"],
      ["realPairing", null],
    ] as const) {
      const params = new URLSearchParams();
      addBridgeVersion(params, mode);
      expect(params.get("sandbox")).toBe(sandbox);
    }
  });

  it("keeps production and plain unsafeTesting behaviour", () => {
    expect(resolveHostedOptions({}, NATIVE_RELEASE).mode).toBe("production");
    expect(
      resolveHostedOptions(
        { unsafeTesting: { breakUrl: "https://localhost:3000/break" } },
        NATIVE_RELEASE,
      ).mode,
    ).toBe("sandbox");
  });
});

describe("credentialStoreKey", () => {
  it("keeps the production key unchanged", () => {
    expect(credentialStoreKey("https://levelmoment.com", "pl_abc")).toBe(
      "com.levelmoment.credential.pl_abc",
    );
  });

  it("puts any other origin under the test prefix", () => {
    expect(credentialStoreKey("http://localhost:3000", "pl_abc")).toBe(
      "com.levelmoment.test-credential.http_localhost_3000.pl_abc",
    );
    expect(credentialStoreKey("http://127.0.0.1:3000", "pl_abc")).toBe(
      "com.levelmoment.test-credential.http_127.0.0.1_3000.pl_abc",
    );
  });

  it("cannot be made to produce a production key from a test origin", () => {
    const key = credentialStoreKey("http://localhost:3000", "x");
    expect(key.startsWith("com.levelmoment.credential.")).toBe(false);
  });
});
