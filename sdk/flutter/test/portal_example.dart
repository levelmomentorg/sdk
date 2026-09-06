import 'package:flutter/material.dart';
import 'package:levelmoment_ads/levelmoment_ads.dart';

// Call once at app startup.
Future<void> initializeLearning() => LevelMomentAds.instance.initialize();

// Call from the player's learning action. Pass your game's callbacks.
Future<void> showLearningBreak(BuildContext context, VoidCallback grantBonus, VoidCallback resumeGame) async {
  final access = await LevelMomentAds.instance.ensureAccess(context: context, placementId: "YOUR_PLACEMENT_ID");
  if (!context.mounted) return;
  if (access != EnsureSignedInResult.ready) { resumeGame(); return; }
  var earned = false;
  await LevelMomentRewardedAd.load(
    placementId: "YOUR_PLACEMENT_ID",
    adLoadCallback: LevelMomentAdLoadCallback(
      onAdLoaded: (ad) {
        ad.fullScreenContentCallback = LevelMomentFullScreenContentCallback(
          onAdDismissedFullScreenContent: (ad) { ad.dispose(); resumeGame(); },
          onAdFailedToShowFullScreenContent: (ad, error) { ad.dispose(); resumeGame(); },
        );
        ad.show(context: context, onUserEarnedReward: (ad, reward) { if (reward.amount == 1 && !earned) { earned = true; grantBonus(); } });
      },
      onAdFailedToLoad: (error) => resumeGame(),
    ),
  );
}
