// ---------------------------------------------------------------------------
// The startup sign-in gate for the Flutter adapter.
//
// Both entry points open the hosted /break page in the same WebView the break
// uses (widgets/level_moment_web_view.dart), differing only in the `mode` param
// and whether the view is visible:
//
//   ensureSignedIn -> ?mode=gate   a pushed fullscreen route; pairs if needed
//   isSignedIn     -> ?mode=check  an offstage overlay entry; validates, closes
//   signOut        -> ?mode=clear  an offstage entry; drops the hosted copy
//
// Why isSignedIn() needs a WebView at all: the device credential lives in the
// hosted page's storage, on the hosted origin. The SDK cannot read it — that is
// the point of putting it there — so the only place that can answer
// authoritatively is the hosted page itself. It calls POST /device-checks and
// posts the verdict back.
//
// Mirrors sdk/web/src/gate.ts message-for-message.
// ---------------------------------------------------------------------------

import 'dart:async';

import 'package:flutter/material.dart';

import 'credential_bridge.dart';
import 'constants.dart';
import 'token_store.dart';
import 'widgets/level_moment_web_view.dart';

/// Total deadline for the headless check — load, credential walk, verdict.
///
/// [kBreakLoadTimeout] only covers the page coming up, and the hosted page's own
/// `check_timeout` only covers the page still running. Neither survives a
/// WebView that loads and then goes silent, so `isSignedIn()` keeps one deadline
/// over the whole thing. With default settings, the 30-second total deadline is
/// the final backstop after the 15-second load watchdog and the page's
/// 10-second deadline.
///
/// `ensureSignedIn()` has no equivalent: past `ready` a parent is finding their
/// phone, and no deadline belongs on that.
const Duration kSignInCheckTimeout = Duration(seconds: 30);

/// The result of the startup gate. Every Level Moment SDK reports these three
/// values, so a publisher shipping on two platforms writes the startup branch
/// once. None of them reveals subscription, tier, or quota.
enum EnsureSignedInResult {
  /// Start the game.
  ready,

  /// A person closed the gate, or a parent denied the connection. Show your own
  /// "learning breaks are off" state or a retry action; do not retry
  /// automatically.
  canceled,

  /// The gate could not run. Retry later.
  technicalFailure,
}

/// Build the hosted gate URL for [mode] (`gate` or `check`).
/// Exposed for testing; treat as private elsewhere.
@visibleForTesting
String buildGateUrl({
  required String breakUrl,
  required String placementId,
  required String mode,
  String? apiUrl,
  bool mock = false,
  bool unsafeTesting = false,
}) {
  final params = <String, String>{
    'mode': mode,
    'placementId': placementId,
    'protocolVersion': '$kLevelMomentProtocolVersion',
    'sdkVersion': kLevelMomentSdkVersion,
  };
  if (mock) {
    params['mock'] = 'true';
  } else if (apiUrl != null && apiUrl.isNotEmpty) {
    params['apiUrl'] = apiUrl;
  }
  if (unsafeTesting) params['sandbox'] = 'true';
  // No credential on the URL — the page asks for one over the bridge.
  params[kHostCapabilitiesParam] = kHostCapabilities;

  final query = params.entries
      .map((e) =>
          '${Uri.encodeQueryComponent(e.key)}=${Uri.encodeQueryComponent(e.value)}')
      .join('&');
  final sep = breakUrl.contains('?') ? '&' : '?';
  return '$breakUrl$sep$query';
}

/// Build the hosted access surface URL while preserving an explicitly chosen
/// unsafe test origin. Production always uses the canonical `/access` page.
@visibleForTesting
String buildAccessUrl({
  required String breakUrl,
  required String placementId,
  required String mode,
  String? apiUrl,
  bool mock = false,
  bool unsafeTesting = false,
}) {
  final parsed = Uri.tryParse(breakUrl);
  if (parsed == null || !parsed.hasScheme || !parsed.hasAuthority) {
    throw ArgumentError.value(breakUrl, 'breakUrl', 'Must be an absolute URL.');
  }
  final accessUrl = breakUrl == kLevelMomentBreakUrl
      ? kLevelMomentAccessUrl
      : parsed.replace(path: '/access', query: null, fragment: null).toString();
  return buildGateUrl(
    breakUrl: accessUrl,
    placementId: placementId,
    mode: mode,
    apiUrl: apiUrl,
    mock: mock,
    unsafeTesting: unsafeTesting,
  );
}

/// The terminal-once discipline both entry points share. The hosted page can
/// post more than one message on a teardown race, and a publisher's startup
/// branch must run exactly once.
///
/// `signedIn` is the yes, `dismissed` is the no — a closed gate and a parent's
/// denial arrive the same way and mean the same thing to the game — and `error`
/// is the technical failure. `ready` and `earnedReward` end nothing.
///
/// Exposed for testing; treat as private elsewhere.
@visibleForTesting
class SignInDispatcher {
  SignInDispatcher({
    required this.onReady,
    required this.onCanceled,
    required this.onFailure,
  });

  final void Function() onReady;
  final void Function() onCanceled;
  final void Function(String code) onFailure;

  bool _settled = false;
  Timer? _deadline;

  bool get isSettled => _settled;

  /// Arm a total deadline that ends the gate with [code] if nothing else has by
  /// then. Only the headless check arms one — `ensureSignedIn` must wait as long
  /// as a parent takes. Cancelled by whichever terminal arrives first.
  void armDeadline(Duration timeout, String code) {
    _deadline?.cancel();
    if (_settled) return;
    _deadline = Timer(timeout, () => fail(code));
  }

  /// Feed a message from the hosted page. Returns true when it ended the gate.
  bool handle(HostMessage message) {
    if (_settled) return false;
    switch (message) {
      case SignedIn():
        _settle();
        onReady();
        return true;
      case Dismissed():
        _settle();
        onCanceled();
        return true;
      case ErrorMsg(:final code):
        _settle();
        onFailure(code);
        return true;
      // The credential messages are the shell's bookkeeping, not a verdict —
      // pairing minting a credential inside the gate is followed by the gate's
      // own `signedIn`, and ending here would pre-empt it.
      // `openExternal` is the WebView's job (it launches the browser) and
      // decides nothing here: the parent is off approving, and the gate ends
      // when the page's own poll hears them.
      case Ready() ||
            EarnedReward() ||
            NeedCredential() ||
            CredentialIssued() ||
            CredentialInvalid() ||
            OpenExternal():
        return false;
    }
  }

  /// End the gate on a technical failure the page never reported itself —
  /// the watchdog, the total deadline, or a surface that would not mount.
  bool fail(String code) {
    if (_settled) return false;
    _settle();
    onFailure(code);
    return true;
  }

  void _settle() {
    _settled = true;
    _deadline?.cancel();
    _deadline = null;
  }
}

/// Thrown by `isSignedIn()` when the check could not produce an answer. Treat it
/// as "unknown, try again", never as signed out.
class LevelMomentSignInCheckError implements Exception {
  const LevelMomentSignInCheckError(this.code, this.message);

  final String code;
  final String message;

  @override
  String toString() => 'LevelMomentSignInCheckError($code: $message)';
}

/// Run the gate in a pushed fullscreen route. See
/// `LevelMomentAds.ensureSignedIn` for the public contract.
Future<EnsureSignedInResult> runEnsureSignedIn({
  required BuildContext context,
  required String breakUrl,
  required String placementId,
  String? apiUrl,
  String? studentToken,
  bool mock = false,
  bool useDeviceStore = true,
  Duration loadTimeout = kBreakLoadTimeout,
}) {
  if (mock) return Future.value(EnsureSignedInResult.ready);

  final completer = Completer<EnsureSignedInResult>();
  void settle(EnsureSignedInResult result) {
    if (!completer.isCompleted) completer.complete(result);
  }

  final dispatcher = SignInDispatcher(
    onReady: () => settle(EnsureSignedInResult.ready),
    onCanceled: () => settle(EnsureSignedInResult.canceled),
    onFailure: (_) => settle(EnsureSignedInResult.technicalFailure),
  );

  try {
    final navigator = Navigator.of(context, rootNavigator: true);
    final hostedUrl = buildGateUrl(
      breakUrl: breakUrl,
      placementId: placementId,
      mode: 'gate',
      apiUrl: apiUrl,
      mock: mock,
      unsafeTesting: !useDeviceStore,
    );
    late final MaterialPageRoute<void> route;
    route = MaterialPageRoute<void>(
      fullscreenDialog: true,
      builder: (_) => LevelMomentWebView(
        key: ValueKey(hostedUrl),
        url: hostedUrl,
        loadTimeout: loadTimeout,
        onNeedCredential: credentialResponder(
          placementId: placementId,
          explicitToken: studentToken,
          useDeviceStore: useDeviceStore,
          origin: levelMomentOrigin(breakUrl),
        ),
        onMessage: (message) {
          // Pairing runs inside the gate, so this is where a first credential
          // is usually minted — keep it before the dispatcher reads the
          // message for a verdict.
          applyCredentialMessage(message, placementId,
              useDeviceStore: useDeviceStore);
          dispatcher.handle(message);
        },
        onLoadTimeout: () {
          dispatcher.fail('load_timeout');
          if (route.isActive) navigator.removeRoute(route);
        },
      ),
    );
    navigator.push(route);
  } catch (_) {
    // A context with no navigator, or a route that will not mount, is a
    // technical failure — not an exception. This method's contract is three
    // values and nothing else: a publisher writes `switch (await
    // ensureSignedIn())` at startup, and a throw from it would crash the boot
    // path of a game whose only sin was a bad config string.
    dispatcher.fail('mount_failed');
  }

  return completer.future;
}

/// Run the access gate on the hosted `/access` surface. Identity sign-in
/// remains on `/break`; this wrapper only changes the hosted page path.
Future<EnsureSignedInResult> runEnsureAccess({
  required BuildContext context,
  required String breakUrl,
  required String placementId,
  String? apiUrl,
  String? studentToken,
  bool mock = false,
  bool useDeviceStore = true,
  Duration loadTimeout = kBreakLoadTimeout,
}) {
  return runEnsureSignedIn(
    context: context,
    breakUrl: _accessSurfaceUrl(breakUrl),
    placementId: placementId,
    apiUrl: apiUrl,
    studentToken: studentToken,
    mock: mock,
    useDeviceStore: useDeviceStore,
    loadTimeout: loadTimeout,
  );
}

/// Run the headless credential check in an offstage overlay entry. See
/// `LevelMomentAds.isSignedIn` for the public contract.
Future<bool> runIsSignedIn({
  required BuildContext context,
  required String breakUrl,
  required String placementId,
  String? apiUrl,
  String? studentToken,
  bool mock = false,
  bool useDeviceStore = true,
  Duration loadTimeout = kBreakLoadTimeout,
  Duration checkTimeout = kSignInCheckTimeout,
}) {
  if (mock) return Future.value(true);

  final completer = Completer<bool>();
  void resolve(bool value) {
    if (!completer.isCompleted) completer.complete(value);
  }

  void reject(String code, String message) {
    if (!completer.isCompleted) {
      completer.completeError(LevelMomentSignInCheckError(code, message));
    }
  }

  OverlayEntry? entry;
  void removeEntry() {
    entry?.remove();
    entry = null;
  }

  final dispatcher = SignInDispatcher(
    onReady: () {
      removeEntry();
      resolve(true);
    },
    onCanceled: () {
      removeEntry();
      resolve(false);
    },
    // Rejects rather than answering `false`: `false` is a claim about the
    // household, and guessing it from a network failure would push a linked
    // player back through pairing.
    onFailure: (code) {
      removeEntry();
      reject(code, 'Level Moment could not check this device.');
    },
  );

  try {
    final overlay = Overlay.of(context, rootOverlay: true);
    final hostedUrl = buildGateUrl(
      breakUrl: breakUrl,
      placementId: placementId,
      mode: 'check',
      apiUrl: apiUrl,
      mock: mock,
      unsafeTesting: !useDeviceStore,
    );
    entry = OverlayEntry(
      builder: (_) => LevelMomentWebView(
        key: ValueKey(hostedUrl),
        url: hostedUrl,
        hidden: true,
        loadTimeout: loadTimeout,
        onNeedCredential: credentialResponder(
          placementId: placementId,
          explicitToken: studentToken,
          useDeviceStore: useDeviceStore,
          origin: levelMomentOrigin(breakUrl),
        ),
        onMessage: (message) {
          // The check does not pair, but it does discard credentials the
          // server refuses — the secure store has to hear about that.
          applyCredentialMessage(message, placementId,
              useDeviceStore: useDeviceStore);
          dispatcher.handle(message);
        },
        onLoadTimeout: () => dispatcher.fail('load_timeout'),
      ),
    );
    overlay.insert(entry!);
    // Armed only once the surface is really mounted, and only for the check:
    // the load watchdog stops at `ready`, and the page's own deadline cannot
    // fire if the page is the thing that stopped running.
    if (checkTimeout > Duration.zero) {
      dispatcher.armDeadline(checkTimeout, 'check_timeout');
    }
  } catch (err) {
    // Unlike ensureSignedIn, this one throws: its contract is a boolean, and
    // there is no honest boolean for "the check never ran".
    entry = null;
    reject(
        'mount_failed', 'Level Moment could not open the sign-in check: $err');
  }

  return completer.future;
}

/// Run the headless access check on the hosted `/access` surface. It keeps the
/// same false-versus-technical-error contract as [runIsSignedIn].
Future<bool> runCheckAccess({
  required BuildContext context,
  required String breakUrl,
  required String placementId,
  String? apiUrl,
  String? studentToken,
  bool mock = false,
  bool useDeviceStore = true,
  Duration loadTimeout = kBreakLoadTimeout,
  Duration checkTimeout = kSignInCheckTimeout,
}) {
  return runIsSignedIn(
    context: context,
    breakUrl: _accessSurfaceUrl(breakUrl),
    placementId: placementId,
    apiUrl: apiUrl,
    studentToken: studentToken,
    mock: mock,
    useDeviceStore: useDeviceStore,
    loadTimeout: loadTimeout,
    checkTimeout: checkTimeout,
  );
}

String _accessSurfaceUrl(String breakUrl) {
  final parsed = Uri.tryParse(breakUrl);
  if (parsed == null || !parsed.hasScheme || !parsed.hasAuthority) {
    throw ArgumentError.value(breakUrl, 'breakUrl', 'Must be an absolute URL.');
  }
  return breakUrl == kLevelMomentBreakUrl
      ? kLevelMomentAccessUrl
      : parsed.replace(path: '/access', query: null, fragment: null).toString();
}

/// Sign this device out for one placement. See `LevelMomentAds.signOut` for the
/// public contract.
Future<void> runSignOut({
  required BuildContext context,
  required String breakUrl,
  required String placementId,
  String? apiUrl,
  bool mock = false,
  Duration loadTimeout = kBreakLoadTimeout,
  Duration checkTimeout = kSignInCheckTimeout,
  LevelMomentTokenStore? store,
  bool useDeviceStore = true,
}) {
  if (mock) return Future.value();

  final completer = Completer<void>();
  void succeed() {
    if (!completer.isCompleted) completer.complete();
  }

  void fail(String code, String message) {
    if (!completer.isCompleted) {
      completer.completeError(LevelMomentSignInCheckError(code, message));
    }
  }

  OverlayEntry? entry;
  void removeEntry() {
    entry?.remove();
    entry = null;
  }

  final dispatcher = SignInDispatcher(
    // The page ends this mode with `dismissed`, which the dispatcher reports on
    // the cancel path. For a sign-out that is the success signal.
    onReady: () {
      removeEntry();
      succeed();
    },
    onCanceled: () {
      removeEntry();
      succeed();
    },
    // Throws rather than completing quietly: the secure store is already empty
    // and the hosted copy may not be, so the household is still signed in.
    // Reporting success would claim a sign-out that did not happen.
    onFailure: (code) {
      removeEntry();
      fail(
        code,
        "Level Moment cleared this app's credential but could not clear the "
        'hosted one. Call signOut again when there is a network.',
      );
    },
  );

  // Secure store first, hosted second. The hosted clear answers with
  // `credentialInvalid`, which drives a second secure-store wipe through the
  // normal message path, so the fragile half runs last and repairs the durable
  // half on its way out.
  final target = store ?? deviceCredentials;
  final clear =
      useDeviceStore ? target.clear(placementId) : Future<void>.value();
  clear.then((_) {
    try {
      final overlay = Overlay.of(context, rootOverlay: true);
      final hostedUrl = buildGateUrl(
        breakUrl: breakUrl,
        placementId: placementId,
        mode: 'clear',
        apiUrl: apiUrl,
        mock: mock,
        unsafeTesting: !useDeviceStore,
      );
      entry = OverlayEntry(
        builder: (_) => LevelMomentWebView(
          key: ValueKey(hostedUrl),
          url: hostedUrl,
          hidden: true,
          loadTimeout: loadTimeout,
          onMessage: (message) {
            applyCredentialMessage(
              message,
              placementId,
              store: store,
              useDeviceStore: useDeviceStore,
            );
            dispatcher.handle(message);
          },
          onLoadTimeout: () => dispatcher.fail('load_timeout'),
        ),
      );
      overlay.insert(entry!);
      if (checkTimeout > Duration.zero) {
        dispatcher.armDeadline(checkTimeout, 'clear_timeout');
      }
    } catch (err) {
      entry = null;
      fail('mount_failed',
          'Level Moment could not open the sign-out surface: $err');
    }
  });

  return completer.future;
}
