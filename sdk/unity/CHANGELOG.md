# Changelog

All notable changes to `com.levelmoment.sdk` (Unity UPM package) are documented
here. Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/);
versioning follows [Semver](https://semver.org).

## [0.4.2] — 2026-10-26

### Fixed

- `InterstitialAd.Load()` with no `format` and no `slot` now declares an
  interstitial slot of 18 seconds and asks for `mastery_round`. Before, it
  sent `format=quick_question` with no slot, and the hosted page served a
  rewarded single question. A call that passes a `format` without a slot is
  unchanged.

### Added

- `Load()`, `EnsureAccess`, `EnsureSignedIn`, `CheckAccess` and `IsSignedIn`
  log a warning, once per ID, when the placement ID doesn't look like one
  from the developer portal (`pl_` followed by 22 letters, digits, `-` or `_`). The call still runs.

## [0.4.1] — 2026-10-26

### Added

- `LevelMomentConfig.RequireInternet` blocks play with a full-screen "No
  internet connection" message, with Try again, while
  `Application.internetReachability` reports no connection. `GameName` names
  the game in it, and `OnConnectionChange(online)` runs on the main thread so
  the game can pause. The SDK reads reachability every 2 seconds and when the
  app regains focus or resumes. Off by default.
- `LevelMomentAds.IsOnline` and `LevelMomentAds.Dispose()`. Call `Dispose()`
  when the game tears down.
- `ILevelMomentRevealableWebView`: a provider that implements it loads a
  visible surface hidden and shows it when the page posts `ready`. The gree
  adapter and the macOS view implement it.

### Changed

- While the device reports no connection, `Show()` calls `OnAdFailedToShow`
  with `network_error`, `EnsureSignedIn` and `EnsureAccess` return
  `TechnicalFailure`, and `IsSignedIn` and `CheckAccess` call `onError`, all
  without opening a WebView. Before, each waited for the 15-second load
  watchdog.
- While a hosted page loads, the SDK shows a spinner and a close button inside
  the safe area. Closing ends a break with `OnAdDismissed` and makes
  `EnsureSignedIn` and `EnsureAccess` return `Canceled`.

## [0.4.0] — 2026-10-25

No changes. Released with the web and Flutter packages so every SDK shares one
version. Require-internet mode, new in those packages, isn't in Unity yet.

## [0.3.2] — 2026-10-20

### Added

- The package depends on `com.unity.inputsystem` and
  `com.unity.modules.unitywebrequest`, which gree's `unity-webview` assembly
  needs to compile.
- The gree adapter compiles whenever the `net.gree.unity-webview` package is
  present; the `LEVELMOMENT_GREE_WEBVIEW` define is needed only for a classic
  gree install.
- The editor hands UnityLinker a `link.xml` for the SDK and gree assemblies on
  every build, so a game needs no `Assets/link.xml`.
- The editor sets Active Input Handling from **Input Manager (Old)** to
  **Both**, so legacy `Input` calls keep working beside the Input System
  package.
- In the editor and development builds, `LevelMomentAds.Initialize` uses the
  sandbox token in `UserSettings/LevelMomentSandboxToken.txt` when the config
  sets no `UnsafeTesting`. The editor copies it into development builds for
  the length of the build; release builds never include it.
  `LevelMomentConfig.UseSandboxTokenFile = false` turns this off.

### Changed — BREAKING

- A release build fails while a `LevelMomentSandboxToken` file sits under
  `Assets/**/Resources`, the 0.3.1 layout. Move it to
  `UserSettings/LevelMomentSandboxToken.txt` and delete the game's own token
  loading code and build guard.

## [0.3.1] — 2026-10-17

### Changed — BREAKING

- `LevelMomentAds.Initialize()` throws in a release player when
  `UnsafeTesting.Token` is set, and the `studentToken` load overloads refuse a
  token there too. A release build that kept the token would serve every player
  unpaid sandbox breaks with no pairing. The editor and development builds are
  unchanged.

### Changed

- The break URL carries `leave=page`, so a mid-break pairing card offers
  **Not now**, which ends the break with no reward.

## [0.3.0] — 2026-10-16

### Added

- `UnsafeTesting.RealPairing` runs the real pairing path against a hosted page
  on `http://localhost:<port>` or `http://127.0.0.1:<port>`, in the editor and
  development builds only. Any other build throws at `Initialize`. Under it the
  break and gate URLs carry no `sandbox` and no `apiUrl`, every token input is
  refused, and the credential reply is bound to the local origin. See
  "Test real pairing against a local stack" in the README.

- The credential-bridge reply to the hosted page's `needCredential` message now
  carries `platform` (`"ios"` / `"android"`, from the running OS; null on
  macOS and every other standalone target, and in the Editor) and
  `storefront` (the iOS StoreKit storefront's alpha-3 country code, such as
  `USA`, or null when unavailable; always null on
  Android). Read once and cached at `LevelMomentAds.Initialize()`, never
  inside the reply itself. The server uses these to decide store policy.
- A native iOS plugin (`Runtime/Plugins/iOS/LevelMomentStorefront.m`) reads
  `SKPaymentQueue.defaultQueue.storefront.countryCode`. An Editor
  post-process build step links `StoreKit.framework` into the generated Xcode
  project. Android sends no storefront and adds no Play Billing dependency,
  by design — every Google Play row resolves the same store policy either
  way.

- `LevelMomentAdSlot` — the slot a game declares for an ad placement: the ad
  type it replaces, a target duration in seconds, and the reward amount the
  game grants the player for it. `RewardedAd.Load` and `InterstitialAd.Load`
  take one, and it rides on the hosted `/break` URL, where the serve fits
  questions to the duration.
- `LevelMomentMaxSdk.MapAdUnit` takes the slot's target duration and reward
  amount alongside the placement and format. `OnAdReceivedRewardEvent` then
  reports the declared amount: the amount the ad unit already granted. MAX
  reads that from its dashboard, and there is no dashboard here. An ad unit
  mapped without one keeps reporting 1 for a passed break.

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

- A native macOS WebView for standalone players
  (`Runtime/Plugins/macOS/LevelMomentWebView.bundle`, source and build script
  in `Native~/macOS`). It replaces gree in a macOS player automatically, with no
  scripting define. The break is a real web view inside the player window, so
  VoiceOver, Voice Control, Switch Control, the keyboard, and UI automation
  reach its questions and answers. Under gree, the page sat in a hidden
  off-screen window and the game window showed a texture of it, so
  position-based assistive technology and pointer automation missed it, and
  typed answers were dropped when Active Input Handling was New or Both.
  Editor play mode on a Mac still uses gree, so typed answers may not register
  there.

### Changed — BREAKING

- `OnUserEarnedReward` is an `Action<LevelMomentRewardItem>` that fires at
  most once per break, when a passed break ends. The item carries `RewardId`
  and `EarnedAt`; the per-answer `int` amount is gone. Only `quick_question`
  and `practice_set` report one; `mastery_round` and `intro_lesson` never do.
- Break formats are `quick_question`, `practice_set`, `mastery_round`, and
  `intro_lesson`, and the default is `quick_question`. Replace `flashcard`,
  `quiz`, and `deep_dive`; the service still accepts them so shipped builds keep working, but that alias is temporary.

### Fixed

- A shown break no longer reports `IsLoaded` or reopens; get a fresh handle
  from `Load()` per break; a second `Show()` reports `not_loaded`.
- `OnAdShowedFullScreenContent` (MAX facade: `OnAdDisplayedEvent`) fires once
  per `Show()`. It fired twice when the page fell back to the pairing card,
  which posts `ready` a second time.

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
