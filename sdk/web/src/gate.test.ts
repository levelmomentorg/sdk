// Tests for the startup sign-in gate. Same DOM-stub approach as client.test.ts
// (vitest env is `node`): a fake window/document lets us mount the frame, emit
// hosted-page messages at chosen origins, and drive the watchdog with fake
// timers.

import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";

import { LevelMomentWebClient } from "./client.js";

const CONFIG = {
  unsafeTesting: {},
  apiUrl: "https://api.test.levelmoment.com",
  placementId: "placement-abc",
  studentToken: "device-tok-123",
  breakUrl: "https://app.levelmoment.com/break",
};

const HOST_ORIGIN = "https://app.levelmoment.com";

interface FakeIframe {
  tagName: "IFRAME";
  src: string;
  style: { cssText: string };
  parentNode: FakeBody | null;
  attrs: Record<string, string>;
  /** Stands in for the real iframe's WindowProxy — see client.test.ts. */
  contentWindow: { frame: symbol; postMessage: ReturnType<typeof vi.fn> };
  setAttribute: (k: string, v: string) => void;
}

interface FakeBody {
  children: FakeIframe[];
  appendChild: (el: FakeIframe) => void;
  removeChild: (el: FakeIframe) => void;
}

let messageListeners: Array<(e: MessageEvent) => void>;
let body: FakeBody;

function emit(data: unknown, origin = HOST_ORIGIN, source?: unknown): void {
  const from = source ?? body.children[0]?.contentWindow;
  for (const l of [...messageListeners])
    l({ data, origin, source: from } as MessageEvent);
}

function frame(): FakeIframe {
  return body.children[0];
}

beforeEach(() => {
  messageListeners = [];
  body = {
    children: [],
    appendChild(el) {
      el.parentNode = body;
      body.children.push(el);
    },
    removeChild(el) {
      body.children = body.children.filter((c) => c !== el);
      el.parentNode = null;
    },
  };

  vi.stubGlobal("window", {
    location: {
      href: "https://game.test.com/play",
      origin: "https://game.test.com",
    },
    addEventListener: vi.fn((type: string, l: (e: MessageEvent) => void) => {
      if (type === "message") messageListeners.push(l);
    }),
    removeEventListener: vi.fn((type: string, l: (e: MessageEvent) => void) => {
      if (type === "message")
        messageListeners = messageListeners.filter((x) => x !== l);
    }),
  });

  vi.stubGlobal("document", {
    body,
    createElement: (tag: string): FakeIframe => {
      const el: FakeIframe = {
        tagName: tag.toUpperCase() as "IFRAME",
        src: "",
        style: { cssText: "" },
        parentNode: null,
        attrs: {},
        contentWindow: {
          frame: Symbol("contentWindow"),
          postMessage: vi.fn(),
        },
        setAttribute(k, v) {
          el.attrs[k] = v;
        },
      };
      return el;
    },
  });
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.clearAllMocks();
  vi.useRealTimers();
});

describe("ensureSignedIn", () => {
  it("opens the hosted page in gate mode with the placement, and no credential", () => {
    LevelMomentWebClient.initialize(CONFIG).ensureSignedIn();
    const url = frame().src;
    expect(url).toContain("https://app.levelmoment.com/break?");
    expect(url).toContain("mode=gate");
    expect(url).toContain("placementId=placement-abc");
    expect(url).not.toContain("format=");
    // The credential travels over the bridge, not the URL.
    expect(url).not.toContain("token=");
    expect(url).not.toContain("device-tok-123");
  });

  it("answers the gate page's needCredential over the bridge", () => {
    LevelMomentWebClient.initialize(CONFIG).ensureSignedIn();
    emit({ type: "needCredential" });
    expect(frame().contentWindow.postMessage).toHaveBeenCalledWith(
      {
        type: "credential",
        payload: {
          token: "device-tok-123",
          custody: false,
          protocolVersion: 1,
          sdkVersion: "0.2.0",
          customData: undefined,
        },
      },
      HOST_ORIGIN,
    );
  });

  it("does not settle the gate on needCredential", () => {
    // It is a question, not a verdict. Treating it as one would resolve a
    // publisher's startup branch before the gate had decided anything.
    const settled = vi.fn();
    void LevelMomentWebClient.initialize(CONFIG).ensureSignedIn().then(settled);
    emit({ type: "needCredential" });
    expect(settled).not.toHaveBeenCalled();
    expect(body.children).toHaveLength(1);
  });

  it("resolves ready on signedIn and removes the frame", async () => {
    const result = LevelMomentWebClient.initialize(CONFIG).ensureSignedIn();
    emit({ type: "signedIn" });
    await expect(result).resolves.toBe("ready");
    expect(body.children).toHaveLength(0);
  });

  it("resolves canceled on dismissed", async () => {
    const result = LevelMomentWebClient.initialize(CONFIG).ensureSignedIn();
    emit({ type: "dismissed" });
    await expect(result).resolves.toBe("canceled");
  });

  it("resolves technicalFailure on error", async () => {
    const result = LevelMomentWebClient.initialize(CONFIG).ensureSignedIn();
    emit({ type: "error", payload: { code: "network_error", message: "no" } });
    await expect(result).resolves.toBe("technicalFailure");
  });

  it("resolves exactly once — the first terminal message wins", async () => {
    const result = LevelMomentWebClient.initialize(CONFIG).ensureSignedIn();
    emit({ type: "signedIn" });
    emit({ type: "dismissed" });
    emit({ type: "error", payload: { code: "x", message: "y" } });
    await expect(result).resolves.toBe("ready");
    expect(body.children).toHaveLength(0);
  });

  it("ignores messages from any other origin", async () => {
    vi.useFakeTimers();
    const result = LevelMomentWebClient.initialize({
      ...CONFIG,
      breakLoadTimeoutMs: 1000,
    }).ensureSignedIn();
    emit({ type: "signedIn" }, "https://evil.example.com");
    vi.advanceTimersByTime(1000);
    await expect(result).resolves.toBe("technicalFailure");
  });

  it("ignores a right-origin message from a different frame", async () => {
    // A game can have a break and a hidden sign-in check open at once. Both
    // post from the Level Moment origin, so origin alone would let one frame's
    // message resolve the other's promise.
    vi.useFakeTimers();
    const result = LevelMomentWebClient.initialize({
      ...CONFIG,
      breakLoadTimeoutMs: 1000,
    }).ensureSignedIn();
    emit({ type: "signedIn" }, HOST_ORIGIN, { frame: Symbol("other") });
    vi.advanceTimersByTime(1000);
    await expect(result).resolves.toBe("technicalFailure");
  });

  it("resolves technicalFailure when the page never posts ready", async () => {
    vi.useFakeTimers();
    const result = LevelMomentWebClient.initialize({
      ...CONFIG,
      breakLoadTimeoutMs: 5000,
    }).ensureSignedIn();
    vi.advanceTimersByTime(5000);
    await expect(result).resolves.toBe("technicalFailure");
    expect(body.children).toHaveLength(0);
  });

  it("keeps waiting after ready — pairing takes as long as a parent takes", async () => {
    vi.useFakeTimers();
    const result = LevelMomentWebClient.initialize({
      ...CONFIG,
      breakLoadTimeoutMs: 5000,
    }).ensureSignedIn();
    emit({ type: "ready" });
    vi.advanceTimersByTime(60_000);
    expect(body.children).toHaveLength(1);
    emit({ type: "signedIn" });
    await expect(result).resolves.toBe("ready");
  });

  it("resolves technicalFailure when the frame cannot be mounted", async () => {
    // A malformed breakUrl or a DOM that refuses the frame must not throw out
    // of a publisher's `switch (await ensureSignedIn())` and crash their boot
    // path. Three values, always.
    vi.stubGlobal("document", {
      body,
      createElement: () => {
        throw new Error("cannot create element");
      },
    });
    await expect(
      LevelMomentWebClient.initialize(CONFIG).ensureSignedIn(),
    ).resolves.toBe("technicalFailure");
    expect(body.children).toHaveLength(0);
  });

  it("resolves technicalFailure on a malformed breakUrl", async () => {
    expect(() =>
      LevelMomentWebClient.initialize({
        ...CONFIG,
        breakUrl: "http://[malformed",
      }),
    ).toThrow(/Invalid URL/);
  });

  it("resolves ready with no UI in mock mode", async () => {
    const result = LevelMomentWebClient.initialize({
      ...CONFIG,
      mock: true,
    }).ensureSignedIn();
    await expect(result).resolves.toBe("ready");
    expect(body.children).toHaveLength(0);
  });
});

describe("isSignedIn", () => {
  it("opens a hidden check frame that carries no visible layout", () => {
    void LevelMomentWebClient.initialize(CONFIG)
      .isSignedIn()
      .catch(() => {});
    expect(frame().src).toContain("mode=check");
    expect(frame().style.cssText).toContain("width:0;height:0");
    expect(frame().attrs["aria-hidden"]).toBe("true");
    expect(frame().src).not.toContain("token=");
  });

  it("answers the headless check's needCredential too", () => {
    // The check walks the same credential list a break does. Withholding the
    // configured credential here would let the check report "not signed in"
    // about a device the very next break signs in fine.
    void LevelMomentWebClient.initialize(CONFIG)
      .isSignedIn()
      .catch(() => {});
    emit({ type: "needCredential" });
    expect(frame().contentWindow.postMessage).toHaveBeenCalledWith(
      {
        type: "credential",
        payload: {
          token: "device-tok-123",
          custody: false,
          protocolVersion: 1,
          sdkVersion: "0.2.0",
          customData: undefined,
        },
      },
      HOST_ORIGIN,
    );
  });

  it("resolves true on signedIn", async () => {
    const result = LevelMomentWebClient.initialize(CONFIG).isSignedIn();
    emit({ type: "signedIn" });
    await expect(result).resolves.toBe(true);
    expect(body.children).toHaveLength(0);
  });

  it("resolves false on dismissed", async () => {
    const result = LevelMomentWebClient.initialize(CONFIG).isSignedIn();
    emit({ type: "dismissed" });
    await expect(result).resolves.toBe(false);
  });

  it("rejects rather than guessing false when the check fails technically", async () => {
    const result = LevelMomentWebClient.initialize(CONFIG).isSignedIn();
    emit({
      type: "error",
      payload: { code: "network_error", message: "offline" },
    });
    await expect(result).rejects.toThrow(/network_error/);
  });

  it("rejects on timeout rather than reporting a linked device as signed out", async () => {
    vi.useFakeTimers();
    const result = LevelMomentWebClient.initialize({
      ...CONFIG,
      breakLoadTimeoutMs: 3000,
    }).isSignedIn();
    vi.advanceTimersByTime(3000);
    await expect(result).rejects.toThrow(/timed out after 3000ms/);
    expect(body.children).toHaveLength(0);
  });

  it("rejects when the frame cannot be mounted", async () => {
    // No honest boolean exists for "the check never ran" — same rule as the
    // timeout path.
    vi.stubGlobal("document", {
      body,
      createElement: () => {
        throw new Error("cannot create element");
      },
    });
    await expect(
      LevelMomentWebClient.initialize(CONFIG).isSignedIn(),
    ).rejects.toThrow(/could not open the sign-in check/);
  });

  it("resolves true in mock mode without mounting a frame", async () => {
    await expect(
      LevelMomentWebClient.initialize({ ...CONFIG, mock: true }).isSignedIn(),
    ).resolves.toBe(true);
    expect(body.children).toHaveLength(0);
  });

  it("rejects when the check loads and then never answers", async () => {
    // `ready` cancels the load watchdog, so past that point only the total
    // deadline can free a check whose page went silent.
    vi.useFakeTimers();
    const result = LevelMomentWebClient.initialize({
      ...CONFIG,
      breakLoadTimeoutMs: 3000,
      signInCheckTimeoutMs: 8000,
    }).isSignedIn();
    emit({ type: "ready" });
    vi.advanceTimersByTime(8000);
    await expect(result).rejects.toThrow(/did not answer within 8000ms/);
    expect(body.children).toHaveLength(0);
  });

  it("cancels the total deadline once a verdict arrives", async () => {
    vi.useFakeTimers();
    const result = LevelMomentWebClient.initialize({
      ...CONFIG,
      signInCheckTimeoutMs: 8000,
    }).isSignedIn();
    emit({ type: "signedIn" });
    vi.advanceTimersByTime(60_000);
    await expect(result).resolves.toBe(true);
  });
});

describe("the deadline contract the two entry points do not share", () => {
  it("leaves ensureSignedIn pending after ready — pairing has no deadline", async () => {
    vi.useFakeTimers();
    const result = LevelMomentWebClient.initialize({
      ...CONFIG,
      breakLoadTimeoutMs: 3000,
      signInCheckTimeoutMs: 8000,
    }).ensureSignedIn();
    emit({ type: "ready" });
    vi.advanceTimersByTime(600_000);

    expect(body.children).toHaveLength(1);
    const race = await Promise.race([
      result,
      Promise.resolve("still-pending" as const),
    ]);
    expect(race).toBe("still-pending");

    emit({ type: "signedIn" });
    await expect(result).resolves.toBe("ready");
  });
});

describe("learning access", () => {
  it("uses the hosted access coordinator for a visible access flow", async () => {
    const result = LevelMomentWebClient.initialize(CONFIG).ensureAccess();
    expect(frame().src).toContain("https://app.levelmoment.com/access?");
    expect(frame().src).toContain("mode=gate");
    emit({ type: "signedIn" });
    await expect(result).resolves.toBe("ready");
  });

  it("maps a coordinator dismissal to unavailable and rejects technical checks", async () => {
    const client = LevelMomentWebClient.initialize(CONFIG);
    const unavailable = client.checkAccess();
    expect(frame().src).toContain("/access?");
    expect(frame().src).toContain("mode=check");
    emit({ type: "dismissed" });
    await expect(unavailable).resolves.toBe(false);

    const failed = client.checkAccess();
    emit({ type: "error", payload: { code: "offline", message: "offline" } });
    await expect(failed).rejects.toThrow(/could not check/i);
  });
});
