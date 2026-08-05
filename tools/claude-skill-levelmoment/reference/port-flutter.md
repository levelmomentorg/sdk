# Port — Flutter (`google_mobile_ads` → LevelMoment)

You are inside `/levelmoment port`, platform = flutter.

## What the LevelMoment Flutter SDK looks like

```dart
import 'package:levelmoment_ads/levelmoment_ads.dart';

// 1. Initialize once at startup (e.g. in main.dart before runApp):
//    `breakUrl` is REQUIRED — the WebView shell has no other way to know
//    where the hosted /break page lives. `mock: true` renders bundled mock
//    questions (no live API / token) — handy for a first integration.
await LevelMomentAds.instance.initialize(
  apiUrl: 'https://api.levelmoment.com',
  breakUrl: 'https://app.levelmoment.com/break',
  // mock: true,
);

// 2. Load + show in your ad break widget:
LevelMomentRewardedAd.load(
  placementId: '{{PLACEMENT_ID}}',
  studentToken: await getTokenFromUrl(),
  adLoadCallback: LevelMomentAdLoadCallback(
    onAdLoaded: (ad) {
      _rewardedAd = ad;
      _rewardedAd!.fullScreenContentCallback = LevelMomentFullScreenContentCallback(
        onAdDismissedFullScreenContent: (ad) {
          ad.dispose();
          resumeGame();
          _loadNextAd();
        },
      );
    },
    onAdFailedToLoad: (err) => debugPrint('LevelMoment load failed: $err'),
  ),
);

// At the natural pause point:
_rewardedAd?.show(
  context: context,
  onUserEarnedReward: (ad, reward) {
    if (reward.amount == 1) grantBonus();
  },
);
```

Name mapping:

| google_mobile_ads                 | LevelMoment                                               |
| --------------------------------- | --------------------------------------------------------- |
| `MobileAds.instance.initialize()` | `LevelMomentAds.instance.initialize(apiUrl:, breakUrl:)`  |
| `RewardedAd.load(adUnitId:)`      | `LevelMomentRewardedAd.load(placementId:, studentToken:)` |
| `AdRequest()`                     | (replaced by `placementId` + `studentToken`)              |
| `RewardedAdLoadCallback`          | `LevelMomentAdLoadCallback`                               |
| `FullScreenContentCallback`       | `LevelMomentFullScreenContentCallback`                    |
| `LoadAdError`                     | `LevelMomentAdError`                                      |
| `RewardItem`                      | `LevelMomentRewardItem`                                   |
| `AdWithoutView`                   | `LevelMomentRewardedAd`                                   |

## Step 1 — update `pubspec.yaml`

```diff
 dependencies:
   flutter:
     sdk: flutter
-  google_mobile_ads: ^5.0.0
+  levelmoment_ads:
+    git:
+      url: https://github.com/levelmomentorg/sdk.git
+      path: sdk/flutter
+      ref: v0.1.0
```

(Phase 1 ships from GitHub source; pub.dev publishing deferred.)

Do **not** run `flutter pub get` automatically — ask the user; native
plugins can have non-trivial install behavior.

## Step 2 — replace the import

In every file from the inventory:

```diff
-import 'package:google_mobile_ads/google_mobile_ads.dart';
+import 'package:levelmoment_ads/levelmoment_ads.dart';
```

## Step 3 — replace initialization

```diff
-await MobileAds.instance.initialize();
+await LevelMomentAds.instance.initialize(
+  apiUrl: 'https://api.levelmoment.com',
+  breakUrl: 'https://app.levelmoment.com/break',  // REQUIRED
+);
```

`breakUrl` is a **required** named arg (the WebView shell needs it to find
the hosted `/break` page); omitting it is a compile error. Pass `mock: true`
as well for a first integration that needs no live API or student token.

Usually in `main()` before `runApp(...)`. If the project uses
`Firebase.initializeApp()` near the same place, leave that untouched.

## Step 4 — replace ad load

```diff
-RewardedAd.load(
-  adUnitId: 'ca-app-pub-xxx/yyy',
-  request: const AdRequest(),
-  rewardedAdLoadCallback: RewardedAdLoadCallback(
-    onAdLoaded: (RewardedAd ad) { _rewardedAd = ad; },
-    onAdFailedToLoad: (LoadAdError error) { print('Failed: $error'); },
-  ),
-);
+LevelMomentRewardedAd.load(
+  placementId: '{{PLACEMENT_ID}}',
+  studentToken: await getTokenFromUrl(),
+  adLoadCallback: LevelMomentAdLoadCallback(
+    onAdLoaded: (LevelMomentRewardedAd ad) { _rewardedAd = ad; },
+    onAdFailedToLoad: (LevelMomentAdError error) { debugPrint('Failed: $error'); },
+  ),
+);
```

Substitute `{{PLACEMENT_ID}}` with the placement ID from the port flow.

If no `getTokenFromUrl()` helper exists in the file, add the template
from `.claude/skills/levelmoment/templates/flutter-get-token.dart.tmpl`.

## Step 5 — replace `fullScreenContentCallback`

```diff
-_rewardedAd?.fullScreenContentCallback = FullScreenContentCallback(
-  onAdShowedFullScreenContent: (ad) {},
-  onAdFailedToShowFullScreenContent: (ad, error) {},
-  onAdDismissedFullScreenContent: (ad) {
-    ad.dispose();
-    resumeGame();
-    _loadNextAd();
-  },
-);
+_rewardedAd?.fullScreenContentCallback = LevelMomentFullScreenContentCallback(
+  onAdShowedFullScreenContent: (ad) {},
+  onAdFailedToShowFullScreenContent: (ad, error) {},
+  onAdDismissedFullScreenContent: (ad) {
+    ad.dispose();
+    resumeGame();
+    _loadNextAd();
+  },
+);
```

Preserve the user's `resumeGame()` / `_loadNextAd()` body.

## Step 6 — replace `show()`

```diff
 _rewardedAd?.show(
-  onUserEarnedReward: (AdWithoutView ad, RewardItem reward) {
-    grantBonus();
-  },
+  context: context,  // LevelMoment needs the BuildContext to push the break screen
+  onUserEarnedReward: (LevelMomentRewardedAd ad, LevelMomentRewardItem reward) {
+    if (reward.amount == 1) grantBonus();
+  },
 );
```

Two differences:

1. `context:` parameter is new (LevelMoment pushes a full-screen route).
2. The reward types change.
3. Gate `grantBonus()` on `reward.amount == 1`.

If the `show()` call isn't inside a `BuildContext`, find the nearest
`BuildContext` and pass it down. If that requires significant
refactoring, leave a `// LEVELMOMENT_TODO:` and ask the user.

### Flame games (no `BuildContext` in the game loop)

A `FlameGame`'s `update()` / collision callbacks have **no `BuildContext`**, so
`show()` can't be called from there. The clean seam (validated porting
`ufrshubham/dino_run`, LevelMoment #61) is to put the ad call in the **game-over
overlay widget**, which Flame renders via `overlays.add(...)` and which _does_
have a context:

- Make the overlay a `StatefulWidget`. In `initState()`, register a
  `WidgetsBinding.instance.addPostFrameCallback((_) { ad.show(context: context, ...); })`
  so `show()` runs once after the overlay mounts (not every frame).
- Leave the game's death detection (`update()` / `lives <= 0`) **pristine** —
  it already triggers `overlays.add(GameOverMenu.id)`; you only add the ad call
  inside that overlay.
- Resume rides the overlay's existing "Restart" button — the WebView route
  pops back to the overlay card on dismiss, so no extra resume wiring is needed.

### Pre-publish dependency (`levelmoment_ads` not yet on pub.dev)

The `git:` dependency in **Step 1** is the preferred pre-publish form — it
resolves from any machine (pulls `sdk/flutter` from GitHub at a `ref`), and the
Flutter SDK has no levelmoment-internal Dart dependency, so it stands alone.

If you're integrating against a **local levelmoment checkout** (as the
`ufrshubham/dino_run` port did, LevelMoment #61), a `path:` dep also works:

```yaml
dependencies:
  levelmoment_ads:
    path: /abs/path/to/levelmoment/sdk/flutter
```

But flag in your summary that a `path:` is build-machine-local — a fresh clone
won't `flutter pub get` until you switch it to the `git:` form or a published
version range. Prefer `git:` unless you specifically need the local checkout.

## Step 7 — preload pattern

In the user's existing `_loadNextAd()`, swap to the LevelMoment equivalent.
If they don't have one, add it as a method that mirrors Step 4.

## Step 8 — flag for review

- **`MobileAds.instance.updateRequestConfiguration(...)`** — GDPR/UMP
  config; LevelMoment doesn't need it. Flag.
- **`AdRequest(keywords:, contentUrl:, nonPersonalizedAds:)`** — these
  AdRequest options have no LevelMoment equivalent. Flag.
- **Banner / native / interstitial ads** — `BannerAd`, `NativeAd`,
  `InterstitialAd`. LevelMoment ports only rewarded ads. Flag.
- **`MobileAds.instance.setAppMuted(true)`** — LevelMoment has no audio.
  Safe to remove but flag.
