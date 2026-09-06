// ---------------------------------------------------------------------------
// LevelMomentAds — singleton initialisation
// Mirrors: MobileAds.instance
// ---------------------------------------------------------------------------

import 'package:flutter/widgets.dart';

import 'constants.dart';
import 'gate.dart';
import 'widgets/level_moment_web_view.dart' show kBreakLoadTimeout;

/// Explicit opt-in configuration for local or sandbox testing.
///
/// Production integrations must use the hosted defaults. This object is the
/// only way to point the SDK at a different URL or provide a test credential.
class UnsafeTesting {
  const UnsafeTesting({
    this.breakUrl,
    this.apiUrl,
    this.token,
  });

  final String? breakUrl;
  final String? apiUrl;
  final String? token;
}

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

  String? _apiUrl;
  String? _breakUrl;
  bool _mock = false;
  bool _initialized = false;
  UnsafeTesting? _unsafeTesting;

  /// Mirrors: MobileAds.instance.initialize()
  ///
  /// [apiUrl] is the API base, forwarded to the hosted break page as a URL
  /// param. [breakUrl] is where the hosted `/break` page lives; production
  /// defaults to `https://levelmoment.com/break`. Set [mock] to render bundled
  /// mock questions instead of hitting the live API. Mirrors the optional
  /// `mock` option in the other native SDKs.
  Future<void> initialize({
    String? apiUrl,
    String? breakUrl,
    bool mock = false,
    UnsafeTesting? unsafeTesting,
  }) async {
    final effectiveApiUrl =
        unsafeTesting?.apiUrl ?? apiUrl ?? kLevelMomentApiUrl;
    final effectiveBreakUrl =
        unsafeTesting?.breakUrl ?? breakUrl ?? kLevelMomentBreakUrl;
    _validateEndpoint(
        effectiveApiUrl, kLevelMomentApiUrl, 'apiUrl', unsafeTesting);
    _validateEndpoint(
      effectiveBreakUrl,
      kLevelMomentBreakUrl,
      'breakUrl',
      unsafeTesting,
    );
    if (unsafeTesting?.token != null &&
        !isSandboxToken(unsafeTesting!.token!)) {
      throw ArgumentError.value(
        unsafeTesting.token,
        'unsafeTesting.token',
        'Test credentials must start with eply_sbx_.',
      );
    }
    _apiUrl = effectiveApiUrl;
    _breakUrl = effectiveBreakUrl;
    _mock = mock;
    _unsafeTesting = unsafeTesting;
    _initialized = true;
  }

  static bool isSandboxToken(String token) => token.startsWith('eply_sbx_');

  void _validateEndpoint(
    String value,
    String canonical,
    String name,
    UnsafeTesting? unsafeTesting,
  ) {
    if (unsafeTesting != null) {
      final uri = Uri.tryParse(value);
      if (uri == null ||
          !uri.hasScheme ||
          !uri.hasAuthority ||
          (uri.scheme != 'http' && uri.scheme != 'https') ||
          uri.userInfo.isNotEmpty ||
          uri.hasQuery ||
          uri.hasFragment) {
        throw ArgumentError.value(value, name, 'Must be an absolute URL.');
      }
      return;
    }
    if (value != canonical) {
      throw ArgumentError.value(
        value,
        name,
        'Use the canonical Level Moment endpoint, or configure unsafeTesting for tests.',
      );
    }
  }

  UnsafeTesting? get unsafeTesting => _unsafeTesting;

  bool get isUnsafeTesting => _unsafeTesting != null;

  /// Resolve the deprecated per-call token field without ever accepting a
  /// production credential. Unsafe testing keeps the token out of the device
  /// secure store and accepts sandbox credentials only.
  String? resolveStudentToken(String? legacyToken) {
    final configured = _unsafeTesting?.token;
    if (legacyToken == null || legacyToken.isEmpty) return configured;
    if (_unsafeTesting == null || !isSandboxToken(legacyToken)) {
      throw ArgumentError.value(
        legacyToken,
        'studentToken',
        'studentToken is available only for eply_sbx_ credentials in unsafeTesting.',
      );
    }
    return legacyToken;
  }

  String get apiUrl {
    assert(
      _initialized,
      'LevelMomentAds.instance.initialize() must be called before loading ads.',
    );
    return _apiUrl!;
  }

  /// Hosted `/break` page URL. Asserts initialized.
  String get breakUrl {
    assert(
      _initialized,
      'LevelMomentAds.instance.initialize() must be called before loading ads.',
    );
    return _breakUrl!;
  }

  /// Whether the hosted page should use bundled mock questions.
  bool get mock => _mock;

  bool get isInitialized => _initialized;

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
    if (!_initialized)
      return Future.value(EnsureSignedInResult.technicalFailure);
    String? token;
    try {
      token = resolveStudentToken(studentToken);
    } catch (_) {
      return Future.value(EnsureSignedInResult.technicalFailure);
    }
    return runEnsureSignedIn(
      context: context,
      breakUrl: _breakUrl!,
      placementId: placementId,
      apiUrl: _apiUrl,
      studentToken: token,
      mock: _mock,
      useDeviceStore: _unsafeTesting == null,
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
    if (!_initialized) {
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
      breakUrl: _breakUrl!,
      placementId: placementId,
      apiUrl: _apiUrl,
      studentToken: token,
      mock: _mock,
      useDeviceStore: _unsafeTesting == null,
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
    if (!_initialized) {
      return Future.error(const LevelMomentSignInCheckError(
        'not_initialized',
        'Call LevelMomentAds.instance.initialize() before isSignedIn().',
      ));
    }
    String? token;
    try {
      token = resolveStudentToken(studentToken);
    } catch (err) {
      return Future.error(const LevelMomentSignInCheckError(
        'invalid_request',
        'Production sign-in checks cannot receive a studentToken.',
      ));
    }
    return runIsSignedIn(
      context: context,
      breakUrl: _breakUrl!,
      placementId: placementId,
      apiUrl: _apiUrl,
      studentToken: token,
      mock: _mock,
      useDeviceStore: _unsafeTesting == null,
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
    if (!_initialized) {
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
        'Production access checks cannot receive a studentToken.',
      ));
    }
    return runCheckAccess(
      context: context,
      breakUrl: _breakUrl!,
      placementId: placementId,
      apiUrl: _apiUrl,
      studentToken: token,
      mock: _mock,
      useDeviceStore: _unsafeTesting == null,
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
    if (!_initialized) {
      return Future.error(const LevelMomentSignInCheckError(
        'not_initialized',
        'Call LevelMomentAds.instance.initialize() before signOut().',
      ));
    }
    return runSignOut(
      context: context,
      breakUrl: _breakUrl!,
      placementId: placementId,
      apiUrl: _apiUrl,
      mock: _mock,
      loadTimeout: loadTimeout,
      checkTimeout: checkTimeout,
      useDeviceStore: _unsafeTesting == null,
    );
  }
}
