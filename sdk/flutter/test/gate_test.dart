// Pure-Dart unit tests for the startup sign-in gate. No WebView platform
// channel is exercised — URL building, the message mapping, the terminal-once
// dispatcher, and mock mode are all platform-free.
//
// Mirrors sdk/web/src/gate.test.ts and sdk/react-native/src/gate.test.ts.

import 'package:flutter/material.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:levelmoment_ads/levelmoment_ads.dart';
import 'package:levelmoment_ads/src/gate.dart';
import 'package:levelmoment_ads/src/widgets/level_moment_web_view.dart';

void main() {
  group('buildGateUrl', () {
    test('live mode carries mode, placement and apiUrl — never a token', () {
      final uri = Uri.parse(buildGateUrl(
        breakUrl: 'https://app.levelmoment.com/break',
        placementId: 'game-42',
        mode: 'gate',
        apiUrl: 'https://api.levelmoment.com',
      ));

      expect(uri.origin + uri.path, 'https://app.levelmoment.com/break');
      expect(uri.queryParameters['mode'], 'gate');
      expect(uri.queryParameters['placementId'], 'game-42');
      expect(uri.queryParameters['apiUrl'], 'https://api.levelmoment.com');
      // The credential never rides on the hosted URL — the page asks for it
      // over the postMessage bridge instead (see credential_bridge.dart).
      expect(uri.queryParameters.containsKey('token'), isFalse);
      expect(uri.queryParameters.containsKey('mock'), isFalse);
    });

    test('uses mode=check for the headless check', () {
      final uri = Uri.parse(buildGateUrl(
        breakUrl: 'https://app.levelmoment.com/break',
        placementId: 'g',
        mode: 'check',
      ));
      expect(uri.queryParameters['mode'], 'check');
    });

    test('mock mode omits apiUrl and token', () {
      final uri = Uri.parse(buildGateUrl(
        breakUrl: 'https://app.levelmoment.com/break',
        placementId: 'g',
        mode: 'gate',
        apiUrl: 'https://api.levelmoment.com',
        mock: true,
      ));
      expect(uri.queryParameters['mock'], 'true');
      expect(uri.queryParameters.containsKey('apiUrl'), isFalse);
      expect(uri.queryParameters.containsKey('token'), isFalse);
    });

    test(
        'announces openExternal support — an old shell that cannot open a '
        'browser must not be offered the button', () {
      final uri = Uri.parse(buildGateUrl(
        breakUrl: 'https://app.levelmoment.com/break',
        placementId: 'game-42',
        mode: 'gate',
      ));
      expect(uri.queryParameters['caps'], 'openExternal');
    });

    test('appends with & when breakUrl already has a query', () {
      final url = buildGateUrl(
        breakUrl: 'https://app.levelmoment.com/break?theme=dark',
        placementId: 'g',
        mode: 'gate',
      );
      expect(
        url.startsWith('https://app.levelmoment.com/break?theme=dark&'),
        isTrue,
      );
      expect(Uri.parse(url).queryParameters['theme'], 'dark');
    });
  });

  group('buildAccessUrl', () {
    test('uses the canonical access path for production', () {
      final uri = Uri.parse(buildAccessUrl(
        breakUrl: 'https://levelmoment.com/break',
        placementId: 'game-42',
        mode: 'check',
      ));

      expect(uri.origin + uri.path, 'https://levelmoment.com/access');
      expect(uri.queryParameters['mode'], 'check');
      expect(uri.queryParameters['protocolVersion'], '1');
      expect(uri.queryParameters['sdkVersion'], '0.2.0');
    });

    test('keeps an unsafe test origin while changing only the path', () {
      final uri = Uri.parse(buildAccessUrl(
        breakUrl: 'http://localhost:3000/break',
        placementId: 'game-42',
        mode: 'gate',
        apiUrl: 'http://localhost:3000/api',
        unsafeTesting: true,
      ));

      expect(uri.origin + uri.path, 'http://localhost:3000/access');
      expect(uri.queryParameters['mode'], 'gate');
      expect(uri.queryParameters['sandbox'], 'true');
      expect(uri.queryParameters['apiUrl'], 'http://localhost:3000/api');
    });
  });

  group('HostMessage.tryParse', () {
    test('parses the gate verdict', () {
      expect(HostMessage.tryParse('{"type":"signedIn"}'), isA<SignedIn>());
    });
  });

  // The credential trio moved here rather than into gate_test's sibling
  // (rewarded_ad_test.dart) because both callers of tryParse share it —
  // pinning it once next to the message-type doc comment in
  // level_moment_web_view.dart is enough; see credential_bridge_test.dart for
  // what the two credential functions built on top of it do.
  group('HostMessage.tryParse — credential messages', () {
    test('needCredential asks this host to answer', () {
      expect(
        HostMessage.tryParse('{"type":"needCredential"}'),
        isA<NeedCredential>(),
      );
    });

    test('credentialIssued extracts the minted token', () {
      final parsed = HostMessage.tryParse(
        '{"type":"credentialIssued","payload":{"token":"tok-123"}}',
      );
      expect(parsed, isA<CredentialIssued>());
      expect((parsed as CredentialIssued).token, 'tok-123');
    });

    test('credentialInvalid carries no payload', () {
      expect(
        HostMessage.tryParse('{"type":"credentialInvalid"}'),
        isA<CredentialInvalid>(),
      );
    });

    const hosted = 'https://app.levelmoment.com/break?placementId=game-42';

    test('openExternal carries the approval URL', () {
      final parsed = HostMessage.tryParse(
        '{"type":"openExternal","payload":{"url":"https://app.levelmoment.com/link?code=AB12CD"}}',
      );
      expect(parsed, isA<OpenExternal>());
      expect(
        (parsed as OpenExternal).launchableFrom(hosted).toString(),
        'https://app.levelmoment.com/link?code=AB12CD',
      );
    });

    test('openExternal without a url is dropped', () {
      expect(HostMessage.tryParse('{"type":"openExternal"}'), isNull);
      expect(
        HostMessage.tryParse('{"type":"openExternal","payload":{"url":7}}'),
        isNull,
      );
    });

    test('openExternal only launches the hosted page own origin', () {
      // The bridge belongs to whatever the WebView is showing. A page that took
      // it somewhere else could otherwise full-screen a password prompt in the
      // device's own browser.
      for (final url in [
        'https://evil.example.com/link',
        'https://app.levelmoment.com.evil.example/link',
        'https://levelmoment.com/link',
        'https://app.levelmoment.com:8443/link',
        // Right host, wrong scheme.
        'http://app.levelmoment.com/link',
        // launchUrl hands a custom scheme to whichever app claims it.
        'javascript:alert(1)',
        'someapp://pay?amount=100',
        'file:///etc/passwd',
        'not a url',
        '',
      ]) {
        expect(OpenExternal(url).launchableFrom(hosted), isNull, reason: url);
      }
    });

    test('openExternal allows http when breakUrl is itself http', () {
      const localHosted = 'http://localhost:3000/break';
      expect(
        const OpenExternal('http://localhost:3000/link')
            .launchableFrom(localHosted)
            .toString(),
        'http://localhost:3000/link',
      );
      expect(
        const OpenExternal('http://localhost:4000/link')
            .launchableFrom(localHosted),
        isNull,
      );
    });

    test('credentialIssued with a missing payload is dropped', () {
      final parsed = HostMessage.tryParse('{"type":"credentialIssued"}');
      expect(parsed, isNull);
    });

    test('credentialIssued with a malformed payload is dropped', () {
      // payload is a string, not a map; the wrong-shaped `token` field
      // (a number instead of a string) is the same failure mode.
      final wrongShapePayload = HostMessage.tryParse(
        '{"type":"credentialIssued","payload":"not-a-map"}',
      );
      expect(wrongShapePayload, isNull);

      final wrongShapeToken = HostMessage.tryParse(
        '{"type":"credentialIssued","payload":{"token":42}}',
      );
      expect(wrongShapeToken, isNull);
    });
  });

  group('SignInDispatcher', () {
    ({List<String> log, SignInDispatcher dispatcher}) make() {
      final log = <String>[];
      return (
        log: log,
        dispatcher: SignInDispatcher(
          onReady: () => log.add('ready'),
          onCanceled: () => log.add('canceled'),
          onFailure: (code) => log.add('failure:$code'),
        ),
      );
    }

    test('signedIn is the yes', () {
      final t = make();
      expect(t.dispatcher.handle(const SignedIn()), isTrue);
      expect(t.log, ['ready']);
    });

    test('dismissed is the no — a closed gate and a denial arrive alike', () {
      final t = make();
      expect(t.dispatcher.handle(const Dismissed()), isTrue);
      expect(t.log, ['canceled']);
    });

    test('error carries its code to the failure path', () {
      final t = make();
      expect(t.dispatcher.handle(const ErrorMsg('network_error', 'offline')),
          isTrue);
      expect(t.log, ['failure:network_error']);
    });

    test('ready and earnedReward end nothing', () {
      final t = make();
      expect(t.dispatcher.handle(const Ready()), isFalse);
      expect(t.dispatcher.handle(const EarnedReward(1)), isFalse);
      expect(t.log, isEmpty);
      expect(t.dispatcher.isSettled, isFalse);
    });

    test('settles exactly once — later terminals are dropped', () {
      final t = make();
      t.dispatcher.handle(const SignedIn());
      t.dispatcher.handle(const Dismissed());
      t.dispatcher.handle(const ErrorMsg('late', 'ignored'));
      expect(t.dispatcher.fail('load_timeout'), isFalse);
      expect(t.log, ['ready']);
    });

    test('fail() ends a gate the page never answered', () {
      final t = make();
      expect(t.dispatcher.fail('load_timeout'), isTrue);
      expect(t.log, ['failure:load_timeout']);
      expect(t.dispatcher.handle(const SignedIn()), isFalse);
      expect(t.log, ['failure:load_timeout']);
    });
  });

  group('the headless check deadline', () {
    ({List<String> log, SignInDispatcher dispatcher}) make() {
      final log = <String>[];
      return (
        log: log,
        dispatcher: SignInDispatcher(
          onReady: () => log.add('ready'),
          onCanceled: () => log.add('canceled'),
          onFailure: (code) => log.add('failure:$code'),
        ),
      );
    }

    test('ends a check that loaded and then said nothing', () async {
      final t = make();
      t.dispatcher
          .armDeadline(const Duration(milliseconds: 5), 'check_timeout');
      t.dispatcher.handle(const Ready()); // ends nothing; cancels no deadline

      await Future<void>.delayed(const Duration(milliseconds: 40));

      expect(t.log, ['failure:check_timeout']);
      expect(t.dispatcher.isSettled, isTrue);
    });

    test('a verdict before the deadline cancels it', () async {
      final t = make();
      t.dispatcher
          .armDeadline(const Duration(milliseconds: 5), 'check_timeout');
      t.dispatcher.handle(const SignedIn());

      await Future<void>.delayed(const Duration(milliseconds: 40));

      expect(t.log, ['ready']);
    });

    test('an unarmed gate waits forever — pairing has no deadline', () async {
      // ensureSignedIn never calls armDeadline. Silence after `ready` is a
      // parent walking to another room, not a failure.
      final t = make();
      t.dispatcher.handle(const Ready());

      await Future<void>.delayed(const Duration(milliseconds: 40));

      expect(t.log, isEmpty);
      expect(t.dispatcher.isSettled, isFalse);
    });

    test('arming after the gate settled does nothing', () async {
      final t = make();
      t.dispatcher.handle(const SignedIn());
      t.dispatcher
          .armDeadline(const Duration(milliseconds: 5), 'check_timeout');

      await Future<void>.delayed(const Duration(milliseconds: 40));

      expect(t.log, ['ready']);
    });

    test('the default budget clears the load window and the page budget', () {
      expect(kSignInCheckTimeout, greaterThan(kBreakLoadTimeout));
    });
  });

  group('mock mode', () {
    testWidgets('ensureSignedIn is ready without mounting anything',
        (tester) async {
      await LevelMomentAds.instance.initialize(
        unsafeTesting: const UnsafeTesting(
          apiUrl: 'https://api.levelmoment.com',
          breakUrl: 'https://app.levelmoment.com/break',
        ),
        mock: true,
      );

      EnsureSignedInResult? result;
      await tester.pumpWidget(MaterialApp(
        home: Builder(
          builder: (context) => ElevatedButton(
            onPressed: () async {
              result = await LevelMomentAds.instance.ensureSignedIn(
                context: context,
                placementId: 'g',
              );
            },
            child: const Text('gate'),
          ),
        ),
      ));
      await tester.tap(find.text('gate'));
      await tester.pump();

      expect(result, EnsureSignedInResult.ready);
      // Nothing was pushed: the button is still the only thing on screen.
      expect(find.text('gate'), findsOneWidget);
    });

    testWidgets('isSignedIn is true without mounting anything', (tester) async {
      await LevelMomentAds.instance.initialize(
        unsafeTesting: const UnsafeTesting(
          apiUrl: 'https://api.levelmoment.com',
          breakUrl: 'https://app.levelmoment.com/break',
        ),
        mock: true,
      );

      bool? result;
      await tester.pumpWidget(MaterialApp(
        home: Builder(
          builder: (context) => ElevatedButton(
            onPressed: () async {
              result = await LevelMomentAds.instance.isSignedIn(
                context: context,
                placementId: 'g',
              );
            },
            child: const Text('check'),
          ),
        ),
      ));
      await tester.tap(find.text('check'));
      await tester.pump();

      expect(result, isTrue);
    });

    testWidgets('access wrappers preserve identity outcome semantics',
        (tester) async {
      await LevelMomentAds.instance.initialize(mock: true);
      EnsureSignedInResult? gateResult;
      bool? checkResult;
      await tester.pumpWidget(MaterialApp(
        home: Builder(
          builder: (context) => ElevatedButton(
            onPressed: () async {
              gateResult = await LevelMomentAds.instance.ensureAccess(
                context: context,
                placementId: 'g',
              );
              checkResult = await LevelMomentAds.instance.checkAccess(
                context: context,
                placementId: 'g',
              );
            },
            child: const Text('access'),
          ),
        ),
      ));
      await tester.tap(find.text('access'));
      await tester.pump();

      expect(gateResult, EnsureSignedInResult.ready);
      expect(checkResult, isTrue);
    });

    testWidgets('signOut completes without clearing either store',
        (tester) async {
      await LevelMomentAds.instance.initialize(
        unsafeTesting: const UnsafeTesting(
          apiUrl: 'https://api.levelmoment.com',
          breakUrl: 'https://app.levelmoment.com/break',
        ),
        mock: true,
      );
      FlutterSecureStorage.setMockInitialValues({});
      final store = LevelMomentTokenStore();
      await store.set('g', 'stored-token');

      var done = false;
      await tester.pumpWidget(MaterialApp(
        home: Builder(
          builder: (context) => ElevatedButton(
            onPressed: () async {
              await LevelMomentAds.instance
                  .signOut(context: context, placementId: 'g');
              done = true;
            },
            child: const Text('out'),
          ),
        ),
      ));
      await tester.tap(find.text('out'));
      await tester.pump();

      expect(done, isTrue);
      // A mock run has no real household to sign out of, so it must not touch
      // a store a developer is using for an offline demo.
      expect(await store.get('g'), 'stored-token');
    });
  });

  group('buildGateUrl for sign-out', () {
    test('uses mode=clear and carries no credential', () {
      final uri = Uri.parse(buildGateUrl(
        breakUrl: 'https://app.levelmoment.com/break',
        placementId: 'game-42',
        mode: 'clear',
        apiUrl: 'https://api.levelmoment.com',
      ));

      expect(uri.queryParameters['mode'], 'clear');
      expect(uri.queryParameters['placementId'], 'game-42');
      expect(uri.queryParameters.containsKey('token'), isFalse);
    });
  });

  group('signOut without a navigator', () {
    testWidgets('throws rather than reporting a sign-out that did not happen',
        (tester) async {
      // The secure store is emptied before the hosted surface is opened. If
      // that surface cannot mount, the hosted copy survives and the household
      // is still signed in — completing quietly would claim otherwise.
      await LevelMomentAds.instance.initialize(
        unsafeTesting: const UnsafeTesting(
          apiUrl: 'https://api.levelmoment.com',
          breakUrl: 'https://app.levelmoment.com/break',
        ),
      );
      FlutterSecureStorage.setMockInitialValues({});

      await tester.pumpWidget(
        Directionality(
          textDirection: TextDirection.ltr,
          child: Builder(builder: (_) => const SizedBox.shrink()),
        ),
      );

      final context = tester.element(find.byType(SizedBox));
      await expectLater(
        LevelMomentAds.instance.signOut(context: context, placementId: 'g'),
        throwsA(isA<LevelMomentSignInCheckError>()),
      );
    });
  });

  group('a context with no navigator', () {
    testWidgets('ensureSignedIn reports technicalFailure instead of throwing',
        (tester) async {
      await LevelMomentAds.instance.initialize(
        unsafeTesting: const UnsafeTesting(
          apiUrl: 'https://api.levelmoment.com',
          breakUrl: 'https://app.levelmoment.com/break',
        ),
      );

      EnsureSignedInResult? result;
      await tester.pumpWidget(
        Directionality(
          textDirection: TextDirection.ltr,
          child: Builder(
            builder: (context) {
              result = null;
              return const SizedBox.shrink();
            },
          ),
        ),
      );

      final context = tester.element(find.byType(SizedBox));
      result = await LevelMomentAds.instance.ensureSignedIn(
        context: context,
        placementId: 'g',
      );
      expect(result, EnsureSignedInResult.technicalFailure);
    });

    testWidgets('isSignedIn throws rather than answering false',
        (tester) async {
      await LevelMomentAds.instance.initialize(
        unsafeTesting: const UnsafeTesting(
          apiUrl: 'https://api.levelmoment.com',
          breakUrl: 'https://app.levelmoment.com/break',
        ),
      );

      await tester.pumpWidget(
        Directionality(
          textDirection: TextDirection.ltr,
          child: Builder(builder: (_) => const SizedBox.shrink()),
        ),
      );

      final context = tester.element(find.byType(SizedBox));
      await expectLater(
        LevelMomentAds.instance.isSignedIn(context: context, placementId: 'g'),
        throwsA(isA<LevelMomentSignInCheckError>()),
      );
    });
  });

  // Note: LevelMomentWebView itself is intentionally NOT mounted in this suite —
  // constructing a WebViewController touches WebViewPlatform.instance, which has
  // no implementation under plain `flutter test`. The widget is a thin
  // presentation shell; the gate's message mapping and terminal-once discipline
  // are pinned by the SignInDispatcher group above.
}
