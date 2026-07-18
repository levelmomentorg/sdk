# levelmoment_ads (Flutter)

**Status: 🟢 Verified end to end on the iOS Simulator — real question shown in the WebView route, answered, reward bridged. `flutter analyze` + `flutter test` clean.**

Flutter SDK for iOS and Android apps. Drop-in replacement for the `google_mobile_ads` rewarded ad format. Published to `pub.dev` when stable.

See [`MIGRATION.md`](MIGRATION.md) for a line-by-line swap guide and [`docs/ADR-001-webview-rendering.md`](../../docs/ADR-001-webview-rendering.md) for the architecture rationale.

---

## What's Done

- **`LevelMomentAds.instance.initialize(apiUrl:, breakUrl:)`** — mirrors `MobileAds.instance.initialize()`; `breakUrl` points the WebView at the hosted `/break` page (optional `mock:` swaps in bundled mock questions)
- **`LevelMomentRewardedAd.load(...)`** — static factory mirrors `RewardedAd.load(adUnitId, request, callback)`; synchronous mark-ready (no native preload), supports `format` (`'flashcard'`, `'quiz'`, `'deep_dive'`) and an optional SSV-parity `customData` (stamped onto every impression the hosted page records)
- **`LevelMomentAdLoadCallback`** — mirrors `RewardedAdLoadCallback` (`onAdLoaded`, `onAdFailedToLoad`)
- **`LevelMomentFullScreenContentCallback`** — mirrors `FullScreenContentCallback` (`onAdShowedFullScreenContent`, `onAdFailedToShowFullScreenContent`, `onAdDismissedFullScreenContent`)
- **`LevelMomentRewardItem`** — mirrors `RewardItem` (`type`, `amount`)
- **`ad.show(context:, onUserEarnedReward:)`** — pushes `LevelMomentWebView` as a fullscreen route; the hosted page renders all question types and session flows
- **`LevelMomentWebView`** (`lib/src/widgets/level_moment_web_view.dart`) — fullscreen `WebView` pointing at the hosted `/break` page, with a `ReactNativeWebView` JavaScript channel bridging `ready` / `earnedReward` / `dismissed` / `error` back to the ad
- **postMessage bridge** — terminal-once dismiss semantics (rewards may repeat; `dismissed`/`error` fire dismissal exactly once). `onUserEarnedReward` fires once per graded answer (`amount` 1 correct / 0 otherwise) — accumulate and apply the reward in `onAdDismissedFullScreenContent`. The dispose-path terminal is delivered on a microtask: the widget tree is locked during route unmount, and a consumer calling `setState` from its dismiss callback would otherwise throw and lose the event.
- **Pre-`ready` watchdog** — 15 seconds, mirroring `sdk/web`, `sdk/react-native`, and `sdk/unity`. An unreachable or crashed break page resolves as a clean dismissal; a main-frame load failure fires `onAdFailedToShowFullScreenContent` with code `network_error`. After `ready` there is no timeout.
- **Dart unit tests** — `test/rewarded_ad_test.dart` covers URL building, the `not_loaded` guard, `HostMessage` parsing, and terminal-once
- **`MIGRATION.md`** — complete line-by-line swap guide

## Architecture

This SDK does **not** render questions. All UI for the 8 question types, the session flow, and the impression queue lives in [`platform/web/app/break`](../../platform/web/app/break). The SDK is a thin shell that:

1. Builds a URL with placement / token / format query params (or `mock=true`)
2. Pushes a fullscreen route hosting a WebView pointing at that URL
3. Listens for `window.ReactNativeWebView.postMessage` events from the page (same bridge name the React Native SDK uses, so the hosted page is unmodified)
4. Translates them to the existing `LevelMomentFullScreenContentCallback` / `onUserEarnedReward` callbacks

## What's Not Done

- [ ] No example app in `sdk/flutter/example/`
- [ ] `pubspec.yaml` version is `0.1.0` — not yet published to pub.dev
- [ ] No native pre-warm — `load()` resolves immediately; `show()` triggers the WebView fetch, adding a small visible delay at the pause point

---

## Setup

```bash
# Requires Flutter SDK installed (https://flutter.dev/docs/get-started/install)
cd sdk/flutter
flutter pub get
flutter analyze
```

---

## Usage

```dart
import 'package:levelmoment_ads/levelmoment_ads.dart';

// 1. Initialize once at app start (in main() or initState)
await LevelMomentAds.instance.initialize(
  apiUrl: 'https://api.levelmoment.com',
  breakUrl: 'https://app.levelmoment.com/break',
);

// 2. Preload while the game runs
LevelMomentRewardedAd? _rewardedAd;

LevelMomentRewardedAd.load(
  placementId: 'your-game-id',
  studentToken: getTokenFromUrl(),
  adLoadCallback: LevelMomentAdLoadCallback(
    onAdLoaded: (ad) {
      _rewardedAd = ad;
      _rewardedAd!.fullScreenContentCallback = LevelMomentFullScreenContentCallback(
        onAdDismissedFullScreenContent: (ad) {
          ad.dispose();
          resumeGame();
          _loadNextAd(); // preload for the next break
        },
      );
    },
    onAdFailedToLoad: (err) => print('Failed: $err'),
  ),
);

// 3. At the natural pause point — SDK renders everything
_rewardedAd?.show(
  context: context,
  onUserEarnedReward: (ad, reward) => reward.amount == 1 && grantBonus(),
);
```

That's it. The SDK pushes a fullscreen route hosting a WebView that loads the hosted `/break` page, which renders all question types and session flows, and calls `onAdDismissedFullScreenContent` when the student finishes.

---

## Key Files

| File                                         | Purpose                                                        |
| -------------------------------------------- | -------------------------------------------------------------- |
| `lib/levelmoment_ads.dart`                   | Library entry point + exports                                  |
| `lib/src/levelmoment_ads.dart`               | `LevelMomentAds` singleton (mirrors `MobileAds`)               |
| `lib/src/rewarded_ad.dart`                   | `LevelMomentRewardedAd` — main ad class; show() pushes WebView |
| `lib/src/models.dart`                        | Public types: errors, rewards, callbacks                       |
| `lib/src/widgets/level_moment_web_view.dart` | Fullscreen WebView shell + `HostMessage` bridge                |
| `test/rewarded_ad_test.dart`                 | Dart unit tests (URL build, guard, message parsing)            |
| `pubspec.yaml`                               | Package manifest                                               |
| `MIGRATION.md`                               | Swap guide from `google_mobile_ads`                            |
