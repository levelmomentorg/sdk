# Changelog

All notable changes to `levelmoment_ads` are documented here. Format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/); versioning
follows [Semver](https://semver.org).

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
  progression + deep-dive lessons.
- `SharedPreferences`-backed impression queue with persistence across app
  restarts.

### Migration

See [MIGRATION.md](./MIGRATION.md) for a line-by-line swap from
`google_mobile_ads`.
