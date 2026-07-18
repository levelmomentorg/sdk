# Changelog

All notable changes to `com.levelmoment.sdk` (Unity UPM package) are documented
here. Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/);
versioning follows [Semver](https://semver.org).

## [Unreleased]

### Changed — BREAKING: rewritten as an ADR-001 WebView shell

The SDK no longer renders questions in-game. Per
[ADR-001](../../docs/ADR-001-webview-rendering.md) it is now a thin shell that
opens the hosted `/break` page in a WebView and bridges its events — the same
architecture as `sdk/web`, `sdk/react-native`, and `sdk/flutter`. This is a
full API replacement.

- **New public API** (mirrors AdMob / Unity Ads rewarded ads):
  - `LevelMomentAds.Initialize(LevelMomentConfig)` — config is `ApiUrl`,
    `BreakUrl`, `Mock` (no per-request state; no MonoBehaviour singleton).
  - `RewardedAd.Load(placementId, callbacks)` — synchronous mark-ready, no
    network. The hosted page fetches when `Show()` opens it.
  - `ad.Show(callbacks)` — opens the hosted `/break` page in a WebView and maps
    its bridge events to `OnUserEarnedReward(amount)` / `OnAdDismissed` /
    `OnAdFailedToShow`. `dismissed`/`error` collapse to a single terminal
    dismiss; a 15s pre-`ready` watchdog (mirroring `sdk/web`) prevents a crashed
    page from covering the game.
- **WebView provider required.** Unity has no built-in WebView. New
  `ILevelMomentWebView` seam + `LevelMomentWebViewRegistry`. Bundled
  `GreeWebViewAdapter` for [`gree/unity-webview`](https://github.com/gree/unity-webview),
  compiled only when the `LEVELMOMENT_GREE_WEBVIEW` scripting define is set
  (its own `LevelMomentSDK.Gree` assembly, gated by `defineConstraints`).
  Absent a provider, `NoWebViewFallback` fails `Show()` with install guidance.
- The page now sends messages to Unity via gree's `Unity.call(json)` bridge; the
  hosted page's postMessage shim (`platform/web/app/break/postToHost.ts`) was
  extended to cover `window.Unity.call`.

### Removed — BREAKING

- The in-game renderer path: `LevelMomentAds.Show()` returning a `Question`,
  `NotifyAnswer()`, `GetLoadedQuestion()`, `IsReady()`, the `Question` /
  `QuestionMeta` models, and the `ILevelMomentLoadListener` /
  `ILevelMomentShowListener` interfaces. The hosted page owns questions now.
- `ImpressionQueue` (PlayerPrefs buffer) and the `/questions` / `/answers` /
  `/impressions` HTTP calls — the hosted page owns fetching, answers, and the
  impression queue.
- `UrlSafety` and its credential-shaped-query-key denylist. It forbade a
  `token` query key, which is incompatible with the ADR-001 contract: the
  student token now legitimately rides in the WebView URL (`?token=`), exactly
  as the web and Flutter shells do. URL construction moved to the pure,
  test-covered `BreakUrl.Build`.

### Notes

- Version intentionally left unchanged here; the release process bumps it.
- Not yet compiled by the Unity toolchain (no Unity in JS/TS CI). EditMode tests
  (`BreakUrl`, `HostMessage`, `LoadWatchdog`, `RewardedAd`) are written; run them
  plus an on-device smoke before tagging. See README → Verification.

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
