import type { HostedOptions } from "./hosted.js";
/** Public contracts shared by the platform SDKs. */

export type BreakFormat = "flashcard" | "quiz" | "deep_dive";

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
