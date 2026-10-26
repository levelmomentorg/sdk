// ---------------------------------------------------------------------------
// Require-internet mode for the Flutter adapter.
//
// "Offline" means the operating system reports no network connection: Wi-Fi
// and mobile data both off, or airplane mode. It never means Level Moment's
// servers failed. An outage on our side ends the break as a load failure and
// the game keeps playing: an outage on our side must never lock a game, so
// nothing here ever blocks on an API error.
//
// The OS answer comes from connectivity_plus, the Flutter Community plugin
// that wraps Android's ConnectivityManager and iOS's NWPathMonitor. It reports
// which interfaces are up, not whether a server answers, which is exactly the
// distinction this mode needs.
//
// Every failure to read that state reads as online: a plugin that is missing
// (a unit test, an unsupported platform) or throws must never lock a game.
// ---------------------------------------------------------------------------

import 'dart:async';

import 'package:connectivity_plus/connectivity_plus.dart';
import 'package:flutter/widgets.dart';

/// Whether the OS-reported interfaces in [results] add up to "no connection".
/// An empty list says nothing and reads as online.
@visibleForTesting
bool connectivityMeansOffline(List<ConnectivityResult> results) =>
    results.isNotEmpty &&
    results.every((result) => result == ConnectivityResult.none);

/// The device's connection state as the operating system reports it.
///
/// [LevelMomentAds.initialize] starts it. Every hosted surface checks it
/// before it mounts a WebView: while the OS reports no connection, a break,
/// the sign-in gate and the access checks fail at once instead of showing a
/// page that cannot load.
class LevelMomentConnection {
  LevelMomentConnection._();

  /// The one instance the SDK reads.
  static final LevelMomentConnection instance = LevelMomentConnection._();

  final StreamController<bool> _changes = StreamController<bool>.broadcast();
  StreamSubscription<List<ConnectivityResult>>? _subscription;
  bool _online = true;

  /// Reads the current OS state. Replaced in tests, which have no plugin.
  @visibleForTesting
  Future<List<ConnectivityResult>> Function() checkConnectivity =
      () => Connectivity().checkConnectivity();

  /// OS state changes. Replaced in tests.
  @visibleForTesting
  Stream<List<ConnectivityResult>> Function() connectivityChanges =
      () => Connectivity().onConnectivityChanged;

  /// False only while the operating system reports no network connection.
  bool get isOnline => _online;

  /// `true` when a connection returns, `false` when it goes. Emits only on a
  /// change.
  Stream<bool> get changes => _changes.stream;

  /// Start following the OS state. Idempotent.
  void start() {
    if (_subscription != null) return;
    try {
      _subscription = connectivityChanges().listen(
        (results) => _set(!connectivityMeansOffline(results)),
        // A plugin error says nothing about the connection.
        onError: (Object _) {},
      );
    } catch (_) {
      // No plugin on this platform: stay online.
      return;
    }
    unawaited(refresh());
  }

  /// Ask the OS again, as Try again does, and return whether it reports a
  /// connection.
  Future<bool> refresh() async {
    try {
      _set(!connectivityMeansOffline(await checkConnectivity()));
    } catch (_) {
      // Leave the last known state alone.
    }
    return _online;
  }

  /// Set the state directly. Tests only.
  @visibleForTesting
  void debugSetOnline(bool online) => _set(online);

  /// Stop following the OS state and read online again. Tests only.
  @visibleForTesting
  Future<void> debugReset() async {
    await _subscription?.cancel();
    _subscription = null;
    _online = true;
    checkConnectivity = () => Connectivity().checkConnectivity();
    connectivityChanges = () => Connectivity().onConnectivityChanged;
  }

  void _set(bool online) {
    if (online == _online) return;
    _online = online;
    _changes.add(online);
  }
}

/// The copy on the no-connection message.
@visibleForTesting
({String heading, String body, String button}) noConnectionCopy(
    String? gameName) {
  final trimmed = gameName?.trim() ?? '';
  final name = trimmed.isEmpty ? 'This game' : trimmed;
  return (
    heading: 'No internet connection',
    body: '$name needs an internet connection. '
        'Turn on Wi-Fi or mobile data, then try again.',
    button: 'Try again',
  );
}

/// Blocks play with a full-screen message while the device has no internet
/// connection, and removes it when the connection returns.
///
/// Wrap your app's content in it, below the [MaterialApp] so every route,
/// Level Moment's breaks included, sits underneath:
///
/// ```dart
/// MaterialApp(
///   builder: (context, child) => LevelMomentConnectionGate(
///     gameName: 'YOUR_GAME_NAME',
///     onConnectionChange: (online) => online ? game.resume() : game.pause(),
///     child: child!,
///   ),
///   home: const GameScreen(),
/// );
/// ```
///
/// Only the operating system's connection state counts. When the device is
/// online and Level Moment's servers fail, nothing is blocked: the break ends
/// and the game carries on.
class LevelMomentConnectionGate extends StatefulWidget {
  const LevelMomentConnectionGate({
    super.key,
    required this.child,
    this.gameName,
    this.onConnectionChange,
  });

  /// The app, shown underneath the message.
  final Widget child;

  /// The game's name as players know it: "{gameName} needs an internet
  /// connection." Defaults to "This game".
  final String? gameName;

  /// Called with `false` when the message appears and `true` when it goes
  /// away. Pause your game loop on `false`.
  final ValueChanged<bool>? onConnectionChange;

  @override
  State<LevelMomentConnectionGate> createState() =>
      _LevelMomentConnectionGateState();
}

class _LevelMomentConnectionGateState extends State<LevelMomentConnectionGate> {
  StreamSubscription<bool>? _subscription;
  late bool _online;
  bool _checking = false;

  LevelMomentConnection get _connection => LevelMomentConnection.instance;

  @override
  void initState() {
    super.initState();
    _connection.start();
    _online = _connection.isOnline;
    _subscription = _connection.changes.listen(_onChange);
    if (!_online) {
      // After the first frame, so a game that pauses from the callback has
      // finished building.
      WidgetsBinding.instance.addPostFrameCallback((_) {
        if (mounted && !_online) widget.onConnectionChange?.call(false);
      });
    }
  }

  void _onChange(bool online) {
    if (!mounted || online == _online) return;
    setState(() => _online = online);
    widget.onConnectionChange?.call(online);
  }

  Future<void> _retry() async {
    if (_checking) return;
    setState(() => _checking = true);
    await _connection.refresh();
    if (mounted) setState(() => _checking = false);
  }

  @override
  void dispose() {
    _subscription?.cancel();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return Stack(
      textDirection: TextDirection.ltr,
      fit: StackFit.passthrough,
      children: [
        widget.child,
        if (!_online)
          Positioned.fill(
            child: _NoConnectionMessage(
              copy: noConnectionCopy(widget.gameName),
              checking: _checking,
              onRetry: _retry,
            ),
          ),
      ],
    );
  }
}

class _NoConnectionMessage extends StatelessWidget {
  const _NoConnectionMessage({
    required this.copy,
    required this.checking,
    required this.onRetry,
  });

  final ({String heading, String body, String button}) copy;
  final bool checking;
  final VoidCallback onRetry;

  @override
  Widget build(BuildContext context) {
    // Above any MaterialApp theme, so it reads the platform setting itself.
    final dark =
        (MediaQuery.maybePlatformBrightnessOf(context) ?? Brightness.dark) ==
            Brightness.dark;
    final background = dark ? const Color(0xFF0B0618) : const Color(0xFFFFFFFF);
    final text = dark ? const Color(0xFFF8FAFC) : const Color(0xFF0F172A);
    final muted = dark ? const Color(0xFFCBD5E1) : const Color(0xFF334155);
    final padding = MediaQuery.maybePaddingOf(context) ?? EdgeInsets.zero;

    return Directionality(
      textDirection: Directionality.maybeOf(context) ?? TextDirection.ltr,
      // Opaque to hits: no tap reaches the game underneath.
      child: GestureDetector(
        behavior: HitTestBehavior.opaque,
        onTap: () {},
        child: ColoredBox(
          color: background,
          child: Padding(
            padding: padding + const EdgeInsets.all(24),
            child: Center(
              child: ConstrainedBox(
                constraints: const BoxConstraints(maxWidth: 420),
                child: Semantics(
                  scopesRoute: true,
                  explicitChildNodes: true,
                  child: Column(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      Semantics(
                        header: true,
                        liveRegion: true,
                        child: Text(
                          copy.heading,
                          textAlign: TextAlign.center,
                          style: TextStyle(
                            color: text,
                            fontSize: 24,
                            height: 1.25,
                            fontWeight: FontWeight.w700,
                            decoration: TextDecoration.none,
                          ),
                        ),
                      ),
                      const SizedBox(height: 12),
                      Text(
                        copy.body,
                        textAlign: TextAlign.center,
                        style: TextStyle(
                          color: muted,
                          fontSize: 16,
                          height: 1.5,
                          fontWeight: FontWeight.w400,
                          decoration: TextDecoration.none,
                        ),
                      ),
                      const SizedBox(height: 24),
                      Semantics(
                        button: true,
                        enabled: !checking,
                        child: GestureDetector(
                          onTap: checking ? null : onRetry,
                          child: Opacity(
                            opacity: checking ? 0.6 : 1,
                            child: Container(
                              constraints: const BoxConstraints(
                                minWidth: 160,
                                minHeight: 48,
                              ),
                              padding: const EdgeInsets.symmetric(
                                horizontal: 24,
                                vertical: 12,
                              ),
                              alignment: Alignment.center,
                              decoration: BoxDecoration(
                                color: text,
                                borderRadius: BorderRadius.circular(999),
                              ),
                              child: Text(
                                copy.button,
                                style: TextStyle(
                                  color: background,
                                  fontSize: 16,
                                  fontWeight: FontWeight.w600,
                                  decoration: TextDecoration.none,
                                ),
                              ),
                            ),
                          ),
                        ),
                      ),
                    ],
                  ),
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }
}
