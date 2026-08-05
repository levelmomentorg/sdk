# Migrating from google_mobile_ads to LevelMoment

This guide shows a line-by-line swap. In most apps this is a 15-minute change.

## 1. Replace the dependency

```yaml
# pubspec.yaml

# REMOVE:
dependencies:
  google_mobile_ads: ^5.0.0

# ADD:
dependencies:
  levelmoment_ads: ^1.0.0
```

## 2. Replace the import

```dart
// REMOVE:
import 'package:google_mobile_ads/google_mobile_ads.dart';

// ADD:
import 'package:levelmoment_ads/levelmoment_ads.dart';
```

## 3. Replace initialization

```dart
// BEFORE:
await MobileAds.instance.initialize();

// AFTER:
await LevelMomentAds.instance.initialize(
  apiUrl: 'https://api.levelmoment.com',         // provided by LevelMoment
  breakUrl: 'https://app.levelmoment.com/break', // hosted break page (required)
);
```

> **Breaking change (WebView refactor):** `initialize()` now requires
> `breakUrl` in addition to `apiUrl`. The SDK is a thin WebView shell over the
> hosted `/break` page (`platform/web/app/break`), which renders all question
> UI, submits answers, and owns the impression queue — there is no longer any
> Dart question rendering. The internal data model types (`Question`,
> `QuestionMeta` and its subtypes, `SessionQuestion`, `ConceptLesson`,
> `LessonPage`, `SessionSummary`) were removed; only the error / reward /
> callback types remain. Pass `mock: true` to render bundled mock questions.

## 4. Replace ad loading

```dart
// BEFORE:
RewardedAd.load(
  adUnitId: 'ca-app-pub-xxx/yyy',
  request: const AdRequest(),
  rewardedAdLoadCallback: RewardedAdLoadCallback(
    onAdLoaded: (RewardedAd ad) {
      _rewardedAd = ad;
    },
    onAdFailedToLoad: (LoadAdError error) {
      print('Failed: $error');
    },
  ),
);

// AFTER (identical structure — swap class names, add studentToken):
LevelMomentRewardedAd.load(
  placementId: 'your-game-id',       // from LevelMoment developer portal (replaces adUnitId)
  studentToken: getTokenFromUrl(),   // from ?token= URL param (new — one extra line)
  adLoadCallback: LevelMomentAdLoadCallback(
    onAdLoaded: (LevelMomentRewardedAd ad) {
      _rewardedAd = ad;
    },
    onAdFailedToLoad: (LevelMomentAdError error) {
      print('Failed: $error');
    },
  ),
);
```

## 5. Replace show + callbacks

```dart
// BEFORE:
_rewardedAd?.fullScreenContentCallback = FullScreenContentCallback(
  onAdShowedFullScreenContent: (ad) {},
  onAdFailedToShowFullScreenContent: (ad, error) {},
  onAdDismissedFullScreenContent: (ad) {
    ad.dispose();
    resumeGame();       // ← your game resume logic
    _loadNextAd();      // preload the next ad
  },
);
_rewardedAd?.show(
  onUserEarnedReward: (AdWithoutView ad, RewardItem reward) {
    grantBonus();       // ← your bonus logic
  },
);

// AFTER (identical structure — swap class names):
_rewardedAd?.fullScreenContentCallback = LevelMomentFullScreenContentCallback(
  onAdShowedFullScreenContent: (ad) {},
  onAdFailedToShowFullScreenContent: (ad, error) {},
  onAdDismissedFullScreenContent: (ad) {
    ad.dispose();
    resumeGame();       // ← unchanged
    _loadNextAd();      // unchanged
  },
);
_rewardedAd?.show(
  onUserEarnedReward: (LevelMomentRewardedAd ad, LevelMomentRewardItem reward) {
    grantBonus();       // ← unchanged
    // reward.amount == 1 → correct answer
    // reward.amount == 0 → skipped / wrong answer
  },
);
```

> **Resume on the error path too.** Just like AdMob's `RewardedAd`, a break
> that fires an `error` invokes `onAdFailedToShowFullScreenContent`, not
> `onAdDismissedFullScreenContent`. If you only resume gameplay in
> `onAdDismissedFullScreenContent`, the game hangs when the page fails to load
> or show. Resume (and preload the next ad) in
> `onAdFailedToShowFullScreenContent` as well, exactly as the AdMob contract
> requires.

## 6. Rendering — nothing to do

Unlike earlier SDK versions, you do **not** render the question. `show()`
pushes a fullscreen WebView pointing at the hosted `/break` page, which renders
all 8 question types, the session flow, and lessons, submits answers, and
records impressions itself. Your game only handles `onUserEarnedReward`
(`reward.amount == 1` → correct) and `onAdDismissedFullScreenContent` (resume
the game). This matches AdMob's "the SDK renders, you get callbacks" model
exactly — no question-rendering code in your app.

## Getting a student token

The `studentToken` comes from the LevelMoment parent portal. The parent launches the
game with a URL like `yourgame://play?token=<session-token>`. Read it from your
deep link or initial route parameters.

```dart
String getTokenFromUrl() {
  // Example with go_router:
  return GoRouterState.of(context).uri.queryParameters['token'] ?? '';
}
```

## Complete example

See `example/lib/main.dart` for a full working integration.
