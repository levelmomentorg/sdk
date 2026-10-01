// ---------------------------------------------------------------------------
// The credential half of the bridge, shared by a break and the startup gate.
//
// Both open the same hosted surface and both must answer its `needCredential`
// the same way, or a gate could approve one credential and the break that
// follows hand over a different one. So the answering and the secure-store
// bookkeeping live here, once.
//
// A credential is never put on the hosted URL. It travels only on this bridge:
// the page asks, the shell answers, and pairing hands the minted credential
// back so the secure store can outlive the WebView's own storage.
//
// The reply is origin-bound by CredentialReply.toInjection(), and the WebView
// navigation delegate separately prevents the page from leaving the loaded
// origin. The async store read is also discarded when the view is stale.
// ---------------------------------------------------------------------------

import 'token_store.dart';
import 'widgets/level_moment_web_view.dart';

/// Build the answer to a [NeedCredential] for one placement on the hosted page
/// at [origin].
///
/// [origin] binds the reply to that page and picks the secure-store slot, so a
/// test origin never reads the production credential. [useDeviceStore] is the
/// resolved mode's `usesCredentialStore`: false for sandbox and mock runs.
///
/// [explicitToken] is whatever the game supplied — a sandbox token, or one it
/// read from a parent-portal link. It leads when set: a token the game gave for
/// this launch is the newer intent, and the page applies the same known-bad
/// demotion to it that it applied to `?token=`. With none, the secure-store
/// copy answers. With neither, the reply carries an empty token and still goes
/// out promptly, so the page stops waiting.
Future<CredentialReply> Function() credentialResponder({
  required String placementId,
  required String origin,
  required bool useDeviceStore,
  String? explicitToken,
  LevelMomentTokenStore? store,
  String? customData,
  String? slotType,
  Map<String, Object>? dimensions,
}) {
  return () async {
    final target = store ?? deviceCredentials;
    final token = (explicitToken != null && explicitToken.isNotEmpty)
        ? explicitToken
        : useDeviceStore
            ? await target.get(origin, placementId)
            : '';
    // Custody is a promise to keep a durable copy, so it is only claimed when
    // there is a secure store to keep it in. Where there is not, claiming it
    // would have the page hand over credentials that go nowhere while it stops
    // treating its own storage as the record; answering false degrades to
    // exactly the pre-secure-store model instead.
    return CredentialReply(
      token: token,
      custody: useDeviceStore && await target.isAvailable(),
      customData: customData,
      slotType: slotType,
      dimensions: dimensions,
      origin: origin,
    );
  };
}

/// Apply a credential message to the secure-store slot for [origin]. Returns
/// whether it handled the message, so callers can keep their own switch honest.
///
/// Both directions matter. Storing what pairing issues is what saves the next
/// launch from asking a parent again; deleting what the page discards is what
/// stops the shell handing the same refused value straight back.
bool applyCredentialMessage(
  HostMessage message,
  String placementId, {
  required String origin,
  required bool useDeviceStore,
  LevelMomentTokenStore? store,
}) {
  if (!useDeviceStore) {
    return message is CredentialIssued || message is CredentialInvalid;
  }
  final target = store ?? deviceCredentials;
  if (message is CredentialIssued) {
    target.set(origin, placementId, message.token);
    return true;
  }
  if (message is CredentialInvalid) {
    target.clear(origin, placementId);
    return true;
  }
  return false;
}
