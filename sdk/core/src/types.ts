import type { HostedOptions } from "./hosted.js";
/** Public contracts shared by the platform SDKs. */

/**
 * The four break formats.
 *
 * `quick_question` is one question and `practice_set` a set of them; both
 * replace a rewarded ad slot. `mastery_round` is a longer set and
 * `intro_lesson` the same set opening on an instruction panel; both replace an
 * interstitial slot. Pass the one that matches the slot you are filling.
 */
export type BreakFormat =
  | "quick_question"
  | "practice_set"
  | "mastery_round"
  | "intro_lesson";

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
