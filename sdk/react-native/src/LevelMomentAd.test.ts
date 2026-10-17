import { afterEach, describe, expect, it, vi } from "vitest";

import { LevelMomentAd } from "./LevelMomentAd.js";
import {
  _registerModalHandler,
  surfaceCanLeave,
  type ModalShowParams,
} from "./modalHost.js";

const OPTIONS = {
  unsafeTesting: {
    breakUrl: "https://localhost:3000/break",
    apiUrl: "https://localhost:8080",
    token: "eply_sbx_test",
  },
};

function mountModalHost(): { current: ModalShowParams | null; opens: number } {
  const host = { current: null as ModalShowParams | null, opens: 0 };
  _registerModalHandler((params) => {
    host.current = params;
    host.opens += 1;
    return { close: vi.fn() };
  });
  return host;
}

async function loadedAd(): Promise<LevelMomentAd> {
  const ad = LevelMomentAd.createForAdRequest("game-42", OPTIONS);
  ad.load();
  await Promise.resolve();
  return ad;
}

afterEach(() => {
  _registerModalHandler(null);
  vi.clearAllMocks();
});

describe("LevelMomentAd bridge lifecycle", () => {
  it("grants an opaque reward ID once when the hosted bridge retries it", async () => {
    const host = mountModalHost();
    const ad = await loadedAd();
    const onReward = vi.fn();
    ad.addAdEventListener("earnedReward", onReward);
    ad.show();

    const reward = {
      type: "earnedReward" as const,
      payload: { rewardId: "reward-42", earnedAt: "2026-09-22T00:00:00.000Z" },
    };
    host.current!.onMessage(reward);
    host.current!.onMessage(reward);

    expect(onReward).toHaveBeenCalledOnce();
    expect(onReward).toHaveBeenCalledWith({
      type: "question_answered",
      rewardId: "reward-42",
      earnedAt: "2026-09-22T00:00:00.000Z",
    });
  });

  it("refuses a sandbox token in a release build", () => {
    vi.stubGlobal("__DEV__", false);
    try {
      expect(() =>
        LevelMomentAd.createForAdRequest("game-42", OPTIONS),
      ).toThrow(/release build/);
    } finally {
      vi.unstubAllGlobals();
    }
  });

  it("opens a break with no close button", async () => {
    // A break ends in a pass, a fail, or a load failure; the player can't
    // close it partway (owner, October 17, 2026).
    const host = mountModalHost();
    const ad = await loadedAd();
    ad.show();
    expect(host.current).not.toBeNull();
    expect(host.current!.closeButton).toBeFalsy();
    // The URL asks the page to end the break itself and to offer a way out
    // of a mid-break pairing card.
    expect(new URL(host.current!.url).searchParams.get("leave")).toBe("page");
  });

  it("keeps a break's way out only until the page confirms it ends itself", () => {
    const shown = { closeButton: false };
    expect(surfaceCanLeave(shown, false)).toBe(true);
    expect(surfaceCanLeave(shown, true)).toBe(false);
    // The gate always keeps its close button: closing it is "not now".
    expect(surfaceCanLeave({ closeButton: true }, true)).toBe(true);
    expect(surfaceCanLeave(null, false)).toBe(false);
  });

  it("does not open a second hosted break after this ad is consumed", async () => {
    const host = mountModalHost();
    const ad = await loadedAd();

    ad.show();
    host.current!.onMessage({ type: "dismissed" });
    ad.show();

    expect(host.opens).toBe(1);
  });
});
