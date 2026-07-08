# Changelog

All notable changes to `@levelmoment/sdk-core` are documented here. Format
follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/);
versioning follows [Semver](https://semver.org).

## [Unreleased]

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
- `ImpressionQueue` — batches and retries impressions with exponential
  backoff. Backed by a `QueueStorage` interface so platform SDKs can plug
  in their own persistence (localStorage / AsyncStorage / SharedPreferences /
  PlayerPrefs).
- `TokenStore` interface plus `MemoryTokenStore` reference implementation.
- All 8 question meta types: `MultipleChoiceMeta`, `TextInputMeta`,
  `OrderingMeta`, `MatchingMeta`, `CategorizingMeta`, `FillInBlankMeta`,
  `MultipleChoiceImageMeta`, `ImagePromptMeta`.
- Session types: `BreakSession`, `SessionSummary`, `SessionAnswer`,
  `BreakFormat`.
