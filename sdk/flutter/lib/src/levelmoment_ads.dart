// ---------------------------------------------------------------------------
// LevelMomentAds — singleton initialisation
// Mirrors: MobileAds.instance
// ---------------------------------------------------------------------------

/// Initialise once at app start, before loading any ads.
///
/// ```dart
/// // BEFORE (AdMob):
/// await MobileAds.instance.initialize();
///
/// // AFTER (LevelMoment):
/// await LevelMomentAds.instance.initialize(
///   apiUrl: 'https://api.levelmoment.com',
///   breakUrl: 'https://app.levelmoment.com/break',
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

  /// Mirrors: MobileAds.instance.initialize()
  ///
  /// [apiUrl] is the API base, forwarded to the hosted break page as a URL
  /// param. [breakUrl] is where the hosted `/break` page lives (e.g.
  /// `https://app.levelmoment.com/break`) — the WebView shell has no other way to
  /// know where to point. Set [mock] to render bundled mock questions instead
  /// of hitting the live API. Mirrors the required `breakUrl` /
  /// optional `mock` options in the react-native SDK.
  Future<void> initialize({
    required String apiUrl,
    required String breakUrl,
    bool mock = false,
  }) async {
    _apiUrl = apiUrl;
    _breakUrl = breakUrl;
    _mock = mock;
    _initialized = true;
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
}
