# Changelog

All notable changes to `com.levelmoment.sdk` (Unity UPM package) are documented
here. Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/);
versioning follows [Semver](https://semver.org).

## [Unreleased]

### Added

- `LevelMoment.Compat.Max` — an AppLovin MAX-shaped compatibility facade
  (`Runtime/Compat/`) over the existing `InterstitialAd`/`RewardedAd`: a game
  whose code calls `MaxSdk.LoadInterstitial(id)` /
  `MaxSdk.ShowInterstitial(id)` and subscribes to
  `MaxSdkCallbacks.Interstitial.OnAdLoadedEvent` switches to Level Moment by
  changing the type prefixes and ad-unit ids, keeping its callback bodies.
  `LevelMomentMaxSdk`/`LevelMomentMaxSdkCallbacks` have zero dependency on the
  AppLovin plugin; three shape-compatible stand-in types (`AdInfo`,
  `ErrorInfo`, `Reward`) live in `MaxCompatTypes.cs`. Facade callbacks are
  delivered on a later frame, in order, as MAX's are, so calling back into
  `LoadX`/`ShowX` from a callback is safe. See `sdk/unity/README.md`
  → "AppLovin MAX compatibility facade".
- `InterstitialAd` / `InterstitialAdLoadCallbacks` / `InterstitialAdShowCallbacks` —
  a non-rewarded break placement with the same `Load()`/`Show()` lifecycle as
  `RewardedAd`, for a slot that grants nothing (interstitial is the dominant
  casual-game ad placement this SDK otherwise has no equivalent for).
  `InterstitialAdShowCallbacks` carries the dismissed/failed/showed callbacks
  only — no reward. The shell opens the hosted `/break` page with
  `kind=interstitial` on the URL (`BreakUrl.Build` gained an optional `kind`
  parameter; `RewardedAd` still passes none, so its URL is unchanged). The web
  `/break` page does not yet read this param — see README.md → InterstitialAd.
- The WebView lifecycle, watchdog, and terminal-once dismiss/error/close
  collapse that `RewardedAd` and `InterstitialAd` share now live in one place
  (`BreakSurfaceCore`, internal), so the two placements cannot drift apart.
  `RewardedAd`'s method/property signatures are unchanged; one-show-per-handle
  is now enforced (see Fixed, below) where it previously was not.

### Fixed

- A shown break no longer reports `IsLoaded` or reopens; get a fresh handle
  from `Load()` per break; a second `Show()` reports `not_loaded`.

## [0.2.0] — 2026-09-05

**Preview:** Validate the WebView provider and hosted break on each target
device before release.

### Removed — BREAKING

- `subscription_required` is gone from the ad error codes, along with every
  other entitlement code. A household's subscription state is Level Moment's,
  not the publisher's: a game that could read it could show its own upsell. A
  break refused for billing reasons now surfaces the same way any other refusal
  does.

### Added — the shell says what it can do

- `BreakUrl.Build` and `BreakUrl.BuildGate` append `caps=openExternal`, telling
  the hosted page this shell can open a link in the system browser. The page
  offers the parent an "Approve in your browser" button only where something
  will happen; a page loaded by an older shell shows the QR code and the typed
  code alone. Upgrading is what turns that button back on.
- `openExternal` is handled by `ExternalBrowser.Open`, which launches the
  approval URL only when it belongs to the origin of the page the WebView
  loaded.

### Changed — BREAKING: hosted WebView shell

The SDK opens the hosted `/break` page in a WebView and bridges its events.

- **New public API** (mirrors AdMob / Unity Ads rewarded ads):
  - `LevelMomentAds.Initialize(LevelMomentConfig)` — production uses canonical
    endpoints; `Mock` previews offline content and `UnsafeTesting` enables
    sandbox URLs and credentials.
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
- The page sends messages to Unity through the configured WebView bridge.

### Removed — BREAKING

- The in-game renderer path: `LevelMomentAds.Show()` returning a `Question`,
  `NotifyAnswer()`, `GetLoadedQuestion()`, `IsReady()`, the `Question` /
  `QuestionMeta` models, and the `ILevelMomentLoadListener` /
  `ILevelMomentShowListener` interfaces. The hosted page owns questions now.
- `ImpressionQueue` (PlayerPrefs buffer) and the `/questions` / `/answers` /
  `/impressions` HTTP calls — the hosted page owns fetching, answers, and the
  impression queue.
- `UrlSafety` and its credential-shaped-query-key denylist. URL construction
  moved to the pure, test-covered `BreakUrl.Build`, which never places a
  credential on the query string — see "Credential bridge" below.

### Added — credential bridge (no more `?token=` on the hosted URL)

- The credential bridge answers the hosted page's `needCredential` message by
  running script in the page,
  instead of the credential riding on the `/break` URL. `BreakUrl.Build` /
  `BreakUrl.BuildGate` never append a `token` param.
- `studentToken` is now optional everywhere. A paired device needs none: the
  credential lives in the hosted page's own storage, and `RewardedAd.Load` /
  `LevelMomentAds.EnsureSignedIn` / `IsSignedIn` answer with an empty token
  unless the game passes one explicitly (a sandbox token, for integration
  testing).
- **Nothing is read from the game's launch URL.** Let the device pair through
  the hosted flow, or use `UnsafeTesting.Token` for sandbox work.
- **Secure storage is not implemented in this SDK.** React Native and Flutter
  keep a second copy of the credential in the device keychain or secure store,
  so it survives the WebView's site data being cleared. Unity has no such store
  without a native plugin, and this SDK does not ship one — it never asks for
  custody of a minted credential, so the credential keeps living in the hosted
  page's own storage, exactly as before. Pending native plugins.
- An explicit token reaches the page only through a scriptable WebView provider.

### Deprecated

- Caller supplied URLs and production `studentToken` values are deprecated.
  Use canonical defaults and `UnsafeTesting` for sandbox work.

### Notes

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
