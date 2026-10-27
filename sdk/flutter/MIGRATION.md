# Migrate a Flutter rewarded placement

## Prerequisites

- Flutter 3.22 or later

The Flutter package is not published on pub.dev. Install it from the public
SDK repository at the `sdk-v0.4.2` tag.

## 1. Replace the dependency

Replace `google_mobile_ads` with the tagged release:

```yaml
dependencies:
  levelmoment_ads:
    git:
      url: https://github.com/levelmomentorg/sdk.git
      ref: sdk-v0.4.2
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
A `ready` result means the household had a subscription, or complimentary
access, when the parent linked the game; a lapse afterward does not unlink the
device.

## 4. Replace rewarded loading

Keep the existing load, show, reward, and full-screen callbacks:

```dart
var rewardGranted = false;
LevelMomentRewardedAd.load(
  placementId: 'YOUR_PLACEMENT_ID',
  adType: 'rewarded',
  targetDurationSeconds: 30,
  rewardAmount: 1,
  format: 'practice_set',
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
          if (!rewardGranted) {
            rewardGranted = true;
            recordGrantedReward(item.rewardId);
            grantBonus();
          }
        },
      );
    },
    onAdFailedToLoad: (_) => resumeGame(),
  ),
);
```

A passed graded break emits one reward callback, regardless of question count.
Deduplicate game grants by `rewardId = breakSessionId`. Dismissal and failure
only resume the game; the game chooses the item or currency amount.

Declare the slot this placement fills: `adType` (`'rewarded'` when it grants
something, `'interstitial'` when it doesn't), `targetDurationSeconds` (how
long the moment holds the player: 30 for a reward, 18 for resuming play, never
under 18) and `rewardAmount` (what the game grants, for reporting). These
replace the old `format` choice. Pass `format: 'practice_set'` with a rewarded slot as a floor,
as the example does. See
[slot sizes](https://levelmoment.com/docs/getting-started#slot-size).

## 5. Configure sandbox testing

Pass your `eply_sbx_` sandbox token alone inside `UnsafeTesting`. Keep it in
an untracked `sandbox.json`:

```json
{ "LM_SANDBOX_TOKEN": "YOUR_SANDBOX_TOKEN" }
```

Read it at compile time, empty in a release build:

```dart
import 'package:flutter/foundation.dart';

const sandboxToken =
    kReleaseMode ? '' : String.fromEnvironment('LM_SANDBOX_TOKEN');

await LevelMomentAds.instance.initialize(
  // Sandbox runs only. A production build passes no unsafeTesting.
  unsafeTesting:
      sandboxToken.isEmpty ? null : const UnsafeTesting(token: sandboxToken),
);
```

Run sandbox with `flutter run --dart-define-from-file=sandbox.json`.

**Warning:** `--dart-define-from-file` compiles the token into any build it is
passed to. Without the `kReleaseMode` gate, a release build made with it
serves every player sandbox breaks that earn nothing. See
[Testing and sandbox](https://levelmoment.com/docs/testing#production-build)
for the check that a release build has no token.

## 6. Optional: require an internet connection

Wrap your app in `LevelMomentConnectionGate` from `MaterialApp.builder` to
block play with a full-screen "No internet connection" message while the
device is offline. Pause the game loop from `onConnectionChange`. See
[Require an internet connection](README.md#require-an-internet-connection).

## Verify the migration

1. Run `flutter analyze` and build every target you ship.
2. Confirm `checkAccess()` does not display a route.
3. Confirm `ensureAccess()` opens pairing when action is required.
4. Complete a break and confirm the game resumes on dismissal or failure.
5. Confirm a multi-question break grants its game reward at most once.
6. Turn on airplane mode and confirm a break fails at once and the game
   resumes. With `LevelMomentConnectionGate`, confirm the message appears and
   goes away when the connection returns.
