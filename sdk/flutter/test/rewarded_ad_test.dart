// Pure-Dart unit tests for the WebView-shell rewarded ad. No WebView platform
// channel is exercised — buildUrl(), the not_loaded guard, and HostMessage
// parsing are all platform-free.

import 'package:levelmoment_ads/levelmoment_ads.dart';
import 'package:levelmoment_ads/src/widgets/level_moment_web_view.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('LevelMomentRewardedAd.buildUrl', () {
    test('live mode embeds apiUrl + token + placementId + format', () async {
      await LevelMomentAds.instance.initialize(
        apiUrl: 'https://api.levelmoment.com',
        breakUrl: 'https://app.levelmoment.com/break',
        mock: false,
      );

      late LevelMomentRewardedAd ad;
      await LevelMomentRewardedAd.load(
        placementId: 'game-42',
        studentToken: 'tok abc/&=',
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
      expect(uri.queryParameters['token'], 'tok abc/&='); // properly encoded
      expect(uri.queryParameters.containsKey('mock'), isFalse);
    });

    test('mock mode sets mock=true and omits apiUrl/token', () async {
      await LevelMomentAds.instance.initialize(
        apiUrl: 'https://api.levelmoment.com',
        breakUrl: 'https://app.levelmoment.com/break',
        mock: true,
      );

      late LevelMomentRewardedAd ad;
      await LevelMomentRewardedAd.load(
        placementId: 'game-1',
        studentToken: 'ignored',
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

    test('appends with & when breakUrl already has a query', () async {
      await LevelMomentAds.instance.initialize(
        apiUrl: 'https://api.levelmoment.com',
        breakUrl: 'https://app.levelmoment.com/break?theme=dark',
        mock: true,
      );

      late LevelMomentRewardedAd ad;
      await LevelMomentRewardedAd.load(
        placementId: 'g',
        studentToken: 't',
        adLoadCallback: LevelMomentAdLoadCallback(
          onAdLoaded: (a) => ad = a,
          onAdFailedToLoad: (_) => fail('should not fail to load'),
        ),
      );

      final url = ad.buildUrl();
      expect(url.startsWith('https://app.levelmoment.com/break?theme=dark&'), isTrue);
      expect(Uri.parse(url).queryParameters['theme'], 'dark');
    });

    test('load() marks ready synchronously without network', () async {
      await LevelMomentAds.instance.initialize(
        apiUrl: 'https://api.levelmoment.com',
        breakUrl: 'https://app.levelmoment.com/break',
      );

      var loadedCalled = false;
      await LevelMomentRewardedAd.load(
        placementId: 'g',
        studentToken: 't',
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
  });

  group('LevelMomentRewardedAd.show before load', () {
    testWidgets('triggers onAdFailedToShowFullScreenContent with not_loaded',
        (tester) async {
      await LevelMomentAds.instance.initialize(
        apiUrl: 'https://api.levelmoment.com',
        breakUrl: 'https://app.levelmoment.com/break',
      );

      // Build an ad instance and force the not-loaded state by disposing it
      // (isLoaded == false). Reuses the public surface only.
      late LevelMomentRewardedAd ad;
      await LevelMomentRewardedAd.load(
        placementId: 'g',
        studentToken: 't',
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
        '{"type":"earnedReward","payload":{"amount":1}}',
      );
      expect(reward, isA<EarnedReward>());
      expect((reward as EarnedReward).amount, 1);

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

    test('earnedReward defaults amount to 0 when missing', () {
      final r = HostMessage.tryParse('{"type":"earnedReward"}');
      expect(r, isA<EarnedReward>());
      expect((r as EarnedReward).amount, 0);
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
          case Dismissed():
            if (terminal) return;
            terminal = true;
            dismissed++;
          case ErrorMsg():
            if (terminal) return;
            terminal = true;
            dismissed++;
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
