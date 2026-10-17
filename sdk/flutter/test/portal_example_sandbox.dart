import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:levelmoment_ads/levelmoment_ads.dart';

// Sandbox runs only: run with --dart-define-from-file=sandbox.json, where
// the untracked sandbox.json is {"LM_SANDBOX_TOKEN": "YOUR_SANDBOX_TOKEN"}.
// Empty in a release build, even one made with that flag.
const sandboxToken =
    kReleaseMode ? '' : String.fromEnvironment('LM_SANDBOX_TOKEN');

// Call once at app startup.
Future<void> initializeLearning() => LevelMomentAds.instance.initialize(
      unsafeTesting: sandboxToken.isEmpty
          ? null
          : const UnsafeTesting(token: sandboxToken),
    );

// Call from the player's tap. The reward fires at most once per break; if a
// game server also grants from the webhook, record rewardId there too.
Future<void> showLearningBreak(BuildContext context,
    void Function(String rewardId) grantBonus, VoidCallback resumeGame) async {
  final access = await LevelMomentAds.instance
      .ensureAccess(context: context, placementId: "YOUR_PLACEMENT_ID");
  if (!context.mounted) return;
  if (access != EnsureSignedInResult.ready) {
    resumeGame();
    return;
  }
  var earned = false;
  await LevelMomentRewardedAd.load(
    placementId: "YOUR_PLACEMENT_ID",
    // One declaration per moment. Level Moment fits the break to the duration.
    adType: 'rewarded',
    targetDurationSeconds: 30,
    rewardAmount: 1,
    // A rewarded slot asks for a practice set at least; see Quickstart, step 3.
    format: 'practice_set',
    adLoadCallback: LevelMomentAdLoadCallback(
      onAdLoaded: (ad) {
        ad.fullScreenContentCallback = LevelMomentFullScreenContentCallback(
          onAdDismissedFullScreenContent: (ad) {
            ad.dispose();
            resumeGame();
          },
          onAdFailedToShowFullScreenContent: (ad, error) {
            ad.dispose();
            resumeGame();
          },
        );
        ad.show(
            context: context,
            onUserEarnedReward: (ad, reward) {
              if (reward.rewardId.isNotEmpty && !earned) {
                earned = true;
                grantBonus(reward.rewardId);
              }
            });
      },
      onAdFailedToLoad: (error) => resumeGame(),
    ),
  );
}
