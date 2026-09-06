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
// not overwrite each other.
//
// A secure store that will not answer is never fatal. Every method swallows its
// failure and reports "nothing stored", which lands the SDK back on the
// pre-secure-store behavior: the hosted page's own storage, and pairing when
// that is empty too.
// ---------------------------------------------------------------------------

import 'package:flutter_secure_storage/flutter_secure_storage.dart';

/// Storage key for one placement's credential.
String credentialKey(String placementId) =>
    'com.levelmoment.credential.$placementId';

/// Per-placement credential storage. The shells use [deviceCredentials]; the
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
      await _storage.read(key: credentialKey('availability-probe'));
      return true;
    } catch (_) {
      return false;
    }
  }

  /// The credential stored for this placement, or `''` if there is none.
  Future<String> get(String placementId) async {
    if (placementId.isEmpty) return '';
    try {
      return await _storage.read(key: credentialKey(placementId)) ?? '';
    } catch (_) {
      // A locked keystore, a platform without one, a failing plugin. The hosted
      // page's own storage is still there to fall back on.
      return '';
    }
  }

  /// Keep a credential the hosted page just minted for this placement.
  Future<void> set(String placementId, String token) async {
    if (placementId.isEmpty || token.isEmpty) return;
    try {
      await _storage.write(key: credentialKey(placementId), value: token);
    } catch (_) {
      // An unwritable store costs a re-pair after the WebView's own storage is
      // evicted, not this session.
    }
  }

  /// Forget this placement's credential after the server refused it.
  Future<void> clear(String placementId) async {
    if (placementId.isEmpty) return;
    try {
      await _storage.delete(key: credentialKey(placementId));
    } catch (_) {
      // Nothing to do: the page refuses the same value again next launch and
      // clears it again from there.
    }
  }
}

/// The store every Level Moment surface in this app shares.
LevelMomentTokenStore deviceCredentials = LevelMomentTokenStore();
