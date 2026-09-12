import { describe, it, expect } from "vitest";
import {
  MAX_SLOT_DURATION_SECONDS,
  MIN_SLOT_DURATION_SECONDS,
  adTypeForBreakFormat,
  normalizeSlotDeclaration,
} from "./types.js";
import { addSlotParams } from "./hosted.js";

describe("adTypeForBreakFormat", () => {
  it("maps graded formats to a rewarded slot", () => {
    expect(adTypeForBreakFormat("quick_question")).toBe("rewarded");
    expect(adTypeForBreakFormat("practice_set")).toBe("rewarded");
  });

  it("maps completion formats to an interstitial slot", () => {
    expect(adTypeForBreakFormat("mastery_round")).toBe("interstitial");
    expect(adTypeForBreakFormat("intro_lesson")).toBe("interstitial");
  });
});

describe("normalizeSlotDeclaration", () => {
  it("reads a declared slot", () => {
    expect(
      normalizeSlotDeclaration({
        adType: "rewarded",
        targetDurationSeconds: 15,
        rewardAmount: 50,
      }),
    ).toEqual({
      adType: "rewarded",
      targetDurationSeconds: 15,
      rewardAmount: 50,
    });
  });

  it("leaves out a reward amount the slot does not grant", () => {
    const slot = normalizeSlotDeclaration({
      adType: "interstitial",
      targetDurationSeconds: 30,
    });
    expect(slot).toEqual({ adType: "interstitial", targetDurationSeconds: 30 });
    expect(slot?.rewardAmount).toBeUndefined();
  });

  it("clamps a duration outside the range rather than refusing the slot", () => {
    expect(
      normalizeSlotDeclaration({ adType: "rewarded", targetDurationSeconds: 1 })
        ?.targetDurationSeconds,
    ).toBe(MIN_SLOT_DURATION_SECONDS);
    expect(
      normalizeSlotDeclaration({
        adType: "rewarded",
        targetDurationSeconds: 99999,
      })?.targetDurationSeconds,
    ).toBe(MAX_SLOT_DURATION_SECONDS);
  });

  it("returns null when nothing usable was declared", () => {
    expect(normalizeSlotDeclaration(null)).toBeNull();
    expect(normalizeSlotDeclaration(undefined)).toBeNull();
    expect(normalizeSlotDeclaration({ targetDurationSeconds: 15 })).toBeNull();
    expect(
      normalizeSlotDeclaration({
        adType: "rewarded",
        targetDurationSeconds: 0,
      }),
    ).toBeNull();
  });
});

describe("addSlotParams", () => {
  it("puts the declaration on the URL under the names the page reads", () => {
    const params = new URLSearchParams();
    addSlotParams(params, {
      adType: "rewarded",
      targetDurationSeconds: 15,
      rewardAmount: 50,
    });
    expect(params.get("adType")).toBe("rewarded");
    expect(params.get("targetDurationSeconds")).toBe("15");
    expect(params.get("rewardAmount")).toBe("50");
  });

  it("adds nothing for a slot that declared nothing", () => {
    const params = new URLSearchParams();
    addSlotParams(params, null);
    expect([...params.keys()]).toEqual([]);
  });
});
