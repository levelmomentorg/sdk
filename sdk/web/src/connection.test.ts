// Require-internet mode: the no-connection message follows the browser's own
// connection state, and Try again re-reads it.

import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";

import { LevelMomentWebClient } from "./client.js";
import { noConnectionCopy } from "./chrome.js";
import {
  clickButton,
  descendants,
  fakeElement,
  type FakeElement,
} from "./fakeDom.test-support.js";

const CONFIG = {
  unsafeTesting: {},
  apiUrl: "https://api.test.levelmoment.com",
  placementId: "placement-abc",
  breakUrl: "https://app.levelmoment.com/break",
};

let body: FakeElement;
let windowListeners: Record<string, Array<() => void>>;
let nav: { onLine: boolean };

function fire(type: "online" | "offline"): void {
  nav.onLine = type === "online";
  for (const fn of [...(windowListeners[type] ?? [])]) fn();
}

function message(): FakeElement | undefined {
  return body.children.find(
    (c) => c.attrs["data-levelmoment"] === "no-connection",
  );
}

function text(root: FakeElement): string[] {
  return descendants(root)
    .map((n) => n.textContent)
    .filter(Boolean);
}

beforeEach(() => {
  body = fakeElement("body");
  windowListeners = {};
  nav = { onLine: true };
  vi.stubGlobal("navigator", nav);
  vi.stubGlobal("window", {
    location: { href: "https://game.test.com/play" },
    addEventListener: (type: string, fn: () => void) => {
      (windowListeners[type] ??= []).push(fn);
    },
    removeEventListener: (type: string, fn: () => void) => {
      windowListeners[type] = (windowListeners[type] ?? []).filter(
        (x) => x !== fn,
      );
    },
  });
  vi.stubGlobal("document", {
    body,
    createElement: (tag: string) => fakeElement(tag),
  });
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("requireInternet", () => {
  it("does nothing unless the game opts in", () => {
    LevelMomentWebClient.initialize(CONFIG);
    fire("offline");
    expect(message()).toBeUndefined();
    expect(windowListeners.offline ?? []).toHaveLength(0);
  });

  it("shows the message on offline and removes it on online", () => {
    const onConnectionChange = vi.fn();
    const client = LevelMomentWebClient.initialize({
      ...CONFIG,
      requireInternet: true,
      gameName: "Hue Hunt",
      onConnectionChange,
    });
    expect(message()).toBeUndefined();
    expect(client.isOnline()).toBe(true);

    fire("offline");
    const shown = message();
    expect(shown).toBeDefined();
    expect(shown!.attrs.role).toBe("alertdialog");
    expect(text(shown!)).toEqual([
      "No internet connection",
      "Hue Hunt needs an internet connection. Turn on Wi-Fi or mobile data, then try again.",
      "Try again",
    ]);
    expect(onConnectionChange).toHaveBeenLastCalledWith(false);
    expect(client.isOnline()).toBe(false);

    fire("online");
    expect(message()).toBeUndefined();
    expect(onConnectionChange).toHaveBeenLastCalledWith(true);
    expect(onConnectionChange).toHaveBeenCalledTimes(2);
    expect(client.isOnline()).toBe(true);
  });

  it("does not stack a second message or repeat the callback", () => {
    const onConnectionChange = vi.fn();
    LevelMomentWebClient.initialize({
      ...CONFIG,
      requireInternet: true,
      onConnectionChange,
    });
    fire("offline");
    fire("offline");
    expect(body.children).toHaveLength(1);
    expect(onConnectionChange).toHaveBeenCalledOnce();
  });

  it("Try again keeps the message while still offline and hides it once online", () => {
    const onConnectionChange = vi.fn();
    LevelMomentWebClient.initialize({
      ...CONFIG,
      requireInternet: true,
      onConnectionChange,
    });
    fire("offline");
    clickButton(message()!);
    expect(message()).toBeDefined();
    expect(onConnectionChange).toHaveBeenCalledOnce();

    // The connection came back without an `online` event reaching the page.
    nav.onLine = true;
    clickButton(message()!);
    expect(message()).toBeUndefined();
    expect(onConnectionChange).toHaveBeenLastCalledWith(true);
  });

  it("shows the message at initialize when already offline, and reports it after initialize returns", async () => {
    nav.onLine = false;
    const onConnectionChange = vi.fn();
    LevelMomentWebClient.initialize({
      ...CONFIG,
      requireInternet: true,
      onConnectionChange,
    });
    expect(message()).toBeDefined();
    expect(message()!.children.at(-1)!.focused).toBe(true);
    expect(onConnectionChange).not.toHaveBeenCalled();
    await Promise.resolve();
    expect(onConnectionChange).toHaveBeenCalledWith(false);
  });

  it("dispose removes the message and stops listening", () => {
    const onConnectionChange = vi.fn();
    const client = LevelMomentWebClient.initialize({
      ...CONFIG,
      requireInternet: true,
      onConnectionChange,
    });
    fire("offline");
    client.dispose();
    expect(message()).toBeUndefined();
    expect(windowListeners.offline).toHaveLength(0);
    expect(windowListeners.online).toHaveLength(0);
    fire("online");
    fire("offline");
    expect(message()).toBeUndefined();
    expect(onConnectionChange).toHaveBeenCalledOnce();
  });

  it("a server failure while online never shows the message", async () => {
    // The browser is online; the hosted frame fails to load. The break ends
    // as a load failure and nothing blocks the game.
    vi.useFakeTimers();
    try {
      const client = LevelMomentWebClient.initialize({
        ...CONFIG,
        requireInternet: true,
        breakLoadTimeoutMs: 1000,
      });
      const result = client.ensureAccess();
      await vi.advanceTimersByTimeAsync(1000);
      await expect(result).resolves.toBe("technicalFailure");
      expect(message()).toBeUndefined();
    } finally {
      vi.useRealTimers();
    }
  });
});

describe("noConnectionCopy", () => {
  it("falls back to This game without a name", () => {
    expect(noConnectionCopy().body).toBe(
      "This game needs an internet connection. Turn on Wi-Fi or mobile data, then try again.",
    );
    expect(noConnectionCopy("   ").body.startsWith("This game ")).toBe(true);
  });
});
