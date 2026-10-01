// ---------------------------------------------------------------------------
// Where the SDK points, resolved once.
//
// Mirrors resolveHostedOptions in sdk/core/src/hosted.ts. `initialize()` turns
// the raw options into a [ResolvedHosted] carrying a single mode, and every URL
// builder and credential call site reads that value. Nothing re-derives the
// mode from raw options, so the rules below are decided here and nowhere else.
// See docs/decisions/sdk-real-pairing-testing-2026-10-01.md.
// ---------------------------------------------------------------------------

import 'package:flutter/foundation.dart';

import 'constants.dart';

/// Explicit opt-in configuration for local or sandbox testing.
///
/// Production integrations must use the hosted defaults. This object is the
/// only way to point the SDK at a different URL or provide a test credential.
class UnsafeTesting {
  const UnsafeTesting({
    this.breakUrl,
    this.apiUrl,
    this.token,
    this.realPairing = false,
  });

  final String? breakUrl;
  final String? apiUrl;
  final String? token;

  /// Run the real pairing path (pairing, a learner session, the learner's own
  /// curriculum) against a local hosted page instead of sandbox content.
  ///
  /// Debug builds only. [breakUrl] must be `http://localhost:<port>/…` or
  /// `http://127.0.0.1:<port>/…`. Takes no [token] and no [apiUrl]: the page
  /// pairs the device itself and owns its API destination.
  final bool realPairing;
}

/// How the SDK is pointed.
enum LevelMomentHostedMode {
  /// The hosted Level Moment destination.
  production,

  /// A test URL serving sandbox content, with no stored credential.
  sandbox,

  /// A loopback page running the real pairing path, with credentials kept in
  /// a test slot apart from production.
  realPairing,
}

/// Hosts a game may pair against with [UnsafeTesting.realPairing]. Always
/// `http:` with an explicit port. Mirrors `REAL_PAIRING_HOSTS` in sdk/core.
const List<String> kRealPairingHosts = ['localhost', '127.0.0.1'];

/// Characters a real-pairing URL may not contain: a backslash, `@`, and
/// anything outside printable ASCII (which covers whitespace).
final RegExp _forbiddenRealPairingChars = RegExp(r'[\\@]|[^\x21-\x7e]');

/// The host as written, before any parser normalises it. `127.1` must be
/// refused here because some parsers expand it to `127.0.0.1`, and every SDK
/// must give the same answer for the same string.
final RegExp _literalHttpHost =
    RegExp(r'^http://([^/:]+):', caseSensitive: false);

/// Parse a loopback URL the way real pairing allows it, or return null.
Uri? _parseLoopbackHttp(String raw) {
  if (_forbiddenRealPairingChars.hasMatch(raw)) return null;
  final literal = _literalHttpHost.firstMatch(raw);
  if (literal == null ||
      !kRealPairingHosts.contains(literal.group(1)!.toLowerCase())) {
    return null;
  }
  final uri = Uri.tryParse(raw);
  if (uri == null ||
      uri.scheme != 'http' ||
      !kRealPairingHosts.contains(uri.host.toLowerCase()) ||
      !uri.hasPort ||
      uri.port < 1 ||
      uri.port > 65535 ||
      uri.userInfo.isNotEmpty ||
      uri.hasQuery ||
      uri.hasFragment) {
    return null;
  }
  return uri.replace(
    host: uri.host.toLowerCase(),
    path: uri.path.isEmpty ? '/' : uri.path,
  );
}

/// True when [raw] is a URL real pairing may load.
bool isRealPairingUrl(String raw) => _parseLoopbackHttp(raw) != null;

/// Whether [token] is a sandbox developer token.
bool isSandboxTokenValue(String token) => token.startsWith('eply_sbx_');

/// Hosted options after validation. Only [resolveHostedOptions] builds one.
class ResolvedHosted {
  const ResolvedHosted._({
    required this.mode,
    required this.breakUrl,
    required this.apiUrl,
    required this.token,
    required this.mock,
  });

  final LevelMomentHostedMode mode;

  /// The `/break` URL to load. Under real pairing, the re-serialised URL, not
  /// the caller's string.
  final String breakUrl;

  /// Null under real pairing: the hosted page uses its own API base.
  final String? apiUrl;

  /// The configured sandbox token. Always null outside sandbox mode.
  final String? token;

  final bool mock;

  /// The origin of [breakUrl]. Credential replies are bound to it and the
  /// secure-store slot is keyed on it.
  String get origin => levelMomentOrigin(breakUrl);

  /// Whether the break URL carries `sandbox=true`.
  bool get sandbox => mode == LevelMomentHostedMode.sandbox;

  /// Whether the native secure store is read and written. Sandbox runs and
  /// bundled mock questions never touch it.
  bool get usesCredentialStore =>
      mode != LevelMomentHostedMode.sandbox && !mock;
}

final Set<String> _warnedOrigins = <String>{};

Never _refuseRealPairing(String reason) =>
    throw ArgumentError('unsafeTesting.realPairing: $reason');

/// Lets a test run the release-build refusal from a debug test run.
///
/// It can only narrow: setting it to `false` makes a debug build behave like a
/// release build for real pairing, and nothing can make a profile or release
/// build look like a debug one, because the check reads `kDebugMode` first.
@visibleForTesting
bool? debugBuildOverride;

/// Whether real pairing may run: `kDebugMode`, narrowed by
/// [debugBuildOverride] in tests.
bool get _isDebugBuild => kDebugMode && (debugBuildOverride ?? true);

/// Resolve the options `initialize()` receives. Not exported from the
/// package.
ResolvedHosted resolveHostedOptions({
  String? apiUrl,
  String? breakUrl,
  bool mock = false,
  UnsafeTesting? unsafeTesting,
}) {
  if (unsafeTesting != null && unsafeTesting.realPairing) {
    if (!_isDebugBuild) _refuseRealPairing('only available in a debug build');
    if (mock) _refuseRealPairing('cannot be combined with mock');
    if (unsafeTesting.token != null) {
      _refuseRealPairing('takes no token; the page pairs the device itself');
    }
    if (unsafeTesting.apiUrl != null || apiUrl != null) {
      _refuseRealPairing('takes no apiUrl; the page uses its own API');
    }
    if (breakUrl != null) {
      _refuseRealPairing('set unsafeTesting.breakUrl, not breakUrl');
    }
    final raw = unsafeTesting.breakUrl;
    final url = raw == null ? null : _parseLoopbackHttp(raw);
    if (url == null) {
      _refuseRealPairing(
        'breakUrl must be http://localhost:<port>/… or http://127.0.0.1:<port>/…',
      );
    }
    final resolved = ResolvedHosted._(
      mode: LevelMomentHostedMode.realPairing,
      breakUrl: url.toString(),
      apiUrl: null,
      token: null,
      mock: false,
    );
    if (_warnedOrigins.add(resolved.origin)) {
      debugPrint(
        '[LevelMoment] unsafeTesting.realPairing is on: pairing against '
        '${resolved.origin}, not Level Moment. Remove it before release.',
      );
    }
    return resolved;
  }

  final effectiveApiUrl = unsafeTesting?.apiUrl ?? apiUrl ?? kLevelMomentApiUrl;
  final effectiveBreakUrl =
      unsafeTesting?.breakUrl ?? breakUrl ?? kLevelMomentBreakUrl;
  _validateEndpoint(
      effectiveApiUrl, kLevelMomentApiUrl, 'apiUrl', unsafeTesting);
  _validateEndpoint(
      effectiveBreakUrl, kLevelMomentBreakUrl, 'breakUrl', unsafeTesting);
  final token = unsafeTesting?.token;
  if (token != null && !isSandboxTokenValue(token)) {
    throw ArgumentError.value(
      token,
      'unsafeTesting.token',
      'Test credentials must start with eply_sbx_.',
    );
  }
  return ResolvedHosted._(
    mode: unsafeTesting == null
        ? LevelMomentHostedMode.production
        : LevelMomentHostedMode.sandbox,
    breakUrl: effectiveBreakUrl,
    apiUrl: effectiveApiUrl,
    token: token,
    mock: mock,
  );
}

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

/// The hosted surface a URL builder targets.
enum HostedSurface { breakPage, access }

/// The URL of [surface] on the resolved origin. Production always uses the
/// canonical pages; a test origin keeps its origin and changes only the path.
String hostedSurfaceUrl(ResolvedHosted hosted, HostedSurface surface) {
  if (surface == HostedSurface.breakPage) return hosted.breakUrl;
  if (hosted.breakUrl == kLevelMomentBreakUrl) return kLevelMomentAccessUrl;
  final parsed = Uri.tryParse(hosted.breakUrl);
  if (parsed == null || !parsed.hasScheme || !parsed.hasAuthority) {
    throw ArgumentError.value(
        hosted.breakUrl, 'breakUrl', 'Must be an absolute URL.');
  }
  return parsed
      .replace(path: '/access', query: null, fragment: null)
      .toString();
}
