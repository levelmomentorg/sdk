// Mirrors AdMob RewardedAd API — see sdk/*/MIGRATION.md for swap guides.

import type {
  LevelMomentConfig,
  AdLoadCallback,
  AdShowCallback,
  AdvanceResult,
  AnswerOutcome,
  CorrectReveal,
  LevelMomentAdHandle,
  Question,
  BreakFormat,
  BreakSession,
  SessionInfo,
  SessionAnswer,
  SessionSummary,
  ConceptLesson,
  QuestionInstruction,
  ProgressUpdate,
  ImpressionEvent,
} from "./types.js";
import type { ImpressionQueue } from "./queue.js";
import { buildAuthHeader } from "./auth.js";

interface LoadOptions {
  format?: BreakFormat;
}

/**
 * Generate a UUID-style idempotency key using `crypto.randomUUID()`.
 *
 * Hard-required, not a fallback chain: the server's recovery path for
 * already-accepted impressions is keyed on (idempotencyKey, developerId,
 * isSandbox), and a collision-prone fallback (Math.random / timestamp) could
 * silently produce duplicate keys that escape the unique constraint and
 * cause an impression on one show() to be returned to a later show() for the
 * same developer. crypto.randomUUID() is available on all browsers (since
 * Safari 15.4 / Chrome 92 / Firefox 95), Node ≥19, and React Native via the
 * standard `react-native-get-random-values` polyfill — runtimes older than
 * that aren't part of the SDK's supported matrix.
 */
function generateIdempotencyKey(): string {
  const c = (globalThis as { crypto?: { randomUUID?: () => string } }).crypto;
  if (typeof c?.randomUUID !== "function") {
    throw new Error(
      "LevelMoment SDK requires crypto.randomUUID() — supported on all modern " +
        "browsers, Node ≥19, and React Native via the " +
        "react-native-get-random-values polyfill. Please upgrade your runtime.",
    );
  }
  return c.randomUUID();
}

/** One graded answer from POST /answers. */
interface GradedAnswer {
  outcome?: AnswerOutcome;
  progressUpdate?: ProgressUpdate;
  reveal?: CorrectReveal;
}

/**
 * Map a graded answer to the public AdvanceResult. A failed submission
 * (network error → null) reports as "skipped": the renderer advances without
 * celebrating or revealing.
 */
function toAdvanceResult(graded: GradedAnswer | null): AdvanceResult {
  return {
    outcome: graded?.outcome ?? "skipped",
    reveal: graded?.reveal,
    progressUpdate: graded?.progressUpdate,
    next: null,
  };
}

export class LevelMomentAd implements LevelMomentAdHandle {
  private _question: Question | null = null;
  private _session: BreakSession | null = null;
  private _currentIndex = 0;
  private _correctCount = 0;
  private _progressUpdates: ProgressUpdate[] = [];
  private _throttled = false;
  private _disposed = false;

  // Resolved impressionIds keyed by questionId. Populated by the
  // synchronous-style POST /impressions issued at show() time. Awaited by
  // notifyCorrectAnswer / notifyDismissed / advanceSession so the answer
  // POST can carry a real impressionId rather than the empty-string sentinel
  // that the server's strict ownership check rejects on student sessions.
  private _pendingImpressionIds = new Map<string, Promise<string>>();

  private constructor(
    private readonly config: LevelMomentConfig,
    private readonly queue: ImpressionQueue,
    private readonly fetchFn: typeof fetch,
  ) {}

  /**
   * Fetch and cache the next question (flashcard) or create a break session
   * (quiz/deep_dive) in the background.
   *
   * format defaults to "flashcard".
   */
  static load(
    config: LevelMomentConfig,
    queue: ImpressionQueue,
    callbacks: AdLoadCallback,
    options: LoadOptions = {},
    // Wrapped (not `globalThis.fetch` directly): the raw reference is unbound,
    // and invoking it as `this.fetchFn(...)` throws "Illegal invocation" in
    // browsers. The wrapper also picks up runtime fetch swaps (mock mode).
    fetchFn: typeof fetch = (input, init) => globalThis.fetch(input, init),
  ): void {
    const ad = new LevelMomentAd(config, queue, fetchFn);
    const format = options.format ?? "flashcard";
    void ad._fetch(callbacks, format);
  }

  get question(): Question | null {
    if (this._session) {
      const q = this._session.questions[this._currentIndex];
      // Carry instanceId through so show() can route a template instance to
      // the impression's instanceId field (not questionId).
      return q
        ? {
            id: q.id,
            meta: q.meta,
            ...(q.instanceId ? { instanceId: q.instanceId } : {}),
          }
        : null;
    }
    return this._question;
  }

  isLoaded(): boolean {
    if (this._disposed) return false;
    if (this._session) return this._session.questions.length > 0;
    return this._question !== null;
  }

  isThrottled(): boolean {
    return this._throttled;
  }

  getLesson(): ConceptLesson | null {
    return this._session?.lesson ?? null;
  }

  getInstruction(questionId: string): QuestionInstruction | null {
    if (!this._session) return null;
    const q = this._session.questions.find((sq) => sq.id === questionId);
    return q?.instruction ?? null;
  }

  getSessionSummary(): SessionSummary | null {
    if (!this._session) return null;
    const totalAnswered = this._currentIndex;
    const passed =
      totalAnswered > 0
        ? Math.round((this._correctCount / totalAnswered) * 100) >=
          this._session.passThreshold
        : false;
    return {
      correctCount: this._correctCount,
      totalAnswered,
      passed,
      progressUpdates: [...this._progressUpdates],
    };
  }

  show(callbacks: AdShowCallback): void {
    if (!this.isLoaded()) {
      callbacks.onAdFailedToShow?.({
        code: "not_loaded",
        message:
          "LevelMomentAd.show() called before onAdLoaded fired. Call load() first.",
      });
      return;
    }

    const q = this.question;
    if (!q) {
      callbacks.onAdFailedToShow?.({
        code: "not_loaded",
        message: "No question available.",
      });
      return;
    }

    // Mint the first question's impression NOW only when it is shown
    // immediately. A deep_dive break opens on its lesson, so its first question
    // isn't visible yet — the renderer mints that impression (with a correct
    // shownAt) when the lesson finishes and the question actually displays.
    if (!this.getLesson()) {
      this.noteQuestionShown();
    }

    callbacks.onAdShowed?.();
  }

  /**
   * Record an impression for the CURRENT question (idempotent per question).
   * `show()` calls this for the first question; the renderer calls it again
   * each time a subsequent session question is displayed, so every question
   * in a quiz/deep_dive break gets its own impression row — the unit the
   * payout pool is divided by. Without this, only question 1 of a session
   * was ever recorded.
   */
  noteQuestionShown(): void {
    const q = this.question;
    if (!q || this._pendingImpressionIds.has(q.id)) return;

    const shownAt = new Date().toISOString();
    const idempotencyKey = generateIdempotencyKey();
    // Option A wire: a template-engine instance carries `instanceId` (its `id`
    // is a question_instances.id); send instanceId. Otherwise send questionId.
    // Exactly one is set — matches the server's XOR validation + DB CHECK.
    const event: ImpressionEvent = {
      ...(q.instanceId ? { instanceId: q.instanceId } : { questionId: q.id }),
      placementId: this.config.placementId,
      shownAt,
      durationMs: 0,
      idempotencyKey,
    };

    // The retry/offline path: even if the synchronous POST below fails or the
    // app dies before answers are submitted, this enqueued event will be
    // re-flushed on the next interval. The server's unique constraint on
    // idempotencyKey makes the double-write safe.
    this.queue.enqueue(event);

    // Synchronous-style POST so notify*/advanceSession can `await` a real
    // impressionId and pass it to /answers. On failure the promise rejects;
    // _submitAnswer treats that as "no impressionId" and the strict student
    // ownership check on /answers surfaces it as 403, which is the correct
    // behavior — we shouldn't paper over a failed impression write.
    this._pendingImpressionIds.set(q.id, this._postImpressionForId(event));
  }

  /**
   * Submit the current question's answer and advance to the next question.
   * Returns the graded outcome, correct-answer reveal, mastery preview, and
   * the next SessionQuestion (null when the session is complete).
   */
  async advanceSession(answer: SessionAnswer): Promise<AdvanceResult> {
    if (!this._session) {
      return { outcome: "skipped", next: null };
    }

    const currentQuestionId = this.question?.id;

    // Submit answer (await the per-question impressionId resolved at show()).
    const impressionId = currentQuestionId
      ? await this._resolveImpressionId(currentQuestionId)
      : "";
    const graded = await this._submitAnswer({ ...answer, impressionId });

    // Submission failed (network/transport) — do NOT advance. Return the same
    // session question so the caller can retry; the student is never told they
    // were wrong for a dropped request, and the impression/score stay consistent.
    if (graded === null) {
      return {
        outcome: "skipped",
        next: this._session.questions[this._currentIndex] ?? null,
        retriable: true,
      };
    }

    if (graded.progressUpdate) {
      this._progressUpdates.push(graded.progressUpdate);
    }
    if (graded.outcome === "correct") {
      this._correctCount++;
    }

    this._currentIndex++;

    const result = toAdvanceResult(graded);

    if (this._currentIndex >= this._session.questions.length) {
      // Session complete — notify server
      const summary = this.getSessionSummary();
      void this._completeSession(summary);
      return result;
    }

    result.next = this._session.questions[this._currentIndex] ?? null;
    return result;
  }

  /**
   * Submit the flashcard question's answer and return the graded result
   * (next is always null). The caller shows feedback, then fires
   * reward/dismiss callbacks itself.
   */
  async submitFlashcardAnswer(answer: SessionAnswer): Promise<AdvanceResult> {
    if (!this._question) {
      return { outcome: "skipped", next: null };
    }
    const impressionId = await this._resolveImpressionId(this._question.id);
    const graded = await this._submitAnswer({ ...answer, impressionId });
    // Failed submission → retriable, keep the same question mounted.
    if (graded === null) {
      return { outcome: "skipped", next: null, retriable: true };
    }
    return toAdvanceResult(graded);
  }

  getSessionInfo(): SessionInfo | null {
    if (!this._session) return null;
    return {
      currentIndex: this._currentIndex,
      totalQuestions: this._session.questions.length,
      passThreshold: this._session.passThreshold,
    };
  }

  /**
   * Call when the student submits a correct answer (flashcard only).
   * Fires onUserEarnedReward → onAdDismissed.
   *
   * Public signature stays synchronous (`void`) — the answer submission is
   * dispatched as a fire-and-forget IIFE that internally awaits the
   * impressionId promise stored by `show()`.
   */
  notifyCorrectAnswer(
    callbacks: AdShowCallback,
    answeredAt = new Date().toISOString(),
  ): void {
    if (!this._question) return;
    const questionId = this._question.id;
    void (async () => {
      const impressionId = await this._resolveImpressionId(questionId);
      await this._submitAnswer({ impressionId, answeredAt });
    })();
    callbacks.onUserEarnedReward?.({ type: "question_answered", amount: 1 });
    callbacks.onAdDismissed?.();
    this.dispose();
  }

  /**
   * Call when the student submits a wrong answer or skips (flashcard only).
   * Fires onUserEarnedReward(amount:0) → onAdDismissed.
   *
   * Public signature stays synchronous (`void`) — see notifyCorrectAnswer.
   */
  notifyDismissed(
    callbacks: AdShowCallback,
    answeredAt = new Date().toISOString(),
    selectedIndex?: number,
  ): void {
    if (!this._question) return;
    const questionId = this._question.id;
    void (async () => {
      const impressionId = await this._resolveImpressionId(questionId);
      await this._submitAnswer({ impressionId, answeredAt, selectedIndex });
    })();
    callbacks.onUserEarnedReward?.({ type: "question_answered", amount: 0 });
    callbacks.onAdDismissed?.();
    this.dispose();
  }

  dispose(): void {
    this._disposed = true;
    this._question = null;
  }

  private async _fetch(
    callbacks: AdLoadCallback,
    format: BreakFormat,
  ): Promise<void> {
    try {
      if (format === "flashcard") {
        await this._fetchFlashcard(callbacks);
      } else {
        await this._fetchSession(callbacks, format);
      }
    } catch (err) {
      callbacks.onAdFailedToLoad({
        code: "network_error",
        message: err instanceof Error ? err.message : "Network error",
      });
    }
  }

  private async _fetchFlashcard(callbacks: AdLoadCallback): Promise<void> {
    const params = new URLSearchParams({
      placementId: this.config.placementId,
      format: "flashcard",
    });

    const res = await this.fetchFn(
      `${this.config.apiUrl}/questions?${params}`,
      { headers: buildAuthHeader(this.config.studentToken) },
    );

    if (!res.ok) {
      callbacks.onAdFailedToLoad({
        code: res.status === 401 ? "invalid_token" : "unknown",
        message: `HTTP ${res.status}`,
      });
      return;
    }

    const data = (await res.json()) as {
      question: Question | null;
      throttled?: boolean;
    };

    if (!data.question) {
      callbacks.onAdFailedToLoad({
        code: "no_fill",
        message: "No question available.",
      });
      return;
    }

    this._question = data.question;
    this._throttled = data.throttled ?? false;
    callbacks.onAdLoaded(this);
  }

  private async _fetchSession(
    callbacks: AdLoadCallback,
    format: "quiz" | "deep_dive",
  ): Promise<void> {
    const res = await this.fetchFn(`${this.config.apiUrl}/break-sessions`, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        ...buildAuthHeader(this.config.studentToken),
      },
      body: JSON.stringify({ format }),
    });

    if (!res.ok) {
      callbacks.onAdFailedToLoad({
        code: res.status === 401 ? "invalid_token" : "unknown",
        message: `HTTP ${res.status}`,
      });
      return;
    }

    const data = (await res.json()) as BreakSession & {
      session: null | undefined;
    };

    if (data.session === null) {
      callbacks.onAdFailedToLoad({
        code: "no_fill",
        message: "No questions available.",
      });
      return;
    }

    this._session = data as BreakSession;
    this._throttled = data.throttled ?? false;
    this._currentIndex = 0;
    callbacks.onAdLoaded(this);
  }

  /**
   * Issue a single-event POST /impressions and return the server-minted
   * `impressionId` matching this event's idempotencyKey. Used to populate
   * `_pendingImpressionIds` so answer submissions can carry a real id.
   *
   * Returns "" on any failure — caller treats that as "no impressionId" and
   * the answer POST will surface the failure via the server's ownership
   * check rather than crashing the show flow.
   */
  private async _postImpressionForId(event: ImpressionEvent): Promise<string> {
    try {
      const res = await this.fetchFn(`${this.config.apiUrl}/impressions`, {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          ...buildAuthHeader(this.config.studentToken),
        },
        body: JSON.stringify({ events: [event] }),
      });
      if (!res.ok) return "";
      const data = (await res.json()) as {
        results?: Array<{ idempotencyKey: string; impressionId: string }>;
      };
      const match = data.results?.find(
        (r) => r.idempotencyKey === event.idempotencyKey,
      );
      return match?.impressionId ?? "";
    } catch {
      return "";
    }
  }

  /**
   * Pull a per-question impressionId resolved by `show()`. If the promise was
   * never set (no show() for this question), returns "". `_postImpressionForId`
   * already catches all failures and resolves with "", so this never needs to
   * handle a rejected promise.
   */
  private async _resolveImpressionId(questionId: string): Promise<string> {
    return (await this._pendingImpressionIds.get(questionId)) ?? "";
  }

  private async _submitAnswer(
    answer: Partial<SessionAnswer> & { impressionId?: string },
  ): Promise<GradedAnswer | null> {
    try {
      const res = await this.fetchFn(`${this.config.apiUrl}/answers`, {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          ...buildAuthHeader(this.config.studentToken),
        },
        body: JSON.stringify({
          answers: [
            {
              impressionId: answer.impressionId ?? "",
              answeredAt: answer.answeredAt ?? new Date().toISOString(),
              selectedIndex: answer.selectedIndex,
              textAnswer: answer.textAnswer,
              orderedIndices: answer.orderedIndices,
              matchedPairs: answer.matchedPairs,
              categoryAssignments: answer.categoryAssignments,
            },
          ],
        }),
      });
      if (!res.ok) return null;
      const data = (await res.json()) as { outcomes?: GradedAnswer[] };
      return data.outcomes?.[0] ?? null;
    } catch {
      return null;
    }
  }

  private async _completeSession(
    summary: SessionSummary | null,
  ): Promise<void> {
    if (!this._session) return;
    try {
      await this.fetchFn(
        `${this.config.apiUrl}/break-sessions/${this._session.sessionId}`,
        {
          method: "PATCH",
          headers: {
            "Content-Type": "application/json",
            ...buildAuthHeader(this.config.studentToken),
          },
          body: JSON.stringify({
            status: "completed",
            correctCount: summary?.correctCount ?? 0,
            totalAnswered: summary?.totalAnswered ?? 0,
          }),
        },
      );
    } catch {
      // Best-effort
    }
  }
}
