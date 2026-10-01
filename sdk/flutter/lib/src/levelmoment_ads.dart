// ---------------------------------------------------------------------------
// LevelMomentAds — singleton initialisation
// Mirrors: MobileAds.instance
// ---------------------------------------------------------------------------

import 'package:meta/meta.dart' show internal;
import 'package:flutter/widgets.dart';

import 'gate.dart';
import 'hosted.dart';
import 'widgets/level_moment_web_view.dart' show kBreakLoadTimeout;

export 'hosted.dart' show UnsafeTesting, LevelMomentHostedMode;

/// Initialise once at app start, before loading any ads.
///
/// ```dart
/// // BEFORE (AdMob):
/// await MobileAds.instance.initialize();
///
/// // AFTER (LevelMoment):
/// await LevelMomentAds.instance.initialize(
///   // Production uses https://levelmoment.com/break and /api by default.
/// );
/// ```
class LevelMomentAds {
  LevelMomentAds._();

  /// Mirrors: MobileAds.instance
  static final LevelMomentAds instance = LevelMomentAds._();

  ResolvedHosted? _hosted;
  UnsafeTesting? _unsafeTesting;

  /// Mirrors: MobileAds.instance.initialize()
  ///
  /// [apiUrl] is the API base, forwarded to the hosted break page as a URL
  /// param. [breakUrl] is where the hosted `/break` page lives; production
  /// defaults to `https://levelmoment.com/break`. Set [mock] to render bundled
  /// mock questions instead of hitting the live API. Mirrors the optional
  /// `mock` option in the other native SDKs.
  ///
  /// The options are resolved here, once, into a single
  /// [LevelMomentHostedMode]. With [UnsafeTesting.realPairing] it throws
  /// [ArgumentError] outside a debug build (`kDebugMode`, so profile builds are
  /// refused too), for any break URL other than `http://localhost:<port>` or
  /// `http://127.0.0.1:<port>`, and for a token, an `apiUrl`, a top-level
  /// `breakUrl`, or [mock]. A failed call leaves the previous configuration in
  /// place.
  Future<void> initialize({
    String? apiUrl,
    String? breakUrl,
    bool mock = false,
    UnsafeTesting? unsafeTesting,
  }) async {
    _hosted = resolveHostedOptions(
      apiUrl: apiUrl,
      breakUrl: breakUrl,
      mock: mock,
      unsafeTesting: unsafeTesting,
    );
    _unsafeTesting = unsafeTesting;
  }

  static bool isSandboxToken(String token) => isSandboxTokenValue(token);

  UnsafeTesting? get unsafeTesting => _unsafeTesting;

  /// The mode `initialize()` resolved: production, sandbox or real pairing.
  /// Asserts initialized.
  LevelMomentHostedMode get mode => hosted.mode;

  /// True in sandbox and real-pairing modes.
  bool get isUnsafeTesting =>
      _hosted != null && _hosted!.mode != LevelMomentHostedMode.production;

  /// The resolved configuration every URL builder and credential call site
  /// reads. Internal to the SDK; asserts initialized.
  @internal
  ResolvedHosted get hosted {
    assert(
      _hosted != null,
      'LevelMomentAds.instance.initialize() must be called before loading ads.',
    );
    return _hosted!;
  }

  /// Resolve the deprecated per-call token field without ever accepting a
  /// production credential. Sandbox mode keeps the token out of the device
  /// secure store and accepts sandbox credentials only. Production and real
  /// pairing accept no token at all: under real pairing the page pairs the
  /// device itself, so a per-call token is refused here, at the call.
  String? resolveStudentToken(String? legacyToken) {
    final hosted = _hosted;
    final configured = hosted?.token;
    if (legacyToken == null || legacyToken.isEmpty) return configured;
    if (hosted?.mode == LevelMomentHostedMode.realPairing) {
      throw ArgumentError.value(
        legacyToken,
        'studentToken',
        'unsafeTesting.realPairing takes no token; the page pairs the device itself.',
      );
    }
    if (hosted?.mode != LevelMomentHostedMode.sandbox ||
        !isSandboxToken(legacyToken)) {
      throw ArgumentError.value(
        legacyToken,
        'studentToken',
        'studentToken is available only for eply_sbx_ credentials in unsafeTesting.',
      );
    }
    return legacyToken;
  }

  /// The API base sent to the hosted page, or null under real pairing, where
  /// the page uses its own. Asserts initialized.
  String? get apiUrl => hosted.apiUrl;

  /// Hosted `/break` page URL. Asserts initialized.
  String get breakUrl => hosted.breakUrl;

  /// Whether the hosted page should use bundled mock questions.
  bool get mock => _hosted?.mock ?? false;

  bool get isInitialized => _hosted != null;

  // -------------------------------------------------------------------------
  // Startup sign-in gate
  // -------------------------------------------------------------------------

  /// Run the startup sign-in gate. Call it once before enabling gameplay and
  /// start the game only on [EnsureSignedInResult.ready].
  ///
  /// It pushes a fullscreen Level Moment route. A device that already holds a
  /// valid credential for this game passes through in a moment; otherwise the
  /// surface runs the ask-a-parent pairing flow and waits for the answer, for
  /// as long as a parent takes.
  ///
  /// Completes exactly once, and never with an error:
  /// - [EnsureSignedInResult.ready] — start the game.
  /// - [EnsureSignedInResult.canceled] — a person closed the gate or a parent
  ///   denied the connection. Show your own "learning breaks are off" state or
  ///   a retry action; do not retry automatically.
  /// - [EnsureSignedInResult.technicalFailure] — the gate could not run. Retry
  ///   later.
  ///
  /// None of the three reveals subscription, tier, or quota. With `mock: true`
  /// it completes [EnsureSignedInResult.ready] without showing anything.
  Future<EnsureSignedInResult> ensureSignedIn({
    required BuildContext context,
    required String placementId,
    String? studentToken,
    Duration loadTimeout = kBreakLoadTimeout,
  }) {
    if (!isInitialized)
      return Future.value(EnsureSignedInResult.technicalFailure);
    String? token;
    try {
      token = resolveStudentToken(studentToken);
    } catch (_) {
      return Future.value(EnsureSignedInResult.technicalFailure);
    }
    return runEnsureSignedIn(
      context: context,
      hosted: _hosted!,
      placementId: placementId,
      studentToken: token,
      loadTimeout: loadTimeout,
    );
  }

  /// Run the opt-in access gate on the hosted `/access` surface. The result
  /// has the same meaning as [ensureSignedIn], while identity sign-in remains
  /// on `/break` for existing integrations.
  Future<EnsureSignedInResult> ensureAccess({
    required BuildContext context,
    required String placementId,
    String? studentToken,
    Duration loadTimeout = kBreakLoadTimeout,
  }) {
    if (!isInitialized) {
      return Future.value(EnsureSignedInResult.technicalFailure);
    }
    String? token;
    try {
      token = resolveStudentToken(studentToken);
    } catch (_) {
      return Future.value(EnsureSignedInResult.technicalFailure);
    }
    return runEnsureAccess(
      context: context,
      hosted: _hosted!,
      placementId: placementId,
      studentToken: token,
      loadTimeout: loadTimeout,
    );
  }

  /// Ask whether this device holds a valid Level Moment credential for the
  /// game. This is an authoritative server-checked answer, not a cached flag:
  /// it mounts an offstage WebView that validates the stored credential through
  /// the API, because that credential lives on the Level Moment origin where
  /// this SDK cannot read it.
  ///
  /// Throws [LevelMomentSignInCheckError] — rather than completing `false` — if
  /// the check fails technically, exceeds [loadTimeout], exceeds the total
  /// [checkTimeout] deadline (including load time), or the surface would not
  /// mount. `false` is a claim about the household, and guessing it from a
  /// network failure would push a linked player back through pairing. Treat a
  /// thrown error as "unknown, try again", never as signed out. With
  /// `mock: true` it completes `true`.
  ///
  /// [checkTimeout] has no counterpart on `ensureSignedIn`, which waits without
  /// a deadline once the pairing card is up.
  Future<bool> isSignedIn({
    required BuildContext context,
    required String placementId,
    String? studentToken,
    Duration loadTimeout = kBreakLoadTimeout,
    Duration checkTimeout = kSignInCheckTimeout,
  }) {
    if (!isInitialized) {
      return Future.error(const LevelMomentSignInCheckError(
        'not_initialized',
        'Call LevelMomentAds.instance.initialize() before isSignedIn().',
      ));
    }
    String? token;
    try {
      token = resolveStudentToken(studentToken);
    } catch (_) {
      return Future.error(const LevelMomentSignInCheckError(
        'invalid_request',
        'Sign-in checks accept a studentToken only in sandbox testing.',
      ));
    }
    return runIsSignedIn(
      context: context,
      hosted: _hosted!,
      placementId: placementId,
      studentToken: token,
      loadTimeout: loadTimeout,
      checkTimeout: checkTimeout,
    );
  }

  /// Check whether access is already connected. Returns `false` when a person
  /// needs to complete the access flow; throws [LevelMomentSignInCheckError]
  /// when the check fails technically. This is the access counterpart to
  /// [isSignedIn] and uses `/access?mode=check`.
  Future<bool> checkAccess({
    required BuildContext context,
    required String placementId,
    String? studentToken,
    Duration loadTimeout = kBreakLoadTimeout,
    Duration checkTimeout = kSignInCheckTimeout,
  }) {
    if (!isInitialized) {
      return Future.error(const LevelMomentSignInCheckError(
        'not_initialized',
        'Call LevelMomentAds.instance.initialize() before checkAccess().',
      ));
    }
    String? token;
    try {
      token = resolveStudentToken(studentToken);
    } catch (_) {
      return Future.error(const LevelMomentSignInCheckError(
        'invalid_request',
        'Access checks accept a studentToken only in sandbox testing.',
      ));
    }
    return runCheckAccess(
      context: context,
      hosted: _hosted!,
      placementId: placementId,
      studentToken: token,
      loadTimeout: loadTimeout,
      checkTimeout: checkTimeout,
    );
  }

  /// Sign this device out of Level Moment for [placementId].
  ///
  /// The credential lives in two places, and clearing one is worse than
  /// clearing neither: wipe only the secure store and the next
  /// [ensureSignedIn] finds the copy still sitting in the hosted origin's
  /// storage, validates it, and signs the previous learner straight back in. On
  /// a shared device that is the whole problem this call exists to solve. So it
  /// clears both, secure store first.
  ///
  /// Not atomic. If the hosted clear cannot run it throws
  /// [LevelMomentSignInCheckError] with this app's credential already gone and
  /// the household still signed in; call it again when there is a network.
  /// Completing quietly would claim a sign-out that did not happen.
  ///
  /// With `mock: true` it completes without showing or clearing anything.
  Future<void> signOut({
    required BuildContext context,
    required String placementId,
    Duration loadTimeout = kBreakLoadTimeout,
    Duration checkTimeout = kSignInCheckTimeout,
  }) {
    if (!isInitialized) {
      return Future.error(const LevelMomentSignInCheckError(
        'not_initialized',
        'Call LevelMomentAds.instance.initialize() before signOut().',
      ));
    }
    return runSignOut(
      context: context,
      hosted: _hosted!,
      placementId: placementId,
      loadTimeout: loadTimeout,
      checkTimeout: checkTimeout,
    );
  }
}
