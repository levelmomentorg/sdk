// ---------------------------------------------------------------------------
// Core domain types shared across all SDK platforms
// ---------------------------------------------------------------------------

export type GenerationType = "static" | "ai-generated";

// ---------------------------------------------------------------------------
// Break formats — educational equivalents of ad formats
// ---------------------------------------------------------------------------

export type BreakFormat = "flashcard" | "quiz" | "deep_dive";

// ---------------------------------------------------------------------------
// Safe question meta types — answer data stripped; 8 question types
// ---------------------------------------------------------------------------

interface BaseQuestionMeta {
  /** Optional countdown in seconds. Omit = untimed. */
  timeLimitSeconds?: number;
  /**
   * Optional parametric SVG visual model (#122, visual-models-v2). Carries a
   * base64 `data:image/svg+xml` URI (`src`) shown through the renderer's
   * `<img src>` path, plus per-instance `alt` text. Carries NO answer data, so
   * it passes `stripAnswerData` untouched. Backward compatible: any of the 8
   * meta types may carry one; legacy questions omit it.
   */
  visual?: { src: string; alt: string };
  /**
   * Optional progressive hints (max 3), revealed one tap at a time on /break.
   * Static strings only — NO answer data, so they pass `stripAnswerData`
   * untouched and reach the client. Using a hint does not affect grading.
   * Any of the 8 meta types may carry hints; legacy questions omit them.
   */
  hints?: string[];
}

export interface MultipleChoiceMeta extends BaseQuestionMeta {
  type: "multiple_choice";
  prompt: string;
  options: string[];
}

export interface MultipleChoiceImageMeta extends BaseQuestionMeta {
  type: "multiple_choice_image";
  prompt: string;
  imageOptions: string[];
}

export interface ImagePromptMeta extends BaseQuestionMeta {
  type: "image_prompt";
  imageUrl: string;
  prompt?: string;
  options: string[];
}

export interface TextInputMeta extends BaseQuestionMeta {
  type: "text_input";
  prompt: string;
}

export interface OrderingMeta extends BaseQuestionMeta {
  type: "ordering";
  prompt: string;
  items: string[];
}

export interface FillInBlankMeta extends BaseQuestionMeta {
  type: "fill_in_blank";
  prompt: string;
  /** Present when rendered as multiple-choice; absent for free-text mode. */
  options?: string[];
}

export interface MatchingMeta extends BaseQuestionMeta {
  type: "matching";
  prompt: string;
  leftItems: string[];
  rightItems: string[];
}

export interface CategorizingMeta extends BaseQuestionMeta {
  type: "categorizing";
  prompt: string;
  categories: string[];
  items: string[];
}

/** Discriminated union of all 8 safe question meta types. */
export type QuestionMeta =
  | MultipleChoiceMeta
  | MultipleChoiceImageMeta
  | ImagePromptMeta
  | TextInputMeta
  | OrderingMeta
  | FillInBlankMeta
  | MatchingMeta
  | CategorizingMeta;

// Legacy — kept for backward compatibility with existing games
export interface StaticQuestionMeta {
  type: "static";
  prompt: string;
  options: string[];
  correctIndex: number;
  subject: string;
  level: string;
}

export interface AiGeneratedQuestionMeta {
  type: "ai-generated";
  subject: string;
  level: string;
  hints?: Record<string, unknown>;
}

export interface Question {
  id: string;
  generationType?: GenerationType;
  /** Discriminated union of all 8 safe meta types. */
  meta: QuestionMeta;
  /**
   * Set ONLY when the served question came from the template engine — its `id`
   * is then a `question_instances.id`. The SDK uses this as the discriminator
   * to send `instanceId` (not `questionId`) on the impression. Absent for
   * static curriculum questions.
   */
  instanceId?: string;
}

// ---------------------------------------------------------------------------
// Educational content
// ---------------------------------------------------------------------------

/** Optional per-question instructional content. Shown after repeated wrong answers. */
export interface QuestionInstruction {
  format: "text" | "image" | "video";
  content: string;
}

/** Multi-page educational lesson shown at deep_dive start or when struggling. */
export interface ConceptLesson {
  title: string;
  pages: Array<{ format: "text" | "image" | "video"; content: string }>;
}

// ---------------------------------------------------------------------------
// Mastery / progress
// ---------------------------------------------------------------------------

export type MasteryStatus =
  | "not_started"
  | "learning"
  | "mastered"
  | "struggling";

export interface ProgressUpdate {
  conceptId: string;
  previousStatus: MasteryStatus;
  newStatus: MasteryStatus;
  masteryScore: number;
  nextBreakSuggestion?: "quiz" | "deep_dive";
}

// ---------------------------------------------------------------------------
// Break session (multi-question quiz/deep_dive)
// ---------------------------------------------------------------------------

export interface SessionQuestion {
  id: string;
  meta: QuestionMeta;
  conceptId: string;
  unitId: string;
  instruction?: QuestionInstruction | null;
  /**
   * Set ONLY for template-engine instances — the `id` is then a
   * `question_instances.id`. The SDK sends `instanceId` (not `questionId`) on
   * the impression. Absent for static curriculum questions.
   */
  instanceId?: string;
}

export interface BreakSession {
  sessionId: string;
  format: BreakFormat;
  questions: SessionQuestion[];
  totalQuestions: number;
  currentIndex: number;
  passThreshold: number;
  lesson?: ConceptLesson | null;
  throttled: boolean;
}

export interface SessionSummary {
  correctCount: number;
  totalAnswered: number;
  passed: boolean;
  progressUpdates: ProgressUpdate[];
}

/** Static facts about the loaded break session, for progress UI. */
export interface SessionInfo {
  currentIndex: number;
  totalQuestions: number;
  passThreshold: number;
}

// ---------------------------------------------------------------------------
// Graded-answer result (returned by advanceSession / submitFlashcardAnswer)
// ---------------------------------------------------------------------------

export type AnswerOutcome = "correct" | "incorrect" | "skipped" | "timed_out";

/**
 * Structured, display-ready description of a question's correct answer.
 * Sent by the server on non-correct outcomes so the renderer can teach
 * ("the answer was…") instead of silently advancing. Mirrors
 * `platform/core/src/grading.ts` (CorrectReveal) — keep in sync.
 */
export type CorrectReveal =
  | { kind: "option"; index: number; label?: string }
  | { kind: "text"; value: string }
  | { kind: "order"; indices: number[]; labels: string[] }
  | { kind: "pairs"; pairs: [number, number][]; labels: [string, string][] }
  | {
      kind: "assignments";
      assignments: number[];
      labels: [string, string][];
    };

/** Result of submitting one answer: the grade, the teach-back, and what's next. */
export interface AdvanceResult {
  outcome: AnswerOutcome;
  /** Present on non-correct outcomes (when the server could grade the answer). */
  reveal?: CorrectReveal;
  progressUpdate?: ProgressUpdate;
  /** The next question, or null when the session is complete (always null for flashcard). */
  next: SessionQuestion | null;
  /**
   * True when the answer could NOT be submitted (network/transport failure, so
   * the server never graded it). The session did NOT advance — `next` holds the
   * SAME question so the caller can let the student retry rather than being
   * told they were wrong for a dropped request.
   */
  retriable?: boolean;
}

// ---------------------------------------------------------------------------
// Ad error — mirrors AdMob's LoadAdError / AdError
// ---------------------------------------------------------------------------

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

// ---------------------------------------------------------------------------
// Reward — mirrors AdMob's RewardItem
// ---------------------------------------------------------------------------

export interface RewardItem {
  type: "question_answered";
  amount: 0 | 1;
}

// ---------------------------------------------------------------------------
// Load callbacks
// ---------------------------------------------------------------------------

export interface AdLoadCallback {
  onAdLoaded: (ad: LevelMomentAdHandle) => void;
  onAdFailedToLoad: (error: LevelMomentAdError) => void;
}

// ---------------------------------------------------------------------------
// Show callbacks
// ---------------------------------------------------------------------------

export interface AdShowCallback {
  onAdShowed?: () => void;
  onAdFailedToShow?: (error: LevelMomentAdError) => void;
  onUserEarnedReward?: (reward: RewardItem) => void;
  onAdDismissed?: () => void;
}

// ---------------------------------------------------------------------------
// Ad handle
// ---------------------------------------------------------------------------

export interface LevelMomentAdHandle {
  isLoaded(): boolean;
  isThrottled(): boolean;
  show(callbacks: AdShowCallback): void;
  /**
   * Record an impression for the current question (idempotent per question).
   * show() covers the first question; the renderer calls this again for each
   * subsequent session question it displays.
   */
  noteQuestionShown(): void;
  /**
   * For session-based formats (quiz/deep_dive): submit the current answer
   * and advance to the next question. Returns the graded outcome, the
   * correct-answer reveal (on non-correct outcomes), the mastery preview,
   * and the next question (null when the session is complete).
   */
  advanceSession(answer: SessionAnswer): Promise<AdvanceResult>;
  /**
   * For flashcard format: submit the single question's answer and return the
   * graded result (next is always null). The caller shows feedback, then
   * fires reward/dismiss callbacks itself.
   */
  submitFlashcardAnswer(answer: SessionAnswer): Promise<AdvanceResult>;
  getLesson(): ConceptLesson | null;
  getInstruction(questionId: string): QuestionInstruction | null;
  getSessionSummary(): SessionSummary | null;
  /** Static session facts for progress UI, or null for flashcard format. */
  getSessionInfo(): SessionInfo | null;
  notifyCorrectAnswer(callbacks: AdShowCallback, answeredAt?: string): void;
  notifyDismissed(
    callbacks: AdShowCallback,
    answeredAt?: string,
    selectedIndex?: number,
  ): void;
  dispose(): void;
  readonly question: Question | null;
}

// ---------------------------------------------------------------------------
// Session answer (submitted via advanceSession)
// ---------------------------------------------------------------------------
//
// Note: there is no `impressionId` field. The SDK resolves the per-question
// impressionId internally from the synchronous-style POST /impressions issued
// at show() time (see LevelMomentAd._postImpressionForId / pendingImpressionIds).
// Game-side code never had a real value to put here; the field was removed
// to remove the foot-gun.

export interface SessionAnswer {
  answeredAt: string; // ISO 8601
  selectedIndex?: number;
  textAnswer?: string;
  orderedIndices?: number[];
  matchedPairs?: [number, number][];
  categoryAssignments?: number[];
}

// ---------------------------------------------------------------------------
// Config
// ---------------------------------------------------------------------------

export interface LevelMomentConfig {
  apiUrl: string;
  placementId: string;
  studentToken: string;
  /**
   * Maximum milliseconds to wait for any single API request before aborting.
   * Surfaces as a `network_error` on the load/show callbacks.
   * Defaults to 10 000 ms (10 seconds).
   */
  requestTimeoutMs?: number;
  /**
   * Server-side-verification custom data — the Level Moment analogue of
   * AdMob's `ServerSideVerificationOptions.customData`. An opaque string
   * (max 1024 characters) that is attached to every impression this ad
   * records and echoed back verbatim in the `reward.earned` webhook, so your
   * game server can correlate the reward to its own user / transaction
   * context. Values longer than 1024 chars are truncated server-side.
   */
  customData?: string;
}

// ---------------------------------------------------------------------------
// Impression event (internal — used by the queue)
// ---------------------------------------------------------------------------

export interface ImpressionEvent {
  /**
   * EXACTLY ONE of questionId / instanceId is set (XOR), matching the server's
   * impressions question_xor_instance CHECK:
   *   - questionId — a static curriculum question.
   *   - instanceId — a template-engine generated question (the served
   *     Question/SessionQuestion carried an `instanceId`).
   * The server rejects an event that sets both or neither with 400.
   */
  questionId?: string;
  /** Set instead of questionId when the shown question is a template instance. */
  instanceId?: string;
  placementId: string;
  shownAt: string;
  durationMs: number;
  /**
   * Client-generated UUID — required by `POST /impressions`. Produced by
   * `crypto.randomUUID()` once per show() call and reused on every retry of
   * that same impression. The server has a unique constraint on this column
   * and returns the existing impressionId on conflict, so a retried batch is
   * idempotent. NOT derived from request content; do not assume it can be
   * recomputed from (questionId, placementId, shownAt).
   */
  idempotencyKey: string;
  /**
   * SSV-parity custom data (see `LevelMomentConfig.customData`). Present only
   * when the ad was loaded with a `customData` option. Forwarded to
   * `POST /impressions`, capped at 1024 chars server-side, and echoed on the
   * `reward.earned` webhook.
   */
  customData?: string;
}

export interface AnswerPayload {
  questionId: string;
  shownAt: string;
  answeredAt: string;
  selectedIndex?: number;
  textAnswer?: string;
  orderedIndices?: number[];
  matchedPairs?: [number, number][];
  categoryAssignments?: number[];
  answerData?: Record<string, unknown>;
}
