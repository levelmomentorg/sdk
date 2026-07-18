# @levelmoment/sdk-core

**Status: ✅ Complete — full 8-type question support, session-based ad breaks, mastery tracking**

Platform-agnostic SDK logic shared across all LevelMoment platform SDKs (web, React Native, Flutter). Contains types, auth helpers, the impression queue, and the core `LevelMomentAd` class.

Platform SDKs (`sdk/web`, `sdk/react-native`, `sdk/flutter`) wrap this package with their own storage implementations and platform-specific APIs.

---

## What's Done

- **Silent device→session exchange** — when the configured token is rejected
  (401), `LevelMomentAd` tries it as a parent-issued device token exactly
  once: `POST /sessions { placementId, deviceToken }`, then retries with the
  minted game-scoped session token. Session and sandbox tokens never pay the
  cost; the token a parent copies from Approve-device works as-is in
  `?token=`. The replacement propagates to answer submission and the
  impression-queue flush.

- **`src/types.ts`** — all shared domain types:
  - 8 safe question meta types: `MultipleChoiceMeta`, `MultipleChoiceImageMeta`, `ImagePromptMeta`, `TextInputMeta`, `OrderingMeta`, `FillInBlankMeta`, `MatchingMeta`, `CategorizingMeta`. All carry an optional `visual?: { src: string; alt: string }` (#122) — a base64 `data:image/svg+xml` URI for a parametric visual model; carries no answer data, so it passes `stripAnswerData` untouched
  - `QuestionMeta` discriminated union + `Question`, `SessionQuestion`
  - `BreakFormat` (`"flashcard" | "quiz" | "deep_dive"`), `BreakSession`, `SessionSummary`, `SessionAnswer`
  - `MasteryStatus`, `ProgressUpdate`, `ConceptLesson`, `QuestionInstruction`
  - `LevelMomentConfig`, `AdLoadCallback`, `AdShowCallback`, `LevelMomentAdHandle`, `RewardItem`, `LevelMomentAdError`
  - `ImpressionEvent`, `AnswerPayload`
- **`src/auth.ts`** — `TokenStore` interface, `MemoryTokenStore` implementation, `buildAuthHeader()` utility
- **`src/queue.ts`** — `ImpressionQueue` class with `QueueStorage` interface, `MemoryQueueStorage` fallback, background flush loop with retry
- **`src/ad.ts`** — `LevelMomentAd` class implementing the full load/show/session pattern

  - `LevelMomentAd.load(config, queue, callbacks, options?, fetchFn?)` — static factory; `options.format` selects flashcard (default), quiz, or deep_dive
  - `ad.show(callbacks)` — displays from cache (no network wait)
  - `ad.isThrottled()` — returns true when throttle limit reached (impression still recorded)
  - `ad.advanceSession(answer)` — submits current answer; returns an `AdvanceResult` `{ outcome, reveal?, progressUpdate?, next }` (graded outcome + correct-answer teach-back; `next` is `null` when the session is complete)
  - `ad.submitFlashcardAnswer(answer)` — flashcard grading path; same `AdvanceResult` shape (`next` always `null`) so the renderer can celebrate/reveal before firing reward + dismiss
  - `ad.noteQuestionShown()` — records an impression for the CURRENT question, idempotent per question. `show()` covers question 1; the renderer calls it again for each subsequent session question so every question earns its own impression row
  - `ad.getLesson()` — returns `ConceptLesson` for deep_dive sessions
  - `ad.getInstruction(questionId)` — returns per-question `QuestionInstruction` if available
  - `ad.getSessionSummary()` — returns `SessionSummary` with correct count, pass/fail, progress updates
  - `ad.getSessionInfo()` — `{ currentIndex, totalQuestions, passThreshold }` for progress UI (`null` for flashcard)
  - `ad.notifyCorrectAnswer(callbacks, answeredAt)` — legacy flashcard path; fires `onUserEarnedReward(amount: 1)`
  - `ad.notifyDismissed(callbacks, answeredAt, selectedIndex)` — legacy flashcard path; fires `onAdDismissed`
  - `ad.dispose()` — releases resources

- **`requestTimeoutMs`** — pass in `LevelMomentConfig` to bound every API call (default 10 000 ms). Uses `AbortSignal.timeout`; surfaces as `network_error` on the load/show callbacks.
- **`customData` (SSV parity)** — pass an opaque string (max 1024 chars) in `LevelMomentConfig`, the analogue of AdMob's `ServerSideVerificationOptions.customData`. It is attached to every impression the ad records and echoed back verbatim on the `reward.earned` webhook, so your game server can correlate the reward with its own user/transaction context. Longer values are truncated to 1024 chars server-side. **Security:** the value originates from the client and is echoed verbatim; the webhook HMAC vouches for the event, not the customData content — never use it alone to authorize value grants without verifying the claimed context against your own server-side state.
- **Exponential back-off in `ImpressionQueue`** — on a failed scheduled flush the queue delays the next attempt by `min(baseInterval × 2^(consecutiveFailures − 1), 5 min)` (1×, 2×, 4×, … so the first retry waits one normal interval). Resets on the first successful flush. Back-off applies only to the automatic timer ticks from `start()`; a direct `tryFlush()` never creates a back-off window.

## What's Not Done

- [ ] Impression queue has no max-size cap — add pruning to prevent unbounded growth on persistent network failure
- [ ] `_submitAnswer` is best-effort (errors swallowed) — consider a retry queue for answers too

---

## Setup

```bash
# From repo root
npm install

# Run unit tests (Vitest)
cd sdk/core && npx vitest run

# Type-check this package only
npx tsc --noEmit

# Build (ESM + CJS dual bundle via tsup)
npm run build
```

---

## Key Files

| File                | Purpose                                                                |
| ------------------- | ---------------------------------------------------------------------- |
| `src/types.ts`      | All shared interfaces and types                                        |
| `src/auth.ts`       | Token management interfaces + `buildAuthHeader()`                      |
| `src/queue.ts`      | Impression queue with pluggable storage                                |
| `src/ad.ts`         | `LevelMomentAd` — the core ad unit (load/show/notify)                  |
| `src/index.ts`      | Public API exports                                                     |
| `src/queue.test.ts` | Vitest unit tests for `ImpressionQueue` + storage + back-off schedule  |
| `src/ad.test.ts`    | Vitest unit tests for `LevelMomentAd` wire protocol + timeout behavior |

---

## Design Decisions

- **`fetchFn` is injected** into `LevelMomentAd.load()` so tests can provide a mock without patching globals.
- **`QueueStorage` and `TokenStore` are interfaces**, not implementations. Platform SDKs provide `localStorage` (web), `AsyncStorage` (RN), or `SharedPreferences` (Flutter) backends.
- **Impression queue uses a background interval** (default 10s). It also flushes immediately on `tryFlush()`, which platform SDKs call on app background/close — a direct `tryFlush()` always attempts regardless of any back-off window and never creates one.
- **`requestTimeoutMs`** is applied via `AbortSignal.timeout` to every outbound fetch — a stalled connection no longer hangs the SDK at a pause point. The default (10 000 ms) is intentionally conservative; games that use a fast CDN may want to lower it.
- **Queue back-off** is computed as `min(baseInterval × 2^(consecutiveFailures − 1), 5 min)` — the first retry waits one normal interval, then each further consecutive failure doubles the delay. The count resets to zero on every successful flush, so a transient outage does not permanently slow the queue.
