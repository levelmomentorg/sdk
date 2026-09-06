// Pure-Dart unit tests for the credential half of the bridge:
// CredentialReply.toInjection(), credentialResponder(), and
// applyCredentialMessage(). No WebView platform channel is exercised — see
// the note at the bottom of gate_test.dart / rewarded_ad_test.dart for why
// LevelMomentWebView itself stays out of this suite.
//
// LevelMomentTokenStore's own get/set/clear round-trip is pinned in
// token_store_test.dart; this file just needs a working store to exercise
// credentialResponder and applyCredentialMessage against, so it points a
// real LevelMomentTokenStore at flutter_secure_storage's own in-memory test
// double (setMockInitialValues) rather than re-deriving that setup.

import 'dart:convert';

import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:levelmoment_ads/levelmoment_ads.dart';
import 'package:levelmoment_ads/src/credential_bridge.dart';
import 'package:levelmoment_ads/src/widgets/level_moment_web_view.dart';

/// Recover the reply an injection carries, the way the hosted page's runtime
/// does: unwrap the JS string literal, then parse the JSON inside it.
///
/// Asserting on the raw script text instead would be asserting on the wrong
/// thing — the JSON lands there escaped for the string literal that carries it,
/// so a substring match either fails on correct output or passes on output the
/// page could not parse.
Map<String, dynamic> decodeInjection(String js) {
  final match = RegExp(
    r'window\.__levelMomentDeliverCredential\((.*)\);',
  ).firstMatch(js);
  expect(match, isNotNull, reason: 'injection shape changed: $js');
  final literal = json.decode(match!.group(1)!) as String;
  return json.decode(literal) as Map<String, dynamic>;
}

void main() {
  // Each test gets a fresh in-memory backing map: setMockInitialValues swaps
  // FlutterSecureStoragePlatform.instance for flutter_secure_storage's own
  // test double, so LevelMomentTokenStore's default (real) constructor works
  // unmodified under `flutter test` — no lib/ change needed for this.
  setUp(() {
    FlutterSecureStorage.setMockInitialValues({});
  });

  group('CredentialReply.toInjection', () {
    test('calls window.__levelMomentDeliverCredential with the token', () {
      final js =
          const CredentialReply(token: 'tok-123', custody: true).toInjection();

      expect(js, contains('window.__levelMomentDeliverCredential'));
      expect(decodeInjection(js), {
        'token': 'tok-123',
        'custody': true,
        'customData': null,
        'protocolVersion': 1,
        'sdkVersion': '0.2.0',
      });
    });

    test(
        'a double-quote and a backslash in the token cannot break out of '
        'the JS string literal', () {
      // The reply is JSON-encoded once (the object) and then that JSON is
      // encoded AGAIN as a string literal (json.encode of a string escapes
      // quotes and backslashes the same way a JS string literal needs them
      // escaped). So whatever the inner encode does to the token, the outer
      // encode does to the inner encode's output — the token can carry any
      // character and still land inside exactly one string argument.
      const evilToken = 'tok"); alert(1); //\\';
      final js =
          const CredentialReply(token: evilToken, custody: true).toInjection();

      // The call must still parse as: identifier(single-string-literal);
      final match = RegExp(
        r'window\.__levelMomentDeliverCredential\((.*)\);',
      ).firstMatch(js);
      expect(match, isNotNull, reason: 'injection shape changed: $js');

      final literalSource = match!.group(1)!;
      // The argument must be a single JSON string literal — decoding it once
      // must succeed and yield a String (not fall through to raw script
      // because a quote inside the token closed the literal early).
      final decodedOnce = json.decode(literalSource);
      expect(decodedOnce, isA<String>());

      // Decoding that string as JSON recovers the original object, token
      // included verbatim — proof the double-encoding round-trips even
      // though the token itself contains `"` and `\`.
      final decodedTwice =
          json.decode(decodedOnce as String) as Map<String, dynamic>;
      expect(decodedTwice['token'], evilToken);
      expect(decodedTwice['custody'], true);
    });

    test('custody: false is carried through', () {
      final js = const CredentialReply(token: '', custody: false).toInjection();
      expect(decodeInjection(js), {
        'token': '',
        'custody': false,
        'customData': null,
        'protocolVersion': 1,
        'sdkVersion': '0.2.0',
      });
    });
  });

  group('credentialResponder', () {
    test('an explicit token wins over the store', () async {
      final store = LevelMomentTokenStore();
      await store.set('placement-a', 'stored-token');

      final responder = credentialResponder(
        placementId: 'placement-a',
        explicitToken: 'explicit-token',
        store: store,
      );
      final reply = await responder();

      expect(reply.token, 'explicit-token');
      expect(reply.custody, isTrue);
    });

    test('falls back to the store when no explicit token is given', () async {
      final store = LevelMomentTokenStore();
      await store.set('placement-a', 'stored-token');

      final responder = credentialResponder(
        placementId: 'placement-a',
        store: store,
      );
      final reply = await responder();

      expect(reply.token, 'stored-token');
    });

    test(
        'an empty explicit token is treated as none — falls back to the '
        'store', () async {
      final store = LevelMomentTokenStore();
      await store.set('placement-a', 'stored-token');

      final responder = credentialResponder(
        placementId: 'placement-a',
        explicitToken: '',
        store: store,
      );
      final reply = await responder();

      expect(reply.token, 'stored-token');
    });

    test(
        'with neither an explicit token nor a stored one, it still '
        'resolves promptly with an empty token and custody: true', () async {
      final store = LevelMomentTokenStore();

      final responder = credentialResponder(
        placementId: 'placement-a',
        store: store,
      );
      final reply = await responder().timeout(const Duration(seconds: 1));

      expect(reply.token, '');
      expect(reply.custody, isTrue);
    });

    test('declines custody when there is no working secure store', () async {
      // A plugin that failed to register, or a platform without a keystore.
      // Claiming custody there would have the page hand over credentials that
      // go nowhere while it stopped treating its own storage as the record.
      // Answering false degrades to exactly the pre-secure-store model.
      FlutterSecureStorage.setMockInitialValues({});
      final store = _UnavailableTokenStore();

      final reply = await credentialResponder(
        placementId: 'placement-a',
        explicitToken: 'explicit-token',
        store: store,
      )();

      expect(reply.custody, isFalse);
      // The explicit token still travels: the page can use it for this launch
      // even though nothing durable will be written.
      expect(reply.token, 'explicit-token');
    });

    test('unsafe testing never reads the production credential store',
        () async {
      final store = LevelMomentTokenStore();
      await store.set('placement-a', 'production-token');

      final reply = await credentialResponder(
        placementId: 'placement-a',
        store: store,
        useDeviceStore: false,
      )();

      expect(reply.token, isEmpty);
      expect(reply.custody, isFalse);
      expect(await store.get('placement-a'), 'production-token');
    });
  });

  group('applyCredentialMessage', () {
    test('stores the token on CredentialIssued', () async {
      final store = LevelMomentTokenStore();

      final handled = applyCredentialMessage(
        const CredentialIssued('minted-token'),
        'placement-a',
        store: store,
      );

      expect(handled, isTrue);
      // set() is fire-and-forget from applyCredentialMessage's point of view
      // (it does not await), so give the microtask queue a turn before
      // reading it back.
      await Future<void>.delayed(Duration.zero);
      expect(await store.get('placement-a'), 'minted-token');
    });

    test('clears the stored token on CredentialInvalid', () async {
      final store = LevelMomentTokenStore();
      await store.set('placement-a', 'dead-token');

      final handled = applyCredentialMessage(
        const CredentialInvalid(),
        'placement-a',
        store: store,
      );

      expect(handled, isTrue);
      await Future<void>.delayed(Duration.zero);
      expect(await store.get('placement-a'), '');
    });

    test('returns false and touches nothing for every other message', () async {
      final store = LevelMomentTokenStore();
      await store.set('placement-a', 'untouched-token');

      for (final message in <HostMessage>[
        const Ready(),
        const EarnedReward(1),
        const SignedIn(),
        const Dismissed(),
        const ErrorMsg('code', 'message'),
        const NeedCredential(),
      ]) {
        final handled =
            applyCredentialMessage(message, 'placement-a', store: store);
        expect(handled, isFalse, reason: '$message should not be handled');
      }

      expect(await store.get('placement-a'), 'untouched-token');
    });
  });
}

/// A store standing in for a device with no working secure storage: every call
/// fails the way an unregistered plugin does, so `isAvailable()` answers false.
class _UnavailableTokenStore extends LevelMomentTokenStore {
  @override
  Future<bool> isAvailable() async => false;
}
