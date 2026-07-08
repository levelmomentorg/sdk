# Changelog

All notable changes to `@levelmoment/sdk-react-native` are documented here.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/);
versioning follows [Semver](https://semver.org).

## [Unreleased]

## [0.1.0] — 2026-05-10

First public release. Drop-in replacement for
`react-native-google-mobile-ads` rewarded ads.

### Added

- `LevelMomentAd.createForAdRequest(placementId, options)` — factory mirroring
  AdMob's `RewardedAd.createForAdRequest`.
- `ad.addAdEventListener(event, listener)` returning an unsubscribe
  function. Event names mirror AdMob:
  - `"loaded"`, `"opened"`, `"closed"`, `"earnedReward"`, `"error"`.
- `ad.load()` / `ad.show()` — load and show separation maintained.
- `ad.dispose()` for cleanup.
- `<LevelMomentAdModal />` — fullscreen modal + WebView component that renders
  the question break. Mount once at app root.
- Peer dependencies: `react-native >= 0.72.0`, `react-native-webview >= 13.0.0`.
- `bin/install-skill` (run via `npx @levelmoment/sdk-react-native install-skill`)
  installs the `/levelmoment` Claude Code skill into the user's repo for
  automatic porting.

### Architecture

The SDK is a thin WebView wrapper around the hosted `/break` page; all
question-rendering UI lives at `app.levelmoment.com/break`. This keeps the
native bundle small and ensures all platforms (web, iOS, Android, Unity)
render identical UI. See ADR-001 in the repository.

### Migration

If migrating from `react-native-google-mobile-ads`, see
[MIGRATION.md](./MIGRATION.md). Or use the
[`/levelmoment` Claude Code skill](https://app.levelmoment.com/docs/porting/using-claude-code).
