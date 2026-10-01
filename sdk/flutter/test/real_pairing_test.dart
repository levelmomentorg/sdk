// `UnsafeTesting.realPairing`: the URL allowlist, the resolved mode, the
// refusals, the secure-store slot per origin, and the guarantee that a real
// pairing run never touches the production credential.
//
// See docs/decisions/sdk-real-pairing-testing-2026-10-01.md. The URL vectors
// are the same table sdk/core/src/realPairing.test.ts pins.

import 'package:flutter/material.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:levelmoment_ads/levelmoment_ads.dart';
import 'package:levelmoment_ads/src/gate.dart';
import 'package:levelmoment_ads/src/hosted.dart';
import 'package:levelmoment_ads/src/widgets/level_moment_web_view.dart';

// Shared vectors: sdk/core and the Unity SDK pin the same table.
const allowed = [
  'http://localhost:3000/break',
  'http://127.0.0.1:3000/break',
  'http://LOCALHOST:3000/break',
];
const refused = [
  'https://levelmoment.com/break',
  'https://dev.levelmoment.com/break',
  'http://localhost/break',
  'http://localhost:80/break',
  'http://localhost:0/break',
  'https://localhost:3000/break',
  'http://localhost.:3000/break',
  'http://127.1:3000/break',
  'http://[::1]:3000/break',
  'http://10.0.2.2:3000/break',
  'http://192.168.1.20:3000/break',
  'http://user@localhost:3000/break',
  'http://localhost:3000/break?x=1',
  'http://localhost:3000/break#x',
  'http://localhost:3000/break?',
  'http://localhost:3000/break#',
  'http:\\\\localhost:3000/break',
  'http://local\thost:3000/break',
  'http://localhost.evil.example:3000/break',
  'capacitor://localhost/break',
];

const localBreak = 'http://localhost:3000/break';
const localOrigin = 'http://localhost:3000';
const placement = 'pl-1';
const productionKey = 'com.levelmoment.credential.$placement';
const testKey =
    'com.levelmoment.test-credential.http_localhost_3000.$placement';

ResolvedHosted resolveRealPairing({
  String? breakUrl = localBreak,
  String? token,
  String? apiUrl,
  String? topLevelApiUrl,
  String? topLevelBreakUrl,
  bool mock = false,
}) =>
    resolveHostedOptions(
      apiUrl: topLevelApiUrl,
      breakUrl: topLevelBreakUrl,
      mock: mock,
      unsafeTesting: UnsafeTesting(
        realPairing: true,
        breakUrl: breakUrl,
        token: token,
        apiUrl: apiUrl,
      ),
    );

Matcher throwsRealPairing(String fragment) => throwsA(isA<ArgumentError>()
    .having((e) => '${e.message}', 'message', contains('realPairing'))
    .having((e) => '${e.message}', 'message', contains(fragment)));

/// Collect what [debugPrint] prints while [body] runs.
Future<List<String>> capturePrints(Future<void> Function() body) async {
  final printed = <String>[];
  final saved = debugPrint;
  debugPrint = (String? message, {int? wrapWidth}) {
    if (message != null) printed.add(message);
  };
  try {
    await body();
  } finally {
    debugPrint = saved;
  }
  return printed;
}

void main() {
  group('real pairing URL vectors', () {
    for (final url in allowed) {
      test('allows $url', () => expect(isRealPairingUrl(url), isTrue));
    }
    for (final url in refused) {
      test('refuses $url', () => expect(isRealPairingUrl(url), isFalse));
    }
  });

  group('resolving realPairing', () {
    test('resolves to realPairing with no apiUrl, no token and no sandbox',
        () async {
      final hosted = resolveRealPairing();
      expect(hosted.mode, LevelMomentHostedMode.realPairing);
      expect(hosted.breakUrl, localBreak);
      expect(hosted.apiUrl, isNull);
      expect(hosted.token, isNull);
      expect(hosted.sandbox, isFalse);
      expect(hosted.usesCredentialStore, isTrue);
      expect(hosted.origin, localOrigin);
    });

    test("loads the re-serialised URL, not the caller's string", () {
      expect(
        resolveRealPairing(breakUrl: 'http://LOCALHOST:3000/break').breakUrl,
        localBreak,
      );
    });

    test('is refused outside a debug build, profile builds included', () {
      debugBuildOverride = false;
      addTearDown(() => debugBuildOverride = null);
      expect(
        () => resolveRealPairing(),
        throwsRealPairing('debug build'),
      );
      // The release refusal also stops initialize().
      expect(
        LevelMomentAds.instance.initialize(
          unsafeTesting:
              const UnsafeTesting(realPairing: true, breakUrl: localBreak),
        ),
        throwsA(isA<ArgumentError>()),
      );
    });

    test('the override cannot enable anything, only refuse', () {
      debugBuildOverride = true;
      addTearDown(() => debugBuildOverride = null);
      expect(resolveRealPairing().mode, LevelMomentHostedMode.realPairing);
    });

    test('refuses a token', () {
      expect(() => resolveRealPairing(token: 'eply_sbx_x'),
          throwsRealPairing('token'));
    });

    test('refuses an apiUrl', () {
      expect(() => resolveRealPairing(apiUrl: 'http://localhost:8080'),
          throwsRealPairing('apiUrl'));
    });

    test('refuses a top-level apiUrl', () {
      expect(
        () => resolveRealPairing(topLevelApiUrl: 'https://levelmoment.com/api'),
        throwsRealPairing('apiUrl'),
      );
    });

    test('refuses mock', () {
      expect(() => resolveRealPairing(mock: true), throwsRealPairing('mock'));
    });

    test('refuses a top-level breakUrl', () {
      expect(
        () => resolveRealPairing(topLevelBreakUrl: localBreak),
        throwsRealPairing('breakUrl'),
      );
    });

    test('refuses realPairing with no breakUrl', () {
      expect(
        () => resolveRealPairing(breakUrl: null),
        throwsRealPairing('breakUrl'),
      );
    });

    for (final url in refused) {
      test('refuses the break URL $url', () {
        expect(() => resolveRealPairing(breakUrl: url),
            throwsRealPairing('breakUrl'));
      });
    }

    test('initialize warns once per origin, naming it', () async {
      const breakUrl = 'http://127.0.0.1:4100/break';
      final printed = await capturePrints(() async {
        for (var i = 0; i < 2; i++) {
          await LevelMomentAds.instance.initialize(
            unsafeTesting:
                const UnsafeTesting(realPairing: true, breakUrl: breakUrl),
          );
        }
      });
      expect(printed, hasLength(1));
      expect(printed.single, contains('http://127.0.0.1:4100'));
      expect(LevelMomentAds.instance.mode, LevelMomentHostedMode.realPairing);
    });

    test('a refused initialize keeps the previous configuration', () async {
      await LevelMomentAds.instance.initialize();
      await expectLater(
        LevelMomentAds.instance.initialize(
          unsafeTesting: const UnsafeTesting(
            realPairing: true,
            breakUrl: 'https://levelmoment.com/break',
          ),
        ),
        throwsA(isA<ArgumentError>()),
      );
      expect(LevelMomentAds.instance.mode, LevelMomentHostedMode.production);
    });
  });

  group('mode and the hosted URLs', () {
    final production = resolveHostedOptions();
    final sandbox = resolveHostedOptions(
      unsafeTesting: const UnsafeTesting(breakUrl: localBreak),
    );
    final realPairing = resolveRealPairing();

    test('resolves the three modes', () {
      expect(production.mode, LevelMomentHostedMode.production);
      expect(sandbox.mode, LevelMomentHostedMode.sandbox);
      expect(realPairing.mode, LevelMomentHostedMode.realPairing);
    });

    test('adds sandbox=true only in sandbox mode', () {
      for (final (hosted, expected) in [
        (production, null),
        (sandbox, 'true'),
        (realPairing, null),
      ]) {
        final uri = Uri.parse(
          buildGateUrl(hosted: hosted, placementId: placement, mode: 'gate'),
        );
        expect(uri.queryParameters['sandbox'], expected,
            reason: '${hosted.mode}');
      }
    });

    test('the gate and access URLs carry no apiUrl under realPairing', () {
      for (final mode in ['gate', 'check', 'clear']) {
        for (final surface in HostedSurface.values) {
          final uri = Uri.parse(buildGateUrl(
            hosted: realPairing,
            placementId: placement,
            mode: mode,
            surface: surface,
          ));
          expect(uri.origin, localOrigin);
          expect(uri.queryParameters.containsKey('apiUrl'), isFalse);
          expect(uri.queryParameters.containsKey('sandbox'), isFalse);
        }
      }
      expect(
        Uri.parse(buildAccessUrl(
          hosted: realPairing,
          placementId: placement,
          mode: 'gate',
        )).path,
        '/access',
      );
    });

    test('the rewarded ad URL carries no apiUrl and no sandbox', () async {
      await LevelMomentAds.instance.initialize(
        unsafeTesting:
            const UnsafeTesting(realPairing: true, breakUrl: localBreak),
      );
      late LevelMomentRewardedAd ad;
      await LevelMomentRewardedAd.load(
        placementId: placement,
        adLoadCallback: LevelMomentAdLoadCallback(
          onAdLoaded: (a) => ad = a,
          onAdFailedToLoad: (e) => fail('should load: $e'),
        ),
      );
      final uri = Uri.parse(ad.buildUrl());
      expect(uri.origin + uri.path, localBreak);
      expect(uri.queryParameters.containsKey('apiUrl'), isFalse);
      expect(uri.queryParameters.containsKey('sandbox'), isFalse);
      expect(uri.queryParameters.containsKey('mock'), isFalse);
      expect(LevelMomentAds.instance.apiUrl, isNull);
    });
  });

  group('per-call tokens under realPairing', () {
    setUp(() async {
      await LevelMomentAds.instance.initialize(
        unsafeTesting:
            const UnsafeTesting(realPairing: true, breakUrl: localBreak),
      );
    });

    test('resolveStudentToken refuses any token and allows none', () {
      expect(
        () => LevelMomentAds.instance.resolveStudentToken('eply_sbx_x'),
        throwsA(isA<ArgumentError>()),
      );
      expect(LevelMomentAds.instance.resolveStudentToken(null), isNull);
      expect(LevelMomentAds.instance.resolveStudentToken(''), isNull);
    });

    test('load() fails with invalid_request for a token', () async {
      LevelMomentAdError? error;
      await LevelMomentRewardedAd.load(
        placementId: placement,
        studentToken: 'eply_sbx_x',
        adLoadCallback: LevelMomentAdLoadCallback(
          onAdLoaded: (_) => fail('a token must be refused'),
          onAdFailedToLoad: (e) => error = e,
        ),
      );
      expect(error?.code, 'invalid_request');
    });

    testWidgets('the gate and checks refuse a token at the call',
        (tester) async {
      await tester.pumpWidget(const MaterialApp(home: SizedBox()));
      final context = tester.element(find.byType(SizedBox));
      final ads = LevelMomentAds.instance;
      expect(
        await ads.ensureSignedIn(
            context: context, placementId: placement, studentToken: 'tok'),
        EnsureSignedInResult.technicalFailure,
      );
      expect(
        await ads.ensureAccess(
            context: context, placementId: placement, studentToken: 'tok'),
        EnsureSignedInResult.technicalFailure,
      );
      await expectLater(
        ads.isSignedIn(
            context: context, placementId: placement, studentToken: 'tok'),
        throwsA(isA<LevelMomentSignInCheckError>()),
      );
      await expectLater(
        ads.checkAccess(
            context: context, placementId: placement, studentToken: 'tok'),
        throwsA(isA<LevelMomentSignInCheckError>()),
      );
    });
  });

  group('credentialKey', () {
    test('keeps the production key unchanged', () {
      expect(credentialKey('https://levelmoment.com', 'pl_abc'),
          'com.levelmoment.credential.pl_abc');
    });

    test('puts any other origin under the test prefix', () {
      expect(credentialKey('http://localhost:3000', 'pl_abc'),
          'com.levelmoment.test-credential.http_localhost_3000.pl_abc');
      expect(credentialKey('http://127.0.0.1:3000', 'pl_abc'),
          'com.levelmoment.test-credential.http_127.0.0.1_3000.pl_abc');
    });

    test('cannot be made to produce a production key from a test origin', () {
      expect(
        credentialKey('http://localhost:3000', 'x')
            .startsWith('com.levelmoment.credential.'),
        isFalse,
      );
    });
  });

  group('the production credential under realPairing', () {
    late _RecordingStorage storage;
    late LevelMomentTokenStore savedStore;
    final views = <LevelMomentWebView>[];

    setUp(() {
      storage = _RecordingStorage({productionKey: 'SENTINEL'});
      savedStore = deviceCredentials;
      deviceCredentials = LevelMomentTokenStore(storage);
      views.clear();
      debugHostedSurfaceOverride = (view) {
        views.add(view);
        return const SizedBox.shrink();
      };
    });

    tearDown(() {
      deviceCredentials = savedStore;
      debugHostedSurfaceOverride = null;
    });

    /// Answer the page's credential request, then deliver what pairing
    /// issues, what the server refuses, and [terminal], as the page would.
    Future<void> drive(LevelMomentWebView view, HostMessage terminal) async {
      final ask = view.onNeedCredential;
      if (ask != null) {
        final reply = await ask();
        expect(reply.origin, Uri.parse(view.url).origin);
      }
      view.onMessage(const CredentialIssued('test-token'));
      view.onMessage(const CredentialInvalid());
      view.onMessage(terminal);
    }

    Future<BuildContext> mountApp(WidgetTester tester) async {
      await tester.pumpWidget(const MaterialApp(home: SizedBox()));
      return tester.element(find.byType(SizedBox));
    }

    testWidgets('is never read, written or deleted by any entry point',
        (tester) async {
      await LevelMomentAds.instance.initialize(
        unsafeTesting:
            const UnsafeTesting(realPairing: true, breakUrl: localBreak),
      );
      final ads = LevelMomentAds.instance;
      final context = await mountApp(tester);

      Future<void> run(
          Future<Object?> Function() start, HostMessage terminal) async {
        final before = views.length;
        final done = start();
        await tester.pump();
        await tester.pump();
        expect(views.length, before + 1, reason: 'a hosted surface mounted');
        final view = views.last;
        expect(Uri.parse(view.url).origin, localOrigin);
        expect(
            Uri.parse(view.url).queryParameters.containsKey('apiUrl'), isFalse);
        await drive(view, terminal);
        await tester.pump();
        await done;
      }

      await run(
        () => ads.ensureSignedIn(context: context, placementId: placement),
        const SignedIn(),
      );
      await run(
        () => ads.ensureAccess(context: context, placementId: placement),
        const SignedIn(),
      );
      await run(
        () => ads.isSignedIn(context: context, placementId: placement),
        const SignedIn(),
      );
      await run(
        () => ads.checkAccess(context: context, placementId: placement),
        const SignedIn(),
      );
      await run(
        () => ads.signOut(context: context, placementId: placement),
        const Dismissed(),
      );

      late LevelMomentRewardedAd ad;
      await LevelMomentRewardedAd.load(
        placementId: placement,
        adLoadCallback: LevelMomentAdLoadCallback(
          onAdLoaded: (a) => ad = a,
          onAdFailedToLoad: (e) => fail('should load: $e'),
        ),
      );
      var dismissed = false;
      ad.fullScreenContentCallback = LevelMomentFullScreenContentCallback(
        onAdDismissedFullScreenContent: (_) => dismissed = true,
      );
      await run(() async {
        ad.show(context: context, onUserEarnedReward: (_, __) {});
        return null;
      }, const Dismissed());
      expect(dismissed, isTrue);

      expect(views, hasLength(6));
      expect(storage.values[productionKey], 'SENTINEL');
      expect(
        storage.log.where((entry) => entry.endsWith(productionKey)),
        isEmpty,
        reason: storage.log.join('\n'),
      );
      expect(
        storage.log
            .where((entry) => entry.contains('com.levelmoment.credential.')),
        isEmpty,
      );
      // And the test slot was really used: read by the five surfaces that ask
      // for a credential, written and deleted by every surface.
      expect(storage.log, contains('read $testKey'));
      expect(storage.log, contains('write $testKey'));
      expect(storage.log, contains('delete $testKey'));
      expect(storage.log.where((e) => e == 'read $testKey'), hasLength(5));
    });

    testWidgets('the same harness does reach the production slot in production',
        (tester) async {
      // Guards the test above against passing vacuously: in production mode
      // the same entry point reads the production key.
      await LevelMomentAds.instance.initialize();
      final context = await mountApp(tester);
      final done = LevelMomentAds.instance
          .ensureSignedIn(context: context, placementId: placement);
      await tester.pump();
      await tester.pump();
      final reply = await views.single.onNeedCredential!();
      expect(reply.token, 'SENTINEL');
      expect(reply.origin, 'https://levelmoment.com');
      views.single.onMessage(const SignedIn());
      await tester.pump();
      expect(await done, EnsureSignedInResult.ready);
      expect(storage.log, contains('read $productionKey'));
    });

    testWidgets('sandbox mode does not touch the store at all', (tester) async {
      await LevelMomentAds.instance.initialize(
        unsafeTesting: const UnsafeTesting(breakUrl: localBreak),
      );
      final context = await mountApp(tester);
      final done = LevelMomentAds.instance
          .ensureSignedIn(context: context, placementId: placement);
      await tester.pump();
      await tester.pump();
      await drive(views.single, const SignedIn());
      await tester.pump();
      expect(await done, EnsureSignedInResult.ready);
      expect(
          storage.log.where((e) => !e.contains('availability-probe')), isEmpty);
    });
  });
}

/// An in-memory secure store that records every key it is asked about.
class _RecordingStorage extends FlutterSecureStorage {
  _RecordingStorage(Map<String, String> initial) : values = {...initial};

  final Map<String, String> values;
  final List<String> log = [];

  @override
  Future<String?> read({
    required String key,
    IOSOptions? iOptions,
    AndroidOptions? aOptions,
    LinuxOptions? lOptions,
    WebOptions? webOptions,
    MacOsOptions? mOptions,
    WindowsOptions? wOptions,
  }) async {
    log.add('read $key');
    return values[key];
  }

  @override
  Future<void> write({
    required String key,
    required String? value,
    IOSOptions? iOptions,
    AndroidOptions? aOptions,
    LinuxOptions? lOptions,
    WebOptions? webOptions,
    MacOsOptions? mOptions,
    WindowsOptions? wOptions,
  }) async {
    log.add('write $key');
    if (value == null) {
      values.remove(key);
    } else {
      values[key] = value;
    }
  }

  @override
  Future<void> delete({
    required String key,
    IOSOptions? iOptions,
    AndroidOptions? aOptions,
    LinuxOptions? lOptions,
    WebOptions? webOptions,
    MacOsOptions? mOptions,
    WindowsOptions? wOptions,
  }) async {
    log.add('delete $key');
    values.remove(key);
  }
}
