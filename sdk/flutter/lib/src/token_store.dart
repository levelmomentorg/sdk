// ---------------------------------------------------------------------------
// SDK-owned credential storage, on the platform secure store.
//
// Before this, a paired device's credential lived only in the hosted page's
// storage — inside the WebView, on the Level Moment origin. That storage is
// real, but it is not the app's to keep: WebView site data can be cleared by
// the platform or by the person using the device, and the SDK gets no say in
// when. Every time it goes, the household re-pairs, with a parent's phone
// involved.
//
// The Keychain (iOS) and EncryptedSharedPreferences (Android) do not have that
// problem. They survive app updates and cache clears, they are encrypted at
// rest by the platform, and they are scoped to this app. So the shell keeps a
// copy: it answers the page's `needCredential` from here, writes what pairing
// issues (`credentialIssued`), and deletes on `credentialInvalid` so a dead
// value is not handed back next launch.
//
// One entry per placement, mirroring the page's per-placement keys: a
// credential is scoped to one game, and two Level Moment games in one app must
// not overwrite each other. And one slot per hosted origin: a credential a
// local stack issued under `unsafeTesting.realPairing` lives under a different
// prefix from a production one, so no test run can read, overwrite or delete
// the household's real credential. See
// docs/decisions/sdk-real-pairing-testing-2026-10-01.md, invariant 4.
//
// A secure store that will not answer is never fatal. Every method swallows its
// failure and reports "nothing stored", which lands the SDK back on the
// pre-secure-store behavior: the hosted page's own storage, and pairing when
// that is empty too.
// ---------------------------------------------------------------------------

import 'package:flutter_secure_storage/flutter_secure_storage.dart';

import 'constants.dart';

/// The origin of the production hosted page, whose credentials keep the
/// original key.
final String kLevelMomentProductionOrigin =
    levelMomentOrigin(kLevelMomentBreakUrl);

/// Storage key for one placement's credential on the hosted page at [origin].
///
/// The production origin keeps its original key,
/// `com.levelmoment.credential.<placementId>`, so stored credentials survive
/// with no migration. Any other origin gets
/// `com.levelmoment.test-credential.<encodedOrigin>.<placementId>`, where every
/// run of characters outside `[A-Za-z0-9.-]` becomes `_`
/// (`http://localhost:3000` → `http_localhost_3000`). The distinct prefix means
/// no placement id can reproduce a test key as a production one. Mirrors
/// `credentialStoreKey` in sdk/core.
String credentialKey(String origin, String placementId) {
  if (origin == kLevelMomentProductionOrigin) {
    return 'com.levelmoment.credential.$placementId';
  }
  final encoded = origin.replaceAll(RegExp(r'[^A-Za-z0-9.-]+'), '_');
  return 'com.levelmoment.test-credential.$encoded.$placementId';
}

/// Per-origin, per-placement credential storage. The shells use [deviceCredentials]; the
/// tests build their own over an in-memory store.
class LevelMomentTokenStore {
  LevelMomentTokenStore([FlutterSecureStorage? storage])
      : _storage = storage ?? const FlutterSecureStorage();

  final FlutterSecureStorage _storage;

  /// Whether this device actually has a working secure store, cached after the
  /// first answer.
  ///
  /// A plugin that failed to register, a platform without a keystore, or a
  /// locked one all end in the same place: every call throws and the catches
  /// below turn it into a silent `''`. Pairing then looks like it worked while
  /// nothing durable was written. Claiming custody in that state is a lie the
  /// household pays for in re-pairs, so the bridge asks this first.
  ///
  /// A read is the probe. It needs no write permission and cannot disturb a
  /// stored credential.
  Future<bool>? _available;

  Future<bool> isAvailable() => _available ??= _probe();

  Future<bool> _probe() async {
    try {
      // Outside both credential prefixes, so the probe is never a read of any
      // origin's credential slot.
      await _storage.read(key: 'com.levelmoment.availability-probe');
      return true;
    } catch (_) {
      return false;
    }
  }

  /// The credential stored for this placement on [origin], or `''` if there is
  /// none.
  Future<String> get(String origin, String placementId) async {
    if (placementId.isEmpty) return '';
    try {
      return await _storage.read(key: credentialKey(origin, placementId)) ?? '';
    } catch (_) {
      // A locked keystore, a platform without one, a failing plugin. The hosted
      // page's own storage is still there to fall back on.
      return '';
    }
  }

  /// Keep a credential the hosted page at [origin] just minted for this
  /// placement.
  Future<void> set(String origin, String placementId, String token) async {
    if (placementId.isEmpty || token.isEmpty) return;
    try {
      await _storage.write(
          key: credentialKey(origin, placementId), value: token);
    } catch (_) {
      // An unwritable store costs a re-pair after the WebView's own storage is
      // evicted, not this session.
    }
  }

  /// Forget this placement's credential on [origin] after the server refused
  /// it.
  Future<void> clear(String origin, String placementId) async {
    if (placementId.isEmpty) return;
    try {
      await _storage.delete(key: credentialKey(origin, placementId));
    } catch (_) {
      // Nothing to do: the page refuses the same value again next launch and
      // clears it again from there.
    }
  }
}

/// The store every Level Moment surface in this app shares.
LevelMomentTokenStore deviceCredentials = LevelMomentTokenStore();
