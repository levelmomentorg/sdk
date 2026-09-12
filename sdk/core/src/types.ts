import type { HostedOptions } from "./hosted.js";
/** Public contracts shared by the platform SDKs. */

/**
 * The four break formats.
 *
 * `quick_question` is one question and `practice_set` a set of them; both
 * replace a rewarded ad slot. `mastery_round` is a longer set and
 * `intro_lesson` the same set opening on an instruction panel; both replace an
 * interstitial slot.
 *
 * Declare the slot and Level Moment picks the format inside it — which one
 * depends on the learner, which a game cannot see. A format you pass is read
 * as a floor: the break you get is never smaller than the one you sized your
 * slot against.
 */
export type BreakFormat =
  | "quick_question"
  | "practice_set"
  | "mastery_round"
  | "intro_lesson";

/** The ad type a slot replaces. */
export type SlotAdType = "rewarded" | "interstitial";

/**
 * What a game declares about one ad slot when it integrates: which ad type the
 * slot replaces, how long the slot should run, and what the game grants the
 * player for it.
 *
 * The duration is a target, not a promise. A break's real length varies with
 * the learner; what the slot fixes is how much content Level Moment puts in it.
 * The reward amount is the value the game already granted for that ad unit, so
 * a ported game pays the player what it always paid.
 *
 * See `docs/decisions/break-formats-and-payment-2026-09-11.md`.
 */
export interface SlotDeclaration {
  adType: SlotAdType;
  targetDurationSeconds: number;
  /** Omit when the ad unit granted nothing (an interstitial slot). */
  rewardAmount?: number;
}

/** Shortest and longest slot a game may declare, in seconds. */
export const MIN_SLOT_DURATION_SECONDS = 5;
export const MAX_SLOT_DURATION_SECONDS = 300;

/** Largest reward amount a game may declare for one slot. */
export const MAX_SLOT_REWARD_AMOUNT = 1_000_000;

/**
 * The ad type a break format replaces. Graded formats fill a rewarded slot;
 * completion-based formats fill an interstitial one.
 */
export function adTypeForBreakFormat(format: BreakFormat): SlotAdType {
  return format === "quick_question" || format === "practice_set"
    ? "rewarded"
    : "interstitial";
}

/**
 * Read a slot declaration off whatever the game passed, returning null when it
 * declared nothing usable. Out-of-range values are clamped rather than
 * rejected: a slot that names 900 seconds gets the longest slot we serve, which
 * is a better outcome for the player than no break at all. The server clamps
 * again — a declaration travels on a URL the game controls.
 */
export function normalizeSlotDeclaration(
  value: Partial<SlotDeclaration> | null | undefined,
): SlotDeclaration | null {
  if (!value) return null;
  const adType =
    value.adType === "rewarded" || value.adType === "interstitial"
      ? value.adType
      : null;
  if (adType === null) return null;
  const duration = Number(value.targetDurationSeconds);
  if (!Number.isFinite(duration) || duration <= 0) return null;
  const reward = Number(value.rewardAmount);
  const declaration: SlotDeclaration = {
    adType,
    targetDurationSeconds: clamp(
      Math.round(duration),
      MIN_SLOT_DURATION_SECONDS,
      MAX_SLOT_DURATION_SECONDS,
    ),
  };
  if (Number.isFinite(reward) && reward > 0) {
    declaration.rewardAmount = clamp(
      Math.round(reward),
      0,
      MAX_SLOT_REWARD_AMOUNT,
    );
  }
  return declaration;
}

function clamp(value: number, min: number, max: number): number {
  return Math.min(max, Math.max(min, value));
}

export type EnsureSignedInResult = "ready" | "canceled" | "technicalFailure";

export type LevelMomentAdErrorCode =
  | "network_error"
  | "no_fill"
  | "invalid_token"
  | "not_loaded"
  | "unknown";

export interface LevelMomentAdError {
  code: LevelMomentAdErrorCode;
  message: string;
}

export interface RewardItem {
  type: "question_answered";
  amount: 0 | 1;
  /** Opaque ID for matching a verified reward webhook. */
  rewardId?: string;
}

export interface LevelMomentConfig extends HostedOptions {
  placementId: string;
  requestTimeoutMs?: number;
  customData?: string;
}
