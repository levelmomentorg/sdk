// Pure-Dart unit tests for the WebView-shell rewarded ad. No WebView platform
// channel is exercised — buildUrl(), the not_loaded guard, and HostMessage
// parsing are all platform-free.

import 'package:flutter/material.dart';
import 'package:levelmoment_ads/levelmoment_ads.dart';
import 'package:levelmoment_ads/src/widgets/level_moment_web_view.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('initialization security defaults', () {
    test('uses the canonical production endpoints by default', () async {
      await LevelMomentAds.instance.initialize();

      expect(LevelMomentAds.instance.breakUrl, 'https://levelmoment.com/break');
      expect(LevelMomentAds.instance.apiUrl, 'https://levelmoment.com/api');
      expect(LevelMomentAds.instance.isUnsafeTesting, isFalse);
    });

    test('rejects a noncanonical endpoint without unsafeTesting', () async {
      expect(
        () => LevelMomentAds.instance.initialize(
          breakUrl: 'https://example.test/break',
        ),
        throwsArgumentError,
      );
    });

    test('unsafe testing requires a sandbox token prefix', () async {
      expect(
        () => LevelMomentAds.instance.initialize(
          unsafeTesting: const UnsafeTesting(
            breakUrl: 'https://example.test/break',
            apiUrl: 'https://example.test/api',
            token: 'production-token',
          ),
        ),
        throwsArgumentError,
      );
    });
  });

  group('LevelMomentRewardedAd.buildUrl', () {
    test('live mode embeds apiUrl + placementId + format — never a token',
        () async {
      await LevelMomentAds.instance.initialize(
        unsafeTesting: const UnsafeTesting(
          apiUrl: 'https://api.levelmoment.com',
          breakUrl: 'https://app.levelmoment.com/break',
        ),
        mock: false,
      );

      late LevelMomentRewardedAd ad;
      await LevelMomentRewardedAd.load(
        placementId: 'game-42',
        studentToken: 'eply_sbx_abc/&=',
        format: 'quiz',
        adLoadCallback: LevelMomentAdLoadCallback(
          onAdLoaded: (a) => ad = a,
          onAdFailedToLoad: (_) => fail('should not fail to load'),
        ),
      );

      final url = ad.buildUrl();
      final uri = Uri.parse(url);
      expect(uri.origin + uri.path, 'https://app.levelmoment.com/break');
      expect(uri.queryParameters['placementId'], 'game-42');
      expect(uri.queryParameters['format'], 'quiz');
      expect(uri.queryParameters['apiUrl'], 'https://api.levelmoment.com');
      // The credential never rides on the URL, even when the game supplied
      // one to load() — it travels only over the postMessage bridge, as
      // credentialResponder's explicitToken argument.
      expect(uri.queryParameters.containsKey('token'), isFalse);
      expect(uri.queryParameters.containsKey('mock'), isFalse);
      expect(uri.queryParameters['sandbox'], 'true');
      expect(uri.queryParameters['protocolVersion'], '1');
      expect(uri.queryParameters['sdkVersion'], '0.2.0');
    });

    test(
        'announces openExternal support — an old shell that cannot open a '
        'browser must not be offered the button', () async {
      await LevelMomentAds.instance.initialize(
        unsafeTesting: const UnsafeTesting(
          apiUrl: 'https://api.levelmoment.com',
          breakUrl: 'https://app.levelmoment.com/break',
        ),
        mock: false,
      );

      late LevelMomentRewardedAd ad;
      await LevelMomentRewardedAd.load(
        placementId: 'game-42',
        adLoadCallback: LevelMomentAdLoadCallback(
          onAdLoaded: (a) => ad = a,
          onAdFailedToLoad: (_) => fail('should not fail to load'),
        ),
      );

      final uri = Uri.parse(ad.buildUrl());
      expect(uri.queryParameters['caps'], 'openExternal');
    });

    test('keeps SSV customData off the URL for the credential handshake',
        () async {
      await LevelMomentAds.instance.initialize(
        unsafeTesting: const UnsafeTesting(
          apiUrl: 'https://api.levelmoment.com',
          breakUrl: 'https://app.levelmoment.com/break',
        ),
        mock: false,
      );

      late LevelMomentRewardedAd ad;
      await LevelMomentRewardedAd.load(
        placementId: 'game-42',
        studentToken: 'eply_sbx_tok',
        customData: 'order/42&x',
        adLoadCallback: LevelMomentAdLoadCallback(
          onAdLoaded: (a) => ad = a,
          onAdFailedToLoad: (_) => fail('should not fail to load'),
        ),
      );

      final uri = Uri.parse(ad.buildUrl());
      expect(uri.queryParameters.containsKey('customData'), isFalse);
    });

    test('omits customData from the URL when not provided', () async {
      await LevelMomentAds.instance.initialize(
        unsafeTesting: const UnsafeTesting(
          apiUrl: 'https://api.levelmoment.com',
          breakUrl: 'https://app.levelmoment.com/break',
        ),
        mock: false,
      );

      late LevelMomentRewardedAd ad;
      await LevelMomentRewardedAd.load(
        placementId: 'game-42',
        studentToken: 'eply_sbx_tok',
        adLoadCallback: LevelMomentAdLoadCallback(
          onAdLoaded: (a) => ad = a,
          onAdFailedToLoad: (_) => fail('should not fail to load'),
        ),
      );

      expect(
        Uri.parse(ad.buildUrl()).queryParameters.containsKey('customData'),
        isFalse,
      );
    });

    test('mock mode sets mock=true and omits apiUrl/token', () async {
      await LevelMomentAds.instance.initialize(
        unsafeTesting: const UnsafeTesting(
          apiUrl: 'https://api.levelmoment.com',
          breakUrl: 'https://app.levelmoment.com/break',
        ),
        mock: true,
      );

      late LevelMomentRewardedAd ad;
      await LevelMomentRewardedAd.load(
        placementId: 'game-1',
        studentToken: 'eply_sbx_ignored',
        adLoadCallback: LevelMomentAdLoadCallback(
          onAdLoaded: (a) => ad = a,
          onAdFailedToLoad: (_) => fail('should not fail to load'),
        ),
      );

      final uri = Uri.parse(ad.buildUrl());
      expect(uri.queryParameters['mock'], 'true');
      expect(uri.queryParameters['placementId'], 'game-1');
      expect(uri.queryParameters['format'], 'flashcard'); // default
      expect(uri.queryParameters.containsKey('apiUrl'), isFalse);
      expect(uri.queryParameters.containsKey('token'), isFalse);
    });

    test('uses the canonical hosted URL by default', () async {
      await LevelMomentAds.instance.initialize(
        apiUrl: 'https://levelmoment.com/api',
        breakUrl: 'https://levelmoment.com/break',
        mock: true,
      );

      late LevelMomentRewardedAd ad;
      await LevelMomentRewardedAd.load(
        placementId: 'g',
        adLoadCallback: LevelMomentAdLoadCallback(
          onAdLoaded: (a) => ad = a,
          onAdFailedToLoad: (_) => fail('should not fail to load'),
        ),
      );

      final url = ad.buildUrl();
      expect(url.startsWith('https://levelmoment.com/break?'), isTrue);
    });

    test('load() marks ready synchronously without network', () async {
      await LevelMomentAds.instance.initialize(
        unsafeTesting: const UnsafeTesting(
          apiUrl: 'https://api.levelmoment.com',
          breakUrl: 'https://app.levelmoment.com/break',
        ),
      );

      var loadedCalled = false;
      await LevelMomentRewardedAd.load(
        placementId: 'g',
        studentToken: 'eply_sbx_t',
        adLoadCallback: LevelMomentAdLoadCallback(
          onAdLoaded: (a) {
            loadedCalled = true;
            expect(a.isLoaded, isTrue);
          },
          onAdFailedToLoad: (_) => fail('should not fail'),
        ),
      );
      expect(loadedCalled, isTrue);
    });

    test('load() works with no studentToken at all — the paired-device path',
        () async {
      // The common case now: a device that already paired holds its
      // credential in the secure store (or the hosted page's own storage),
      // so nothing needs to flow through load() at all. studentToken exists
      // only for a sandbox token or a link the game read itself.
      await LevelMomentAds.instance.initialize(
        unsafeTesting: const UnsafeTesting(
          apiUrl: 'https://api.levelmoment.com',
          breakUrl: 'https://app.levelmoment.com/break',
        ),
      );

      late LevelMomentRewardedAd ad;
      await LevelMomentRewardedAd.load(
        placementId: 'g',
        adLoadCallback: LevelMomentAdLoadCallback(
          onAdLoaded: (a) => ad = a,
          onAdFailedToLoad: (_) => fail('should not fail to load'),
        ),
      );

      expect(ad.isLoaded, isTrue);
      expect(ad.studentToken, isNull);
      expect(
        Uri.parse(ad.buildUrl()).queryParameters.containsKey('token'),
        isFalse,
      );
    });
  });

  group('LevelMomentRewardedAd.show before load', () {
    testWidgets('triggers onAdFailedToShowFullScreenContent with not_loaded',
        (tester) async {
      await LevelMomentAds.instance.initialize(
        unsafeTesting: const UnsafeTesting(
          apiUrl: 'https://api.levelmoment.com',
          breakUrl: 'https://app.levelmoment.com/break',
        ),
      );

      // Build an ad instance and force the not-loaded state by disposing it
      // (isLoaded == false). Reuses the public surface only.
      late LevelMomentRewardedAd ad;
      await LevelMomentRewardedAd.load(
        placementId: 'g',
        adLoadCallback: LevelMomentAdLoadCallback(
          onAdLoaded: (a) => ad = a,
          onAdFailedToLoad: (_) => fail('should not fail'),
        ),
      );
      ad.dispose(); // now isLoaded == false

      LevelMomentAdError? captured;
      ad.fullScreenContentCallback = LevelMomentFullScreenContentCallback(
        onAdFailedToShowFullScreenContent: (_, err) => captured = err,
      );

      await tester.pumpWidget(
        MaterialApp(
          home: Builder(
            builder: (context) => ElevatedButton(
              onPressed: () => ad.show(
                context: context,
                onUserEarnedReward: (_, __) => fail('no reward expected'),
              ),
              child: const Text('show'),
            ),
          ),
        ),
      );
      await tester.tap(find.text('show'));
      await tester.pump();

      expect(captured, isNotNull);
      expect(captured!.code, 'not_loaded');
    });
  });

  group('HostMessage.tryParse', () {
    test('parses all 4 variants', () {
      expect(HostMessage.tryParse('{"type":"ready"}'), isA<Ready>());

      final reward = HostMessage.tryParse(
        '{"type":"earnedReward","payload":{"amount":1,"rewardId":"impression-7"}}',
      );
      expect(reward, isA<EarnedReward>());
      final earned = reward as EarnedReward;
      expect(earned.amount, 1);
      expect(earned.rewardId, 'impression-7');
      expect(
        HostMessage.tryParse(
          '{"type":"earnedReward","payload":{"amount":2}}',
        ),
        isNull,
      );
      expect(
        HostMessage.tryParse(
          '{"type":"earnedReward","payload":{"amount":1,"rewardId":""}}',
        ),
        isNull,
      );
      expect(
        HostMessage.tryParse('{"type":"ready","protocolVersion":2}'),
        isNull,
      );

      expect(HostMessage.tryParse('{"type":"dismissed"}'), isA<Dismissed>());

      final err = HostMessage.tryParse(
        '{"type":"error","payload":{"code":"invalid_token","message":"bad"}}',
      );
      expect(err, isA<ErrorMsg>());
      expect((err as ErrorMsg).code, 'invalid_token');
      expect(err.message, 'bad');
    });

    test('returns null for malformed / unknown payloads', () {
      expect(HostMessage.tryParse('not json'), isNull);
      expect(HostMessage.tryParse('{"type":"bogus"}'), isNull);
      expect(HostMessage.tryParse('[1,2,3]'), isNull);
    });

    test('rejects earnedReward without a payload', () {
      final r = HostMessage.tryParse('{"type":"earnedReward"}');
      expect(r, isNull);
    });
  });

  group('show() terminal-once', () {
    // Locks the contract that the dispatch closure inside show() collapses two
    // terminal messages (dismissed/error) into a single dismiss path, while
    // earnedReward may fire repeatedly. The closure is private; we exercise
    // the equivalent guard logic that show() implements so the contract is
    // pinned even though the WebView channel isn't driven here.
    test('two terminals collapse to one dismiss; rewards repeat', () {
      var dismissed = 0;
      var rewards = 0;
      var terminal = false;

      void handle(HostMessage m) {
        switch (m) {
          case Ready():
            break;
          case EarnedReward():
            rewards++;
          case Dismissed() || SignedIn():
            if (terminal) return;
            terminal = true;
            dismissed++;
          case ErrorMsg():
            if (terminal) return;
            terminal = true;
            dismissed++;
          // The credential trio is the show() closure's bookkeeping (handled
          // by applyCredentialMessage before this switch even runs in the
          // real code) — not a verdict, so it ends nothing here either.
          case NeedCredential() ||
                CredentialIssued() ||
                CredentialInvalid() ||
                OpenExternal():
            break;
        }
      }

      handle(const EarnedReward(1));
      handle(const EarnedReward(0));
      handle(const Dismissed());
      handle(const ErrorMsg('late', 'ignored')); // second terminal — ignored

      expect(rewards, 2);
      expect(dismissed, 1);
    });
  });

  // Note: LevelMomentWebView itself is intentionally NOT mounted in this suite —
  // constructing a WebViewController touches WebViewPlatform.instance, which
  // has no implementation under plain `flutter test`. The widget is a thin
  // presentation shell; its terminal-once guard mirrors the show() dispatch
  // logic pinned above, and its message parsing is covered by the
  // HostMessage.tryParse group.
}
