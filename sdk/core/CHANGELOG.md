# Changelog

All notable changes to `@levelmoment/sdk-core` are documented here. Format
follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/);
versioning follows [Semver](https://semver.org).

## [Unreleased]

### Added

- `requestTimeoutMs` option on `LevelMomentConfig` (default 10 000 ms). Every
  API call (`/questions`, `/break-sessions`, `/impressions`, `/answers`,
  `PATCH /break-sessions/:id`) is now bounded by an `AbortSignal.timeout`;
  a stalled connection no longer hangs the SDK at a game pause point. Abort
  errors surface through the existing `network_error` code path on load and
  show callbacks.

### Fixed

- `ImpressionQueue` now implements **real** exponential back-off on flush
  failure: after each consecutive failure the next scheduled attempt is delayed
  by `min(baseInterval × 2^(consecutiveFailures − 1), 5 min)`. The first retry
  waits one normal interval; each further consecutive failure doubles the delay
  (1×, 2×, 4×, … up to 5 min). The back-off resets to zero on the first
  successful flush. Back-off applies **only** to the automatic timer ticks
  started by `start()`; a direct `tryFlush()` (used by platform SDKs on app
  background/close) always attempts immediately and never creates a back-off
  window. Previous behaviour retried on every tick at a fixed 10 s interval
  regardless of failure count; this release is the first version where back-off
  is actually implemented (the 0.1.0 CHANGELOG entry that described
  "exponential backoff" was incorrect).

## [0.1.2] — 2026-07-08

### Changed

- Publish pipeline migrated to OIDC trusted publishing (no long-lived
  `NPM_TOKEN`). No functional changes to the package.

## [0.1.1] — 2026-07-08

### Changed

- Version bump across all SDK packages to verify the OIDC trusted-publishing
  pipeline end-to-end. No functional changes to the package.

## [0.1.0] — 2026-05-10

First public release. The package is platform-agnostic; consumers should
typically install one of the platform SDKs (`@levelmoment/sdk-web`,
`@levelmoment/sdk-react-native`) which depend on this package.

### Added

- `LevelMomentAd.load(config, queue, callbacks, options?, fetchFn?)` — preload
  a question break in the background.
- `LevelMomentAd.show(callbacks)` — display the cached break.
- `ad.advanceSession(answer)` — submit an answer and step to the next
  question in a multi-question session.
- `ad.isThrottled()` — check whether the impression would count toward
  payouts.
- `ad.getLesson()` — retrieve the concept lesson for `deep_dive` breaks.
- `ad.getSessionSummary()` — post-session stats (correct count, duration).
- `ad.notifyCorrectAnswer()` / `ad.notifyDismissed()` — manual lifecycle
  for flashcard format consumers that own their own UI.
- `ImpressionQueue` — batches and retries impressions with a fixed 10 s retry
  interval. Backed by a `QueueStorage` interface so platform SDKs can plug
  in their own persistence (localStorage / AsyncStorage / SharedPreferences /
  PlayerPrefs).
- `TokenStore` interface plus `MemoryTokenStore` reference implementation.
- All 8 question meta types: `MultipleChoiceMeta`, `TextInputMeta`,
  `OrderingMeta`, `MatchingMeta`, `CategorizingMeta`, `FillInBlankMeta`,
  `MultipleChoiceImageMeta`, `ImagePromptMeta`.
- Session types: `BreakSession`, `SessionSummary`, `SessionAnswer`,
  `BreakFormat`.
