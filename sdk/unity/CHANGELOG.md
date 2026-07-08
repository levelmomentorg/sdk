# Changelog

All notable changes to `com.levelmoment.sdk` (Unity UPM package) are documented
here. Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/);
versioning follows [Semver](https://semver.org).

## [Unreleased]

- Full Runtime + Editor C# implementation (currently scaffolded).
- Unity Asset Store listing.

### Security

- Added `UrlSafety.BuildUrl` helper that throws on credential-shaped
  query keys (`token`, `authorization`, `bearer`, `api_key`, …). All
  SDK URL construction (`/questions`, `/answers`, `/impressions`) now
  routes through it so the student session token can never accidentally
  end up in a URL — it is passed only via the `Authorization: Bearer`
  request header.
- Fixed misleading comment on `LevelMomentConfig.StudentToken` that
  previously claimed the token rode in the `?token=` query string.

## [0.1.0] — 2026-05-10

First tagged release. UPM package metadata + planned API documented.

### Added

- `package.json` (UPM manifest) with `com.levelmoment.sdk` namespace.
- Planned C# API mirroring Unity Ads SDK:
  - `LevelMomentAds.Initialize(LevelMomentConfig)`
  - `LevelMomentAds.Load(placementId, ILevelMomentLoadListener)`
  - `LevelMomentAds.Show(placementId, ILevelMomentShowListener)` — returns a
    `Question` object for the game's UI to render.
  - `LevelMomentAds.IsReady(placementId)`
  - `LevelMomentAds.NotifyAnswer(placementId, selectedIndex, isCorrect, listener)`
- `MIGRATION.md` — line-by-line swap from Unity Ads SDK and AdMob Unity
  plugin.

### Installation

Add to `Packages/manifest.json`:

```json
"com.levelmoment.sdk": "https://github.com/levelmomentorg/sdk.git?path=sdk/unity#v0.1.0"
```

A WebView plugin (3D WebView, UniWebView, or gree/unity-webview) is
required as a runtime peer dependency.

### Migration

See [MIGRATION.md](./MIGRATION.md) for the Unity Ads / AdMob Unity swap.
