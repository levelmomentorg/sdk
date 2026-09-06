import { afterEach, describe, expect, it, vi } from "vitest";

import { LevelMomentAd } from "./LevelMomentAd.js";
import { _registerModalHandler, type ModalShowParams } from "./modalHost.js";

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
      payload: { amount: 1 as const, rewardId: "reward-42" },
    };
    host.current!.onMessage(reward);
    host.current!.onMessage(reward);

    expect(onReward).toHaveBeenCalledOnce();
    expect(onReward).toHaveBeenCalledWith({
      type: "question_answered",
      amount: 1,
      rewardId: "reward-42",
    });
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
