// Require-internet mode: the connection monitor, the no-connection message,
// and the offline short-circuit on every hosted surface.

import 'dart:async';

import 'package:connectivity_plus/connectivity_plus.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:levelmoment_ads/levelmoment_ads.dart';
import 'package:levelmoment_ads/src/connection.dart';
import 'package:levelmoment_ads/src/widgets/level_moment_web_view.dart';

/// A stand-in for the OS: [results] is what a check returns, and [emit] is a
/// change the OS reports.
class FakeOs {
  List<ConnectivityResult> results = [ConnectivityResult.wifi];
  final StreamController<List<ConnectivityResult>> _changes =
      StreamController<List<ConnectivityResult>>.broadcast();

  void install() {
    final connection = LevelMomentConnection.instance;
    connection.checkConnectivity = () async => results;
    connection.connectivityChanges = () => _changes.stream;
  }

  void emit(List<ConnectivityResult> next) {
    results = next;
    _changes.add(next);
  }
}

/// Deliver a connection change: one pump runs the stream event and its
/// setState, the next builds the result.
Future<void> settle(WidgetTester tester) async {
  await tester.pump();
  await tester.pump();
}

const offline = [ConnectivityResult.none];
const wifi = [ConnectivityResult.wifi];

void main() {
  late FakeOs os;

  setUp(() async {
    await LevelMomentConnection.instance.debugReset();
    os = FakeOs()..install();
  });

  tearDown(() async {
    debugHostedSurfaceOverride = null;
    await LevelMomentConnection.instance.debugReset();
  });

  group('connectivityMeansOffline', () {
    test('only an all-none report is offline', () {
      expect(connectivityMeansOffline(offline), isTrue);
      expect(connectivityMeansOffline(wifi), isFalse);
      expect(
        connectivityMeansOffline(
            [ConnectivityResult.none, ConnectivityResult.mobile]),
        isFalse,
      );
      // An empty report says nothing, and nothing never locks a game.
      expect(connectivityMeansOffline(const []), isFalse);
    });
  });

  group('LevelMomentConnection', () {
    test('follows OS changes and emits only on a change', () async {
      final seen = <bool>[];
      final sub = LevelMomentConnection.instance.changes.listen(seen.add);
      LevelMomentConnection.instance.start();
      await Future<void>.delayed(Duration.zero);
      expect(LevelMomentConnection.instance.isOnline, isTrue);

      os.emit(offline);
      os.emit(offline);
      await Future<void>.delayed(Duration.zero);
      expect(LevelMomentConnection.instance.isOnline, isFalse);

      os.emit(wifi);
      await Future<void>.delayed(Duration.zero);
      expect(seen, [false, true]);
      await sub.cancel();
    });

    test('reads offline at start when the OS already reports it', () async {
      os.results = offline;
      LevelMomentConnection.instance.start();
      await Future<void>.delayed(Duration.zero);
      expect(LevelMomentConnection.instance.isOnline, isFalse);
    });

    test('a plugin that throws reads as online', () async {
      final connection = LevelMomentConnection.instance;
      connection.checkConnectivity = () async => throw Exception('no plugin');
      connection.connectivityChanges = () => throw Exception('no plugin');
      LevelMomentConnection.instance.start();
      expect(await LevelMomentConnection.instance.refresh(), isTrue);
    });
  });

  group('LevelMomentConnectionGate', () {
    Widget app({ValueChanged<bool>? onChange, String? gameName}) => MaterialApp(
          builder: (context, child) => LevelMomentConnectionGate(
            gameName: gameName,
            onConnectionChange: onChange,
            child: child!,
          ),
          home: const Scaffold(body: Text('game')),
        );

    testWidgets('shows the message offline and removes it online',
        (tester) async {
      final changes = <bool>[];
      await tester.pumpWidget(app(onChange: changes.add, gameName: 'Hue Hunt'));
      await tester.pump();
      expect(find.text('No internet connection'), findsNothing);

      os.emit(offline);
      await settle(tester);
      expect(find.text('No internet connection'), findsOneWidget);
      expect(
        find.text('Hue Hunt needs an internet connection. '
            'Turn on Wi-Fi or mobile data, then try again.'),
        findsOneWidget,
      );
      expect(find.text('Try again'), findsOneWidget);
      expect(changes, [false]);

      os.emit(wifi);
      await settle(tester);
      expect(find.text('No internet connection'), findsNothing);
      expect(changes, [false, true]);
    });

    testWidgets('blocks taps on the game underneath', (tester) async {
      var taps = 0;
      await tester.pumpWidget(MaterialApp(
        builder: (context, child) => LevelMomentConnectionGate(child: child!),
        home: Scaffold(
          body: Center(
            child: ElevatedButton(
              onPressed: () => taps++,
              child: const Text('jump'),
            ),
          ),
        ),
      ));
      os.emit(offline);
      await settle(tester);
      await tester.tap(find.text('jump'), warnIfMissed: false);
      expect(taps, 0);
    });

    testWidgets('Try again keeps the message while offline, hides it online',
        (tester) async {
      await tester.pumpWidget(app());
      os.emit(offline);
      await settle(tester);

      await tester.tap(find.text('Try again'));
      await tester.pump();
      await tester.pump();
      expect(find.text('No internet connection'), findsOneWidget);

      // The connection came back without the OS reporting a change.
      os.results = wifi;
      await tester.tap(find.text('Try again'));
      await tester.pump();
      await tester.pump();
      expect(find.text('No internet connection'), findsNothing);
    });

    testWidgets('falls back to This game, and reports a start offline',
        (tester) async {
      os.results = offline;
      LevelMomentConnection.instance.debugSetOnline(false);
      final changes = <bool>[];
      await tester.pumpWidget(app(onChange: changes.add));
      await tester.pump();
      expect(
        find.text('This game needs an internet connection. '
            'Turn on Wi-Fi or mobile data, then try again.'),
        findsOneWidget,
      );
      expect(changes, [false]);
    });

    testWidgets('stops listening when removed', (tester) async {
      final changes = <bool>[];
      await tester.pumpWidget(app(onChange: changes.add));
      await tester.pumpWidget(const SizedBox());
      os.emit(offline);
      await settle(tester);
      expect(changes, isEmpty);
    });
  });

  group('offline short-circuit', () {
    final views = <LevelMomentWebView>[];

    setUp(() {
      views.clear();
      debugHostedSurfaceOverride = (view) {
        views.add(view);
        return const SizedBox.shrink();
      };
    });

    Future<BuildContext> boot(WidgetTester tester) async {
      await LevelMomentAds.instance.initialize(
        unsafeTesting: const UnsafeTesting(
          apiUrl: 'https://api.levelmoment.com',
          breakUrl: 'https://app.levelmoment.com/break',
          token: 'eply_sbx_test',
        ),
      );
      await tester.pumpWidget(const MaterialApp(home: SizedBox()));
      os.emit(offline);
      await settle(tester);
      expect(LevelMomentConnection.instance.isOnline, isFalse);
      return tester.element(find.byType(SizedBox));
    }

    testWidgets('ensureSignedIn and ensureAccess fail without mounting',
        (tester) async {
      final context = await boot(tester);
      expect(
        await LevelMomentAds.instance
            .ensureSignedIn(context: context, placementId: 'g'),
        EnsureSignedInResult.technicalFailure,
      );
      expect(
        await LevelMomentAds.instance
            .ensureAccess(context: context, placementId: 'g'),
        EnsureSignedInResult.technicalFailure,
      );
      await tester.pumpAndSettle();
      expect(views, isEmpty);
    });

    testWidgets('isSignedIn and checkAccess throw rather than answer false',
        (tester) async {
      final context = await boot(tester);
      await expectLater(
        LevelMomentAds.instance.isSignedIn(context: context, placementId: 'g'),
        throwsA(isA<LevelMomentSignInCheckError>()
            .having((e) => e.code, 'code', 'offline')),
      );
      await expectLater(
        LevelMomentAds.instance.checkAccess(context: context, placementId: 'g'),
        throwsA(isA<LevelMomentSignInCheckError>()),
      );
      await tester.pumpAndSettle();
      expect(views, isEmpty);
    });

    testWidgets('a break reports network_error without pushing a route',
        (tester) async {
      final context = await boot(tester);
      late LevelMomentRewardedAd ad;
      await LevelMomentRewardedAd.load(
        placementId: 'g',
        adLoadCallback: LevelMomentAdLoadCallback(
          onAdLoaded: (a) => ad = a,
          onAdFailedToLoad: (e) => fail('should load: $e'),
        ),
      );
      LevelMomentAdError? error;
      var dismissed = 0;
      ad.fullScreenContentCallback = LevelMomentFullScreenContentCallback(
        onAdFailedToShowFullScreenContent: (_, e) => error = e,
        onAdDismissedFullScreenContent: (_) => dismissed++,
      );
      ad.show(context: context, onUserEarnedReward: (_, __) {});
      await tester.pumpAndSettle();
      expect(views, isEmpty);
      expect(error?.code, 'network_error');
      expect(dismissed, 0);
      expect(ad.isLoaded, isFalse);
    });

    testWidgets('online again, the break mounts as usual', (tester) async {
      final context = await boot(tester);
      os.emit(wifi);
      await settle(tester);
      late LevelMomentRewardedAd ad;
      await LevelMomentRewardedAd.load(
        placementId: 'g',
        adLoadCallback: LevelMomentAdLoadCallback(
          onAdLoaded: (a) => ad = a,
          onAdFailedToLoad: (e) => fail('should load: $e'),
        ),
      );
      ad.show(context: context, onUserEarnedReward: (_, __) {});
      await tester.pumpAndSettle();
      expect(views, hasLength(1));
      views.single.onMessage(const Dismissed());
      await tester.pumpAndSettle();
    });
  });
}
