# @levelmoment/sdk-core

**Status: ✅ Complete — full 8-type question support, session-based ad breaks, mastery tracking**

Platform-agnostic SDK logic shared across all LevelMoment platform SDKs (web, React Native, Flutter). Contains types, auth helpers, the impression queue, and the core `LevelMomentAd` class.

Platform SDKs (`sdk/web`, `sdk/react-native`, `sdk/flutter`) wrap this package with their own storage implementations and platform-specific APIs.

---

## What's Done

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

## What's Not Done

- [ ] `LevelMomentAd` unit tests — `ImpressionQueue` and `MemoryQueueStorage` are covered; add tests for `LevelMomentAd.load/show/advanceSession`
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

| File                | Purpose                                               |
| ------------------- | ----------------------------------------------------- |
| `src/types.ts`      | All shared interfaces and types                       |
| `src/auth.ts`       | Token management interfaces + `buildAuthHeader()`     |
| `src/queue.ts`      | Impression queue with pluggable storage               |
| `src/ad.ts`         | `LevelMomentAd` — the core ad unit (load/show/notify) |
| `src/index.ts`      | Public API exports                                    |
| `src/queue.test.ts` | Vitest unit tests for `ImpressionQueue` + storage     |

---

## Design Decisions

- **`fetchFn` is injected** into `LevelMomentAd.load()` so tests can provide a mock without patching globals.
- **`QueueStorage` and `TokenStore` are interfaces**, not implementations. Platform SDKs provide `localStorage` (web), `AsyncStorage` (RN), or `SharedPreferences` (Flutter) backends.
- **Impression queue uses a background interval** (default 10s). It also flushes immediately on `tryFlush()`, which platform SDKs call on app background/close.
