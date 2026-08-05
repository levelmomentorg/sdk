# Changelog

All notable changes to `levelmoment_ads` are documented here. Format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/); versioning
follows [Semver](https://semver.org).

## [Unreleased]

### Changed

- **WebView refactor (ADR-001).** The SDK is now a thin WebView shell over the
  hosted `/break` page (`platform/web/app/break`). The page renders all 8
  question types, the session flow, lessons, answer submission, AND the
  impression queue. `LevelMomentRewardedAd.show()` now pushes a fullscreen
  `LevelMomentWebView` route instead of the Dart `LevelMomentBreakScreen` renderer.
- `LevelMomentRewardedAd.load()` is now a synchronous mark-ready (no native
  question fetch); the fetch happens inside the WebView on `show()`.

### Added

- `LevelMomentWebView` — fullscreen `WebView` host with a `ReactNativeWebView`
  JavaScript channel bridging `ready` / `earnedReward` / `dismissed` / `error`,
  with terminal-once dismiss semantics.
- `test/rewarded_ad_test.dart` — Dart unit tests for URL building, the
  `not_loaded` guard, `HostMessage` parsing, and terminal-once.

### Breaking

- `LevelMomentAds.instance.initialize()` now **requires** `breakUrl:` in addition
  to `apiUrl:` (the WebView needs to know where the hosted page lives). Added
  an optional `mock:` flag that forwards `mock=true` to the page.
- Removed internal data-model types (`Question`, `QuestionMeta` + subtypes,
  `SessionQuestion`, `ConceptLesson`, `LessonPage`, `SessionSummary`) and the
  `question` / `lesson` / `sessionSummary` getters and
  `notifyDismissed` / `advanceSession` methods — the hosted page owns all of
  this now. The error / reward / callback public surface is unchanged.
- Deleted the Dart renderer `lib/src/widgets/level_moment_break_screen.dart`.

### Removed

- Dependencies `http` and `shared_preferences` (only used by the deleted
  fetch/answer/impression code); added `webview_flutter: ^4.7.0`. The
  `SharedPreferences`-backed impression queue was flush-less dead code and is
  removed entirely — the hosted page owns impressions.

## [0.1.0] — 2026-05-10

First public release. Drop-in replacement for `google_mobile_ads` rewarded
ads. Installed via Git source for Phase 1; pub.dev publication deferred
to Phase 2.

### Added

- `LevelMomentAds.instance.initialize(apiUrl:)` — mirrors `MobileAds.instance.initialize()`.
- `LevelMomentRewardedAd.load(placementId, studentToken, adLoadCallback, format?)` —
  static factory mirroring `RewardedAd.load`.
- `LevelMomentAdLoadCallback` with `onAdLoaded` / `onAdFailedToLoad`.
- `LevelMomentFullScreenContentCallback` with `onAdShowedFullScreenContent` /
  `onAdFailedToShowFullScreenContent` / `onAdDismissedFullScreenContent`.
- `ad.show(context:, onUserEarnedReward:)` — pushes the `LevelMomentBreakScreen`
  as a full-screen route.
- `LevelMomentBreakScreen` widget renders all 8 question types + session
  progression + deep-dive lessons.
- `SharedPreferences`-backed impression queue with persistence across app
  restarts.

### Migration

See [MIGRATION.md](./MIGRATION.md) for a line-by-line swap from
`google_mobile_ads`. Or use the
[`/levelmoment` Claude Code skill](https://app.levelmoment.com/docs/porting/using-claude-code).
