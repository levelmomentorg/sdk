// ---------------------------------------------------------------------------
// LevelMomentRewardedAd
//
// Drop-in replacement for AdMob's RewardedAd. The SDK is a thin shell over a
// WebView pointing at the hosted break page (platform/web/app/break). The page
// renders all question UI, fetches data, submits answers, AND owns the
// impression queue. Games call show(context:) and handle onUserEarnedReward /
// onAdDismissedFullScreenContent only. See docs/ADR-001-webview-rendering.md.
//
// Structurally mirrors sdk/react-native/src/LevelMomentAd.ts.
//
// BEFORE (AdMob):
//   RewardedAd.load(
//     adUnitId: 'ca-app-pub-xxx/yyy',
//     request: const AdRequest(),
//     rewardedAdLoadCallback: RewardedAdLoadCallback(
//       onAdLoaded: (ad) => _rewardedAd = ad,
//       onAdFailedToLoad: (err) => retry(),
//     ),
//   );
//   _rewardedAd?.fullScreenContentCallback = FullScreenContentCallback(
//     onAdDismissedFullScreenContent: (ad) { ad.dispose(); resumeGame(); },
//   );
//   _rewardedAd?.show(onUserEarnedReward: (ad, reward) => grantBonus());
//
// AFTER (LevelMoment — identical structure, the hosted page renders everything):
//   LevelMomentRewardedAd.load(
//     placementId: 'your-game-id',
//     studentToken: getTokenFromUrl(),
//     adLoadCallback: LevelMomentAdLoadCallback(
//       onAdLoaded: (ad) => _rewardedAd = ad,
//       onAdFailedToLoad: (err) => retry(),
//     ),
//   );
//   _rewardedAd?.fullScreenContentCallback = LevelMomentFullScreenContentCallback(
//     onAdDismissedFullScreenContent: (ad) { ad.dispose(); resumeGame(); },
//   );
//   _rewardedAd?.show(
//     context: context,
//     onUserEarnedReward: (ad, reward) => grantBonus(),
//   );
// ---------------------------------------------------------------------------

import 'package:flutter/foundation.dart' show visibleForTesting;
import 'package:flutter/material.dart';

import 'levelmoment_ads.dart';
import 'models.dart';
import 'widgets/level_moment_web_view.dart';

class LevelMomentRewardedAd {
  /// Mirrors: adUnitId — your game's identifier from the LevelMoment developer portal
  final String placementId;

  /// The student session token from the parent portal (?token= URL param)
  final String studentToken;

  /// Mirrors: fullScreenContentCallback — set this before calling show()
  LevelMomentFullScreenContentCallback? fullScreenContentCallback;

  // Internal state
  final String _format;
  bool _loaded = false;
  bool _disposed = false;

  LevelMomentRewardedAd._({
    required this.placementId,
    required this.studentToken,
    required String format,
  }) : _format = format;

  // ---------------------------------------------------------------------------
  // Static factory — mirrors RewardedAd.load()
  // ---------------------------------------------------------------------------

  /// Mark the ad ready to show. The actual question fetch happens inside the
  /// WebView when show() is called — there is no separate native preload step
  /// in this architecture. Kept as a separate call so consumers can stay on
  /// the familiar AdMob load -> show pattern.
  ///
  /// Mirrors: RewardedAd.load(adUnitId, request, rewardedAdLoadCallback) and
  /// LevelMomentAd.load() in the react-native SDK (synchronous mark-ready, no
  /// network).
  static Future<void> load({
    required String placementId,
    required String studentToken,
    required LevelMomentAdLoadCallback adLoadCallback,
    String format = 'flashcard',
  }) async {
    assert(
      LevelMomentAds.instance.isInitialized,
      'Call LevelMomentAds.instance.initialize() before loading ads.',
    );

    final ad = LevelMomentRewardedAd._(
      placementId: placementId,
      studentToken: studentToken,
      format: format,
    );
    ad._loaded = true;
    adLoadCallback.onAdLoaded(ad);
  }

  // ---------------------------------------------------------------------------
  // Instance API — mirrors the RewardedAd object returned to onAdLoaded
  // ---------------------------------------------------------------------------

  bool get isLoaded => _loaded && !_disposed;

  /// The break format: 'flashcard', 'quiz', or 'deep_dive'.
  String get format => _format;

  /// Display the break. No network call here — the hosted page does the fetch.
  /// Pushes a fullscreen route hosting a WebView that points at the hosted
  /// /break page, which renders all question types and session flows, then
  /// posts back terminal events.
  ///
  /// Mirrors: rewardedAd.show(onUserEarnedReward: ...) and LevelMomentAd.show().
  void show({
    required BuildContext context,
    required void Function(LevelMomentRewardedAd ad, LevelMomentRewardItem reward)
        onUserEarnedReward,
  }) {
    if (!isLoaded) {
      fullScreenContentCallback?.onAdFailedToShowFullScreenContent?.call(
        this,
        const LevelMomentAdError(
          code: 'not_loaded',
          message: 'show() called before onAdLoaded. Call load() first.',
        ),
      );
      return;
    }

    // Terminal-once guard for the dismiss path: earnedReward may fire many
    // times; dismissed/error fire the dismissal exactly once. Mirrors the
    // _shown/dismissedRef discipline in the react-native SDK.
    var terminal = false;

    void handleMessage(HostMessage message) {
      switch (message) {
        case Ready():
          fullScreenContentCallback?.onAdShowedFullScreenContent?.call(this);
        case EarnedReward(:final amount):
          onUserEarnedReward(
            this,
            LevelMomentRewardItem(type: 'question_answered', amount: amount),
          );
        case Dismissed():
          if (terminal) return;
          terminal = true;
          fullScreenContentCallback?.onAdDismissedFullScreenContent?.call(this);
          dispose();
        case ErrorMsg(:final code, :final message):
          if (terminal) return;
          terminal = true;
          fullScreenContentCallback?.onAdFailedToShowFullScreenContent?.call(
            this,
            LevelMomentAdError(code: code, message: message),
          );
          dispose();
      }
    }

    Navigator.of(context).push(
      MaterialPageRoute<void>(
        fullscreenDialog: true,
        builder: (_) => LevelMomentWebView(
          url: buildUrl(),
          onMessage: handleMessage,
        ),
      ),
    );
  }

  /// Release resources. Called automatically after the break ends.
  /// Mirrors: ad.dispose()
  void dispose() {
    _disposed = true;
  }

  // ---------------------------------------------------------------------------
  // URL building — mirrors LevelMomentAd._buildUrl() in the react-native SDK
  // ---------------------------------------------------------------------------

  /// Build the hosted /break page URL with placement / token / format params.
  /// Exposed for testing; treat as private elsewhere.
  @visibleForTesting
  String buildUrl() {
    final params = <String, String>{
      'placementId': placementId,
      'format': _format,
    };
    if (LevelMomentAds.instance.mock) {
      params['mock'] = 'true';
    } else {
      params['apiUrl'] = LevelMomentAds.instance.apiUrl;
      params['token'] = studentToken;
    }

    final query = params.entries
        .map((e) =>
            '${Uri.encodeQueryComponent(e.key)}=${Uri.encodeQueryComponent(e.value)}')
        .join('&');
    final breakUrl = LevelMomentAds.instance.breakUrl;
    final sep = breakUrl.contains('?') ? '&' : '?';
    return '$breakUrl$sep$query';
  }
}
