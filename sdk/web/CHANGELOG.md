# Changelog

All notable changes to `@levelmoment/sdk-web` are documented here. Format
follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/);
versioning follows [Semver](https://semver.org).

## [Unreleased]

## [0.1.0] — 2026-05-10

First public release. Drop-in replacement for AdSense / Google Ad Manager
rewarded interstitials for HTML5 games.

### Added

- `LevelMomentWebClient.initialize(config)` returns a singleton client.
- `client.start()` begins the background impression-flush loop.
- `client.loadAd(callbacks, options?)` preloads a question break. The
  optional `options.format` accepts `flashcard` | `quiz` | `deep_dive`.
- `LevelMomentWebAd.show(callbacks)` renders the full DOM overlay with all
  8 question types, session progression, deep-dive lessons, countdown
  timers, and inline grading feedback.
- `ad.isLoaded()` to test before calling `show()`.
- `LocalStorageTokenStore` and `LocalStorageQueueStorage` for browser
  persistence.
- Mock mode: pass `mock: true` in the client config to serve canned
  questions offline (useful in tests).
- `bin/install-skill` (run via `npx @levelmoment/sdk-web install-skill`)
  installs the `/levelmoment` Claude Code skill into the user's repo for
  automatic porting from AdMob / AppLovin / google_mobile_ads.

### Migration

If migrating from Google AdSense / Ad Manager, see
[MIGRATION.md](./MIGRATION.md). The port is a 10-line swap; the
[`/levelmoment` Claude Code skill](https://app.levelmoment.com/docs/porting/using-claude-code)
can do it automatically.
