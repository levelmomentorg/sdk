// ---------------------------------------------------------------------------
// Models — public surface, mirrors google_mobile_ads types exactly
//
// This SDK is a thin WebView shell over the hosted /break page, which renders
// all question UI; only the public callback / error / reward types games
// reference live here (the same shape the react-native SDK keeps).
// ---------------------------------------------------------------------------

import 'rewarded_ad.dart';

/// Mirrors: LoadAdError
class LevelMomentAdError {
  final String code;
  final String message;

  const LevelMomentAdError({required this.code, required this.message});

  @override
  String toString() => 'LevelMomentAdError($code: $message)';
}

/// Mirrors: RewardItem
/// type is always 'question_answered'
/// amount is 1 for correct answer, 0 for skipped/wrong
class LevelMomentRewardItem {
  final String type;
  final num amount;

  const LevelMomentRewardItem({required this.type, required this.amount});
}

/// Mirrors: RewardedAdLoadCallback
class LevelMomentAdLoadCallback {
  /// Fired when the ad is ready to show.
  /// Mirrors: onAdLoaded
  final void Function(LevelMomentRewardedAd ad) onAdLoaded;

  /// Fired when the ad could not be prepared.
  /// Mirrors: onAdFailedToLoad
  final void Function(LevelMomentAdError error) onAdFailedToLoad;

  const LevelMomentAdLoadCallback({
    required this.onAdLoaded,
    required this.onAdFailedToLoad,
  });
}

/// Mirrors: FullScreenContentCallback
class LevelMomentFullScreenContentCallback {
  /// Fired when the break UI is presented.
  /// Mirrors: onAdShowedFullScreenContent
  final void Function(LevelMomentRewardedAd ad)? onAdShowedFullScreenContent;

  /// Fired if show() is called before load() completes.
  /// Mirrors: onAdFailedToShowFullScreenContent
  final void Function(LevelMomentRewardedAd ad, LevelMomentAdError error)?
      onAdFailedToShowFullScreenContent;

  /// Fired when the ad break ends (correct answer, wrong answer, or skip).
  /// Always resume your game here.
  /// Mirrors: onAdDismissedFullScreenContent
  final void Function(LevelMomentRewardedAd ad)? onAdDismissedFullScreenContent;

  const LevelMomentFullScreenContentCallback({
    this.onAdShowedFullScreenContent,
    this.onAdFailedToShowFullScreenContent,
    this.onAdDismissedFullScreenContent,
  });
}
