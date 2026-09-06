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

import 'package:flutter/material.dart';

import 'constants.dart';
import 'credential_bridge.dart';
import 'levelmoment_ads.dart';
import 'models.dart';
import 'widgets/level_moment_web_view.dart';

class LevelMomentRewardedAd {
  /// Mirrors: adUnitId — your game's identifier from the LevelMoment developer portal
  final String placementId;

  /// A credential to start from. Normally null: a paired device's credential
  /// is held by the SDK (platform secure store) and by the hosted page, and
  /// neither needs the game to supply it. Set it only for a sandbox token
  /// while you are integrating, or a token you read yourself from a
  /// parent-portal link. It is handed to the page over the bridge, never on
  /// the URL.
  final String? studentToken;

  /// SSV-parity custom data (mirrors LevelMomentConfig.customData in sdk-core).
  /// Sent in the credential handshake so the page stamps it on every
  /// impression it records and echoes it on reward.earned.
  final String? customData;

  /// Mirrors: fullScreenContentCallback — set this before calling show()
  LevelMomentFullScreenContentCallback? fullScreenContentCallback;

  // Internal state
  final String _format;
  bool _loaded = false;
  bool _disposed = false;

  LevelMomentRewardedAd._({
    required this.placementId,
    required String format,
    this.studentToken,
    this.customData,
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
    required LevelMomentAdLoadCallback adLoadCallback,
    String? studentToken,
    String format = 'flashcard',
    String? customData,
  }) async {
    assert(
      LevelMomentAds.instance.isInitialized,
      'Call LevelMomentAds.instance.initialize() before loading ads.',
    );

    String? resolvedToken;
    try {
      resolvedToken = LevelMomentAds.instance.resolveStudentToken(studentToken);
    } catch (err) {
      adLoadCallback.onAdFailedToLoad(
        LevelMomentAdError(code: 'invalid_request', message: '$err'),
      );
      return;
    }
    final ad = LevelMomentRewardedAd._(
      placementId: placementId,
      studentToken: resolvedToken,
      format: format,
      customData: customData,
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
    required void Function(
            LevelMomentRewardedAd ad, LevelMomentRewardItem reward)
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
      // Keep the secure store in step with the page: store what pairing
      // minted, forget what the server refused. The WebView answers
      // `needCredential` itself — it holds the controller that can inject.
      if (applyCredentialMessage(
        message,
        placementId,
        useDeviceStore: !LevelMomentAds.instance.mock &&
            LevelMomentAds.instance.unsafeTesting == null,
      )) {
        return;
      }
      switch (message) {
        // `openExternal` is launched by the WebView itself; the break carries
        // on behind the browser.
        case NeedCredential() ||
              CredentialIssued() ||
              CredentialInvalid() ||
              OpenExternal():
          return;
        case Ready():
          fullScreenContentCallback?.onAdShowedFullScreenContent?.call(this);
        case EarnedReward(:final amount, :final rewardId):
          onUserEarnedReward(
            this,
            LevelMomentRewardItem(
              type: 'question_answered',
              amount: amount,
              rewardId: rewardId,
            ),
          );
        // SignedIn belongs to the sign-in gate and never reaches a break. If
        // one ever arrives the surface has closed, so resume the game rather
        // than leaving it waiting for a dismiss that will not come.
        case Dismissed() || SignedIn():
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

    final hostedUrl = buildUrl();
    Navigator.of(context).push(
      MaterialPageRoute<void>(
        fullscreenDialog: true,
        builder: (_) => LevelMomentWebView(
          key: ValueKey(hostedUrl),
          url: hostedUrl,
          onMessage: handleMessage,
          onNeedCredential: credentialResponder(
            placementId: placementId,
            explicitToken: studentToken,
            customData: customData,
            useDeviceStore: !LevelMomentAds.instance.mock &&
                LevelMomentAds.instance.unsafeTesting == null,
            origin: levelMomentOrigin(LevelMomentAds.instance.breakUrl),
          ),
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

  /// Build the hosted /break page URL with placement / format params.
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
    }
    // No credential rides on this URL. The page asks over the bridge
    // (`needCredential`) and the answer comes from the secure store, so a live
    // token never reaches a launch URL, a crash report, or a web log.
    params[kHostCapabilitiesParam] = kHostCapabilities;
    params['protocolVersion'] = '$kLevelMomentProtocolVersion';
    params['sdkVersion'] = kLevelMomentSdkVersion;
    if (LevelMomentAds.instance.isUnsafeTesting) {
      params['sandbox'] = 'true';
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
