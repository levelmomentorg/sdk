# Changelog

All notable changes to `levelmoment_ads` are documented here. Format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/); versioning
follows [Semver](https://semver.org).

## [0.4.0] — 2026-10-25

### Added

- `LevelMomentConnectionGate`: wrap the app in it from `MaterialApp.builder` to block play with a full-screen "No internet connection" message, with Try again, while the operating system reports no connection. `gameName` names the game in it; `onConnectionChange(online)` lets the game pause.
- `LevelMomentConnection.instance.isOnline` and its `changes` stream.
- Dependency on `connectivity_plus` to read the device's connection state. Rebuild the native app after upgrading.

### Changed

- While the operating system reports no connection, `show()` calls `onAdFailedToShowFullScreenContent` with `network_error`, `ensureSignedIn()` and `ensureAccess()` return `technicalFailure`, `isSignedIn()` and `checkAccess()` throw `LevelMomentSignInCheckError` with code `offline`, and `signOut()` throws after clearing the secure store, all without mounting a WebView.
- The close button over a loading page sits inside the safe area.

## [0.3.2] — 2026-10-20

No changes. Released with the Unity package so every SDK shares one version.

## [0.3.1] — 2026-10-17

### Changed — BREAKING

- `initialize()` throws in a release build when `unsafeTesting` carries a
  sandbox token, and a per-call `studentToken` is refused there too. A release
  build that kept the token would serve every player unpaid sandbox breaks
  with no pairing. Debug and profile builds are unchanged. Gate the token on
  `kReleaseMode`, as the testing guide shows.

### Fixed

- A break no longer shows the native close button (×), and the Android back
  button does nothing on it, once the break page confirms it ends the break
  itself. The break URL carries `leave=page`, and the page's `ready` answers
  with `leaveSupported`; until then, and on a page that never answers, the
  button and back stay. A player can't close a break partway: it ends in a
  pass, a fail, or a load failure, and a mid-break pairing card offers
  **Not now**. The sign-in gate keeps its close button, which is a parent's
  "not now".
- When the break page reloads itself after pairing, the load watchdog is
  re-armed, so a reload that fails or hangs no longer leaves a blank screen.
  If the web view's content process stops, the break ends through
  `onAdFailedToShowFullScreenContent`.
- Error messages for a refused `studentToken` or `unsafeTesting.token` no
  longer include the token.

## [0.3.0] — 2026-10-16

### Added

- `LevelMomentRewardedAd.load()` takes a slot declaration: `adType`,
  `targetDurationSeconds` and an optional `rewardAmount`, plus the reporting
  fields `slotType` and `dimensions`, which the studio portal registers first.
- `UnsafeTesting.realPairing` runs pairing, a learner session and the
  learner's own topics against a hosted page on
  `http://localhost:<port>` or `http://127.0.0.1:<port>`. Debug builds only;
  `initialize()` throws in release and profile builds, for any other URL, and
  for a token, an `apiUrl`, a top-level `breakUrl` or `mock`. A per-call
  `studentToken` is refused at the call. No `apiUrl` or `sandbox` flag is sent.
- `LevelMomentAds.instance.mode` reports the resolved `LevelMomentHostedMode`:
  `production`, `sandbox` or `realPairing`.
- The credential reply carries `platform` (`ios` or `android`, from
  `defaultTargetPlatform`) and `storefront: null`, so Level Moment can apply
  its store rules. Other targets send no platform. Reading the App Store
  storefront comes in a later release.

### Changed — BREAKING

- `onUserEarnedReward` fires at most once per break, when a passed break ends.
  `LevelMomentRewardItem` carries `rewardId` and `earnedAt`; there is no
  per-answer amount. Only `quick_question` and `practice_set` report one;
  `mastery_round` and `intro_lesson` never do.
- The `format` argument takes `quick_question` (the default),
  `practice_set`, `mastery_round`, or `intro_lesson`.
- `credentialKey(origin, placementId)` and the token store's `get`, `set` and
  `clear` take the hosted page origin first. The production origin keeps the
  key `com.levelmoment.credential.<placementId>`, so stored credentials need no
  migration. Any other origin uses
  `com.levelmoment.test-credential.<encodedOrigin>.<placementId>`.
- `LevelMomentAds.instance.apiUrl` is a `String?`: it is null under
  `realPairing`.

### Changed

- A store-policy refusal from the hosted page reaches
  `onAdFailedToShowFullScreenContent` as `no_fill`, as it does in the web and
  React Native SDKs, never as a credential failure.

## [0.2.0] — 2026-09-05

**Preview:** Validate the WebView provider and hosted break on each target
device before release.

### Removed — BREAKING

- `subscription_required` is gone from the ad error codes, along with every
  other entitlement code. A household's subscription state is Level Moment's,
  not the publisher's: a game that could read it could show its own upsell. A
  break refused for billing reasons now surfaces the same way any other refusal
  does.

### Added

- The break and gate URLs now carry `caps=openExternal`, telling the hosted page
  this shell can open a link in the system browser. The page offers the parent
  an "Approve in your browser" button only where something will happen; a page
  loaded by an older shell shows the QR code and the typed code alone. Upgrading
  is what turns that button back on.
- `OpenExternal` is handled: the shell launches the approval URL in the system
  browser, and only when it belongs to the origin of the page the WebView
  loaded.
- Platform secure-store credential storage. The SDK keeps a per-placement
  second copy of a paired device's
  credential (Keychain on iOS, EncryptedSharedPreferences on Android), so the
  credential survives the WebView's site data being cleared. Each eviction
  previously cost a household a re-pair with a parent's phone involved.
- The credential bridge answers the hosted page's `needCredential` message and
  keeps the secure store in step: it stores what pairing issues
  and clears what the server refuses.
- `LevelMomentAds.instance.signOut(context:, placementId:)` — clears the
  secure-store copy, then clears the hosted origin's copy. Clearing only the
  secure store would let the next `ensureSignedIn()` sign the previous learner
  back in from the hosted copy. Not atomic: a hosted clear that cannot run
  throws `LevelMomentSignInCheckError` with the app's own credential already
  gone, so retry when there is a network.
- Secure-store availability probe. The store is probed on first use; when it
  will not register, the SDK tells the page it is not keeping custody and the
  hosted origin keeps ownership of the credential.
- `LevelMomentWebView` — fullscreen `WebView` host with a `ReactNativeWebView`
  JavaScript channel bridging `ready` / `earnedReward` / `dismissed` / `error`,
  with terminal-once dismiss semantics.
- `test/rewarded_ad_test.dart` — Dart unit tests for URL building, the
  `not_loaded` guard, `HostMessage` parsing, and terminal-once.

### Changed

- **WebView shell.** The SDK opens the hosted `/break` page. The page renders the
  question types, the session flow, lessons, answer submission, AND the
  impression queue. `LevelMomentRewardedAd.show()` now pushes a fullscreen
  `LevelMomentWebView` route instead of the Dart `LevelMomentBreakScreen` renderer.
- `LevelMomentRewardedAd.load()` is now a synchronous mark-ready (no native
  question fetch); the fetch happens inside the WebView on `show()`.
- The device credential no longer rides on the hosted `/break` URL as
  `?token=`. `LevelMomentRewardedAd.load` / `show` and
  `ensureSignedIn` / `isSignedIn` hand a credential over the bridge instead, so
  it never reaches a launch URL, a crash report, or a web server log.
- `studentToken` is now optional everywhere. A paired device needs none; local
  URLs and `eply_sbx_` credentials require `unsafeTesting`.
- The page announces a minted credential (`credentialIssued`) only for a token
  its own pairing flow just minted during that page load. A credential the
  secure store supplied is used for that run and is not echoed back, so the
  bridge cannot be used to read a credential out of the hosted origin.

### Deprecated

- Caller supplied URLs and production `studentToken` values are deprecated.
  Use the canonical defaults and `unsafeTesting` for sandbox work.

### Breaking

- `LevelMomentAds.instance.initialize()` now uses canonical endpoints by
  default. Added `unsafeTesting` for local URLs and sandbox credentials.
- Removed former data-model types (`Question`, `QuestionMeta` + subtypes,
  `SessionQuestion`, `ConceptLesson`, `LessonPage`, `SessionSummary`) and the
  `question` / `lesson` / `sessionSummary` getters and
  `notifyDismissed` / `advanceSession` methods — the hosted page owns all of
  this now. The error / reward / callback public surface is unchanged.
- Removed the former in-app question renderer; the hosted break page now owns
  question content and session flow.

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
  progression + mastery-round lessons.
- `SharedPreferences`-backed impression queue with persistence across app
  restarts.

### Migration

See [MIGRATION.md](./MIGRATION.md) for a line-by-line swap from
`google_mobile_ads`.
