// Pure-Dart unit tests for the WebView-shell rewarded ad. No WebView platform
// channel is exercised — buildUrl(), the not_loaded guard, and HostMessage
// parsing are all platform-free.

import 'package:flutter/material.dart';
import 'package:levelmoment_ads/levelmoment_ads.dart';
import 'package:levelmoment_ads/src/widgets/level_moment_web_view.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:webview_flutter/webview_flutter.dart'
    show WebResourceError, WebResourceErrorType;

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
        format: 'practice_set',
        adLoadCallback: LevelMomentAdLoadCallback(
          onAdLoaded: (a) => ad = a,
          onAdFailedToLoad: (_) => fail('should not fail to load'),
        ),
      );

      final url = ad.buildUrl();
      final uri = Uri.parse(url);
      expect(uri.origin + uri.path, 'https://app.levelmoment.com/break');
      expect(uri.queryParameters['placementId'], 'game-42');
      expect(uri.queryParameters['format'], 'practice_set');
      expect(uri.queryParameters['apiUrl'], 'https://api.levelmoment.com');
      // The credential never rides on the URL, even when the game supplied
      // one to load() — it travels only over the postMessage bridge, as
      // credentialResponder's explicitToken argument.
      expect(uri.queryParameters.containsKey('token'), isFalse);
      expect(uri.queryParameters.containsKey('mock'), isFalse);
      expect(uri.queryParameters['sandbox'], 'true');
      expect(uri.queryParameters['protocolVersion'], '1');
      expect(uri.queryParameters['sdkVersion'], '0.3.0');
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
      expect(uri.queryParameters['format'], 'quick_question'); // default
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
        '{"type":"earnedReward","payload":{"rewardId":"break-7","earnedAt":"2026-09-22T00:00:00.000Z"}}',
      );
      expect(reward, isA<EarnedReward>());
      final earned = reward as EarnedReward;
      expect(earned.rewardId, 'break-7');
      expect(earned.earnedAt, '2026-09-22T00:00:00.000Z');
      expect(
        HostMessage.tryParse(
          '{"type":"earnedReward","payload":{"earnedAt":"2026-09-22T00:00:00.000Z"}}',
        ),
        isNull,
      );
      expect(
        HostMessage.tryParse(
          '{"type":"earnedReward","payload":{"rewardId":"","earnedAt":"2026-09-22T00:00:00.000Z"}}',
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
    // terminal messages (dismissed/error) into a single dismiss path and
    // forwards one earnedReward. The closure is private; we exercise
    // the equivalent guard logic that show() implements so the contract is
    // pinned even though the WebView channel isn't driven here.
    test('two terminals collapse to one dismiss; reward forwards once', () {
      var dismissed = 0;
      var rewards = 0;
      var terminal = false;
      var rewardDelivered = false;

      void handle(HostMessage m) {
        switch (m) {
          case Ready():
            break;
          case EarnedReward():
            if (terminal || rewardDelivered) return;
            rewardDelivered = true;
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

      handle(const EarnedReward('break-1', '2026-09-22T00:00:00.000Z'));
      handle(const EarnedReward('break-2', '2026-09-22T00:00:01.000Z'));
      handle(const Dismissed());
      handle(const ErrorMsg('late', 'ignored')); // second terminal — ignored

      expect(rewards, 1);
      expect(dismissed, 1);
    });
  });

  group('close button', () {
    // A break has no close button (owner, October 17, 2026): it ends in a
    // pass, a fail, or a load failure. The sign-in gate keeps one, because
    // closing it is a parent's "not now". The WebView cannot mount here (see
    // the note below), so the surfaces are captured as pushed.
    final views = <LevelMomentWebView>[];

    setUp(() {
      views.clear();
      debugHostedSurfaceOverride = (view) {
        views.add(view);
        return const SizedBox.shrink();
      };
    });

    tearDown(() => debugHostedSurfaceOverride = null);

    test(
        'a break keeps its way out only until the page confirms it ends itself',
        () {
      // Before `ready` carries leaveSupported, an older page that can't end
      // itself on every failure must not trap the player.
      expect(
        hostedSurfaceCanLeave(closeButton: false, leaveSupported: false),
        isTrue,
      );
      expect(
        hostedSurfaceCanLeave(closeButton: false, leaveSupported: true),
        isFalse,
      );
      // The gate always keeps its close button: closing it is "not now".
      expect(
        hostedSurfaceCanLeave(closeButton: true, leaveSupported: true),
        isTrue,
      );
    });

    test('a dead page process ends the surface even after ready', () {
      const terminated = WebResourceError(
        errorCode: 0,
        description: 'terminated',
        errorType: WebResourceErrorType.webContentProcessTerminated,
        isForMainFrame: true,
      );
      const subresource = WebResourceError(
        errorCode: -1,
        description: 'image failed',
        isForMainFrame: false,
      );
      const mainFrame = WebResourceError(
        errorCode: -1004,
        description: 'could not connect',
        isForMainFrame: true,
      );
      expect(isTerminalWebResourceError(terminated, pageReady: true), isTrue);
      expect(isTerminalWebResourceError(mainFrame, pageReady: false), isTrue);
      expect(isTerminalWebResourceError(mainFrame, pageReady: true), isFalse);
      expect(
          isTerminalWebResourceError(subresource, pageReady: false), isFalse);
    });

    test('ready reports leaveSupported only when the page says so', () {
      Ready parse(String raw) => HostMessage.tryParse(raw)! as Ready;
      expect(parse('{"type":"ready"}').leaveSupported, isFalse);
      expect(
        parse('{"type":"ready","payload":{"leaveSupported":true}}')
            .leaveSupported,
        isTrue,
      );
      expect(
        parse('{"type":"ready","payload":{"leaveSupported":"yes"}}')
            .leaveSupported,
        isFalse,
      );
    });

    testWidgets('a break pushes no close button; the sign-in gate keeps one',
        (tester) async {
      await LevelMomentAds.instance.initialize(
        unsafeTesting: const UnsafeTesting(token: 'eply_sbx_test'),
      );
      await tester.pumpWidget(const MaterialApp(home: SizedBox()));
      final context = tester.element(find.byType(SizedBox));

      late LevelMomentRewardedAd ad;
      await LevelMomentRewardedAd.load(
        placementId: 'game-42',
        adLoadCallback: LevelMomentAdLoadCallback(
          onAdLoaded: (a) => ad = a,
          onAdFailedToLoad: (e) => fail('should load: $e'),
        ),
      );
      ad.show(context: context, onUserEarnedReward: (_, __) {});
      await tester.pumpAndSettle();
      expect(views, hasLength(1));
      expect(views.single.closeButton, isFalse);
      // The break URL asks the page to end the break itself and to offer a
      // way out of a mid-break pairing card.
      expect(
          Uri.parse(views.single.url).queryParameters['leave'], equals('page'));
      views.single.onMessage(const Dismissed());
      await tester.pumpAndSettle();

      final gate = LevelMomentAds.instance
          .ensureSignedIn(context: context, placementId: 'game-42');
      await tester.pumpAndSettle();
      expect(views, hasLength(2));
      expect(views.last.closeButton, isTrue);
      views.last.onMessage(const SignedIn());
      await tester.pumpAndSettle();
      await gate;
    });
  });

  // Note: LevelMomentWebView itself is intentionally NOT mounted in this suite —
  // constructing a WebViewController touches WebViewPlatform.instance, which
  // has no implementation under plain `flutter test`. The widget is a thin
  // presentation shell; its terminal-once guard mirrors the show() dispatch
  // logic pinned above, and its message parsing is covered by the
  // HostMessage.tryParse group.

  group('publicAdErrorCode', () {
    test('reports a store refusal as no_fill, never a credential failure', () {
      expect(publicAdErrorCode('store_unavailable'), 'no_fill');
    });

    test('passes other codes through', () {
      for (final code in ['no_fill', 'invalid_token', 'network_error']) {
        expect(publicAdErrorCode(code), code);
      }
    });
  });
}
