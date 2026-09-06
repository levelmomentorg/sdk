# Migrate a Flutter rewarded placement

## Prerequisites

- Flutter 3.22 or later
- The immutable 0.2 preview commit supplied to you

The Flutter package is not published on pub.dev. Pin the supplied commit until
a release is available.

## 1. Replace the dependency

Replace `google_mobile_ads` with the pinned preview:

```yaml
dependencies:
  levelmoment_ads:
    git:
      url: https://github.com/levelmomentorg/sdk.git
      ref: PROVIDED_PREVIEW_COMMIT
      path: sdk/flutter
```

Do not use a moving branch such as `main`.

## 2. Replace the import and initialization

```dart
import 'package:levelmoment_ads/levelmoment_ads.dart';

await LevelMomentAds.instance.initialize();
```

Production endpoints and player credentials are managed by Level Moment. Do
not pass `apiUrl`, `breakUrl`, or `studentToken`.

## 3. Add the access flow

Use `checkAccess()` for an invisible check and `ensureAccess()` after the
player chooses learning:

```dart
try {
  final ready = await LevelMomentAds.instance.checkAccess(
    context: context,
    placementId: 'YOUR_PLACEMENT_ID',
  );
  if (!ready) {
    final result = await LevelMomentAds.instance.ensureAccess(
      context: context,
      placementId: 'YOUR_PLACEMENT_ID',
    );
    if (result != EnsureSignedInResult.ready) return;
  }
  openLearningMode();
} on LevelMomentSignInCheckError {
  showTryAgainLater();
}
```

Use `ensureSignedIn()` or `isSignedIn()` only for identity-only features.

## 4. Replace rewarded loading

Keep the existing load, show, reward, and full-screen callbacks:

```dart
var rewardGranted = false;
LevelMomentRewardedAd.load(
  placementId: 'YOUR_PLACEMENT_ID',
  adLoadCallback: LevelMomentAdLoadCallback(
    onAdLoaded: (ad) {
      ad.fullScreenContentCallback = LevelMomentFullScreenContentCallback(
        onAdDismissedFullScreenContent: (_) {
          resumeGame();
        },
        onAdFailedToShowFullScreenContent: (_, error) => resumeGame(),
      );
      ad.show(
        context: context,
        onUserEarnedReward: (_, item) {
          if (item.amount == 1 && !rewardGranted) {
            rewardGranted = true;
            grantBonus();
          }
        },
      );
    },
    onAdFailedToLoad: (_) => resumeGame(),
  ),
);
```

A multi-question break can emit several reward callbacks. Guard the game
reward so the first correct answer grants it once. Dismissal and failure only
resume the game.

## 5. Configure sandbox testing

Put local endpoints and the sandbox credential inside `UnsafeTesting`:

```dart
await LevelMomentAds.instance.initialize(
  unsafeTesting: const UnsafeTesting(
    apiUrl: 'http://YOUR_COMPUTER_IP:8080',
    breakUrl: 'http://YOUR_COMPUTER_IP:3000/break',
    token: 'YOUR_EPLY_SBX_TOKEN',
  ),
);
```

**Warning:** Remove `unsafeTesting` from production builds. Production
configuration rejects endpoint overrides and explicit player tokens.

## Verify the migration

1. Run `flutter analyze` and build every target you ship.
2. Confirm `checkAccess()` does not display a route.
3. Confirm `ensureAccess()` opens pairing when action is required.
4. Complete a break and confirm the game resumes on dismissal or failure.
5. Confirm a multi-question break grants its game reward at most once.
