import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";

import { LevelMomentWebClient } from "./client.js";
import { LevelMomentWebAd } from "./LevelMomentWebAd.js";
import type { RewardItem } from "@levelmoment/sdk-core";

// ---------------------------------------------------------------------------
// The SDK is a thin loader: it builds a /break URL, mounts a fullscreen
// iframe, and bridges the hosted page's postMessage protocol. Tests stub a
// minimal `window` / `document` (vitest env is `node`).
// ---------------------------------------------------------------------------

const CONFIG = {
  apiUrl: "https://api.test.levelmoment.com",
  placementId: "placement-abc",
  studentToken: "student-tok-123",
};

// ---- DOM stubs ------------------------------------------------------------

interface FakeIframe {
  tagName: "IFRAME";
  src: string;
  style: { cssText: string };
  parentNode: FakeBody | null;
  setAttribute: (k: string, v: string) => void;
}

interface FakeBody {
  children: FakeIframe[];
  appendChild: (el: FakeIframe) => void;
  removeChild: (el: FakeIframe) => void;
}

function makeBody(): FakeBody {
  const body: FakeBody = {
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
  return body;
}

let messageListeners: Array<(e: MessageEvent) => void>;
let body: FakeBody;

function emitMessage(data: unknown, origin: string): void {
  for (const l of [...messageListeners]) l({ data, origin } as MessageEvent);
}

beforeEach(() => {
  messageListeners = [];
  body = makeBody();

  const fakeWindow = {
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
  };
  vi.stubGlobal("window", fakeWindow);

  vi.stubGlobal("document", {
    body,
    createElement: (tag: string): FakeIframe => ({
      tagName: tag.toUpperCase() as "IFRAME",
      src: "",
      style: { cssText: "" },
      parentNode: null,
      setAttribute: vi.fn(),
    }),
  });
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.clearAllMocks();
});

// ---------------------------------------------------------------------------
// LevelMomentWebClient
// ---------------------------------------------------------------------------

describe("LevelMomentWebClient", () => {
  describe("initialize", () => {
    it("returns a client with the provided config", () => {
      const client = LevelMomentWebClient.initialize(CONFIG);
      expect(client.config).toEqual(CONFIG);
    });
  });

  describe("start / stop", () => {
    it("start is a no-op and does not throw", () => {
      const client = LevelMomentWebClient.initialize(CONFIG);
      expect(() => client.start()).not.toThrow();
    });

    it("stop is an idempotent no-op", () => {
      const client = LevelMomentWebClient.initialize(CONFIG);
      client.start();
      client.stop();
      expect(() => client.stop()).not.toThrow();
    });

    it("start/stop do not register any DOM listeners", () => {
      const client = LevelMomentWebClient.initialize(CONFIG);
      client.start();
      client.stop();
      expect(
        (window.addEventListener as ReturnType<typeof vi.fn>).mock.calls.length,
      ).toBe(0);
    });
  });

  describe("loadAd", () => {
    it("fires onAdLoaded with a loaded LevelMomentWebAd", async () => {
      const client = LevelMomentWebClient.initialize(CONFIG);
      const ad = await new Promise<LevelMomentWebAd>((resolve) =>
        client.loadAd({ onAdLoaded: resolve }),
      );
      expect(ad).toBeInstanceOf(LevelMomentWebAd);
      expect(ad.isLoaded()).toBe(true);
    });

    it("defaults breakUrl to <origin>/break", async () => {
      const client = LevelMomentWebClient.initialize(CONFIG);
      const ad = await new Promise<LevelMomentWebAd>((resolve) =>
        client.loadAd({ onAdLoaded: resolve }),
      );
      ad.show({});
      expect(body.children[0].src).toContain("https://game.test.com/break?");
    });

    it("uses an explicit breakUrl when provided in config", async () => {
      const client = LevelMomentWebClient.initialize({
        ...CONFIG,
        breakUrl: "https://app.levelmoment.com/break",
      });
      const ad = await new Promise<LevelMomentWebAd>((resolve) =>
        client.loadAd({ onAdLoaded: resolve }),
      );
      ad.show({});
      expect(body.children[0].src).toContain(
        "https://app.levelmoment.com/break?",
      );
    });
  });
});

// ---------------------------------------------------------------------------
// LevelMomentWebAd — loader behaviour
// ---------------------------------------------------------------------------

function loadAd(
  config: Parameters<typeof LevelMomentWebAd.load>[0],
  options?: { format?: "flashcard" | "quiz" | "deep_dive" },
): Promise<LevelMomentWebAd> {
  return new Promise((resolve, reject) =>
    LevelMomentWebAd.load(
      config,
      { onAdLoaded: resolve, onAdFailedToLoad: reject },
      options,
    ),
  );
}

const LIVE = {
  ...CONFIG,
  breakUrl: "https://app.levelmoment.com/break",
};

describe("LevelMomentWebAd", () => {
  describe("load", () => {
    it("calls onAdFailedToLoad when placementId is missing", async () => {
      await expect(loadAd({ ...LIVE, placementId: "" })).rejects.toMatchObject({
        code: "unknown",
      });
    });

    it("produces a loaded ad", async () => {
      const ad = await loadAd(LIVE);
      expect(ad.isLoaded()).toBe(true);
    });

    it("isThrottled is always false (server-decided)", async () => {
      const ad = await loadAd(LIVE);
      expect(ad.isThrottled()).toBe(false);
    });
  });

  describe("show — URL building", () => {
    it("builds a live URL with apiUrl + token params", async () => {
      const ad = await loadAd(LIVE, { format: "quiz" });
      ad.show({});
      const url = body.children[0].src;
      expect(url).toContain("placementId=placement-abc");
      expect(url).toContain("format=quiz");
      expect(url).toContain("apiUrl=https%3A%2F%2Fapi.test.levelmoment.com");
      expect(url).toContain("token=student-tok-123");
      expect(url).not.toContain("mock=true");
    });

    it("builds a mock URL without apiUrl/token", async () => {
      const ad = await loadAd({ ...LIVE, mock: true });
      ad.show({});
      const url = body.children[0].src;
      expect(url).toContain("mock=true");
      expect(url).not.toContain("apiUrl=");
      expect(url).not.toContain("token=");
    });

    it("threads SSV customData onto the iframe src, URL-encoded", async () => {
      // SSV-parity (V1): the host-supplied customData must reach the hosted
      // /break page as a URL param so the page can stamp it on every impression.
      const ad = await loadAd({ ...LIVE, customData: "order/42&x" });
      ad.show({});
      const url = body.children[0].src;
      expect(url).toContain("customData=order%2F42%26x");
    });

    it("omits customData from the URL when unset", async () => {
      const ad = await loadAd(LIVE);
      ad.show({});
      expect(body.children[0].src).not.toContain("customData=");
    });

    it("defaults format to flashcard", async () => {
      const ad = await loadAd(LIVE);
      ad.show({});
      expect(body.children[0].src).toContain("format=flashcard");
    });

    it("uses ? when breakUrl has no query, & when it does", async () => {
      const ad1 = await loadAd(LIVE);
      ad1.show({});
      expect(body.children[0].src).toContain("/break?placementId=");

      body.children = [];
      const ad2 = await loadAd({
        ...LIVE,
        breakUrl: "https://app.levelmoment.com/break?theme=dark",
      });
      ad2.show({});
      expect(body.children[0].src).toContain("theme=dark&placementId=");
    });
  });

  describe("show — iframe mounting", () => {
    it("appends a fullscreen fixed iframe to document.body", async () => {
      const ad = await loadAd(LIVE);
      ad.show({});
      expect(body.children).toHaveLength(1);
      const css = body.children[0].style.cssText;
      expect(css).toContain("position:fixed");
      expect(css).toContain("inset:0");
      expect(css).toContain("z-index:2147483647");
    });

    it("does not mount twice on double show()", async () => {
      const ad = await loadAd(LIVE);
      ad.show({});
      ad.show({});
      expect(body.children).toHaveLength(1);
    });

    it("show() before load resumes cleanly via onAdDismissed", () => {
      const onAdDismissed = vi.fn();
      // Construct an ad but never let load() complete the microtask.
      let captured: LevelMomentWebAd | undefined;
      LevelMomentWebAd.load(LIVE, {
        onAdLoaded: (a) => (captured = a),
        onAdFailedToLoad: () => {},
      });
      // captured is undefined synchronously (load resolves on microtask).
      expect(captured).toBeUndefined();
      // A fresh, never-loaded handle is unreachable here; assert the
      // documented contract instead: an unloaded show dismisses cleanly.
      const ad = Object.create(LevelMomentWebAd.prototype) as LevelMomentWebAd;
      ad.show({ onAdDismissed });
      expect(onAdDismissed).toHaveBeenCalledOnce();
      expect(body.children).toHaveLength(0);
    });
  });

  describe("postMessage bridge", () => {
    const HOST_ORIGIN = "https://app.levelmoment.com";

    it("maps earnedReward to onUserEarnedReward", async () => {
      const rewards: RewardItem[] = [];
      const ad = await loadAd(LIVE);
      ad.show({ onUserEarnedReward: (r) => rewards.push(r) });
      emitMessage(
        { type: "earnedReward", payload: { amount: 1 } },
        HOST_ORIGIN,
      );
      expect(rewards).toEqual([{ type: "question_answered", amount: 1 }]);
    });

    it("tears down + dismisses on 'dismissed'", async () => {
      const onAdDismissed = vi.fn();
      const ad = await loadAd(LIVE);
      ad.show({ onAdDismissed });
      emitMessage({ type: "dismissed" }, HOST_ORIGIN);
      expect(onAdDismissed).toHaveBeenCalledOnce();
      expect(body.children).toHaveLength(0);
      expect(messageListeners).toHaveLength(0);
    });

    it("tears down + dismisses on 'error'", async () => {
      const onAdDismissed = vi.fn();
      const ad = await loadAd(LIVE);
      ad.show({ onAdDismissed });
      emitMessage(
        { type: "error", payload: { code: "no_fill", message: "x" } },
        HOST_ORIGIN,
      );
      expect(onAdDismissed).toHaveBeenCalledOnce();
      expect(body.children).toHaveLength(0);
    });

    it("'error' fires onAdFailedToShow when provided (dismiss not called)", async () => {
      const onAdDismissed = vi.fn();
      const onAdFailedToShow = vi.fn();
      const ad = await loadAd(LIVE);
      ad.show({ onAdDismissed, onAdFailedToShow });
      emitMessage(
        { type: "error", payload: { code: "no_fill", message: "x" } },
        HOST_ORIGIN,
      );
      expect(onAdFailedToShow).toHaveBeenCalledWith({
        code: "no_fill",
        message: "x",
      });
      expect(onAdDismissed).not.toHaveBeenCalled();
      expect(body.children).toHaveLength(0);
    });

    it("'ready' is a no-op (iframe stays mounted)", async () => {
      const onAdDismissed = vi.fn();
      const ad = await loadAd(LIVE);
      ad.show({ onAdDismissed });
      emitMessage({ type: "ready" }, HOST_ORIGIN);
      expect(onAdDismissed).not.toHaveBeenCalled();
      expect(body.children).toHaveLength(1);
    });

    it("ignores messages from a mismatched origin", async () => {
      const onAdDismissed = vi.fn();
      const rewards: RewardItem[] = [];
      const ad = await loadAd(LIVE);
      ad.show({ onAdDismissed, onUserEarnedReward: (r) => rewards.push(r) });
      emitMessage(
        { type: "earnedReward", payload: { amount: 1 } },
        "https://evil.example.com",
      );
      emitMessage({ type: "dismissed" }, "https://evil.example.com");
      expect(rewards).toHaveLength(0);
      expect(onAdDismissed).not.toHaveBeenCalled();
      expect(body.children).toHaveLength(1);
    });

    it("accepts same-origin default breakUrl messages", async () => {
      // breakUrl resolves relative to window.location → game.test.com origin.
      const client = LevelMomentWebClient.initialize(CONFIG);
      const onAdDismissed = vi.fn();
      const ad = await new Promise<LevelMomentWebAd>((resolve) =>
        client.loadAd({ onAdLoaded: resolve }),
      );
      ad.show({ onAdDismissed });
      emitMessage({ type: "dismissed" }, "https://game.test.com");
      expect(onAdDismissed).toHaveBeenCalledOnce();
    });
  });

  describe("load-timeout watchdog", () => {
    const HOST_ORIGIN = "https://app.levelmoment.com";

    beforeEach(() => {
      vi.useFakeTimers();
    });

    afterEach(() => {
      vi.useRealTimers();
    });

    async function loadAdFake(
      config: Parameters<typeof LevelMomentWebAd.load>[0],
    ): Promise<LevelMomentWebAd> {
      const p = new Promise<LevelMomentWebAd>((resolve, reject) =>
        LevelMomentWebAd.load(config, {
          onAdLoaded: resolve,
          onAdFailedToLoad: reject,
        }),
      );
      // load() resolves on a microtask; flush it under fake timers.
      await vi.advanceTimersByTimeAsync(0);
      return p;
    }

    it("tears down + dismisses once when no 'ready' arrives within the timeout", async () => {
      const onAdDismissed = vi.fn();
      const ad = await loadAdFake(LIVE);
      ad.show({ onAdDismissed });
      expect(body.children).toHaveLength(1);
      expect(messageListeners).toHaveLength(1);

      // Just before the default 15s deadline: nothing happens yet.
      await vi.advanceTimersByTimeAsync(14999);
      expect(onAdDismissed).not.toHaveBeenCalled();
      expect(body.children).toHaveLength(1);

      // Cross the deadline → watchdog fires.
      await vi.advanceTimersByTimeAsync(1);
      expect(onAdDismissed).toHaveBeenCalledOnce();
      expect(body.children).toHaveLength(0);
      expect(messageListeners).toHaveLength(0);
    });

    it("'ready' before the timeout clears the watchdog (no late teardown)", async () => {
      const onAdDismissed = vi.fn();
      const ad = await loadAdFake(LIVE);
      ad.show({ onAdDismissed });

      emitMessage({ type: "ready" }, HOST_ORIGIN);
      // Advance well past the deadline — must NOT teardown/dismiss.
      await vi.advanceTimersByTimeAsync(60000);
      expect(onAdDismissed).not.toHaveBeenCalled();
      expect(body.children).toHaveLength(1);
      expect(messageListeners).toHaveLength(1);
    });

    it("dispose() before the timeout clears the watchdog (no late fire)", async () => {
      const onAdDismissed = vi.fn();
      const ad = await loadAdFake(LIVE);
      ad.show({ onAdDismissed });

      ad.dispose();
      expect(body.children).toHaveLength(0);
      await vi.advanceTimersByTimeAsync(60000);
      expect(onAdDismissed).not.toHaveBeenCalled();
    });

    it("a 'dismissed' teardown before the timeout does not double-call onAdDismissed", async () => {
      const onAdDismissed = vi.fn();
      const ad = await loadAdFake(LIVE);
      ad.show({ onAdDismissed });

      emitMessage({ type: "dismissed" }, HOST_ORIGIN);
      expect(onAdDismissed).toHaveBeenCalledOnce();
      await vi.advanceTimersByTimeAsync(60000);
      expect(onAdDismissed).toHaveBeenCalledOnce();
    });

    it("honors a custom breakLoadTimeoutMs", async () => {
      const onAdDismissed = vi.fn();
      const ad = await loadAdFake({ ...LIVE, breakLoadTimeoutMs: 3000 });
      ad.show({ onAdDismissed });

      await vi.advanceTimersByTimeAsync(2999);
      expect(onAdDismissed).not.toHaveBeenCalled();
      await vi.advanceTimersByTimeAsync(1);
      expect(onAdDismissed).toHaveBeenCalledOnce();
      expect(body.children).toHaveLength(0);
    });

    it("breakLoadTimeoutMs of 0 disables the watchdog", async () => {
      const onAdDismissed = vi.fn();
      const ad = await loadAdFake({ ...LIVE, breakLoadTimeoutMs: 0 });
      ad.show({ onAdDismissed });

      await vi.advanceTimersByTimeAsync(120000);
      expect(onAdDismissed).not.toHaveBeenCalled();
      expect(body.children).toHaveLength(1);
    });
  });

  describe("dispose", () => {
    it("removes the iframe and the message listener", async () => {
      const ad = await loadAd(LIVE);
      ad.show({});
      expect(body.children).toHaveLength(1);
      ad.dispose();
      expect(body.children).toHaveLength(0);
      expect(messageListeners).toHaveLength(0);
    });

    it("a disposed ad does not show", async () => {
      const ad = await loadAd(LIVE);
      ad.dispose();
      ad.show({});
      expect(body.children).toHaveLength(0);
    });
  });
});
