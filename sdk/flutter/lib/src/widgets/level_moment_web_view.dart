// LevelMomentWebView — fullscreen WebView host for the Level Moment experience.
//
// Pushed as a fullscreen route by LevelMomentRewardedAd.show(). All question UI
// lives in the hosted /break page (platform/web/app/break); this widget just
// owns the native fullscreen presentation and bridges the page's postMessage
// events back to LevelMomentRewardedAd.
//
// The hosted page calls window.ReactNativeWebView.postMessage(json) first if
// that object exists. A JavaScript channel named exactly `ReactNativeWebView`
// therefore makes the existing page work UNMODIFIED — same bridge name the
// react-native SDK relies on. See docs/ADR-001-webview-rendering.md.

import 'dart:async';
import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:url_launcher/url_launcher.dart';
import 'package:webview_flutter/webview_flutter.dart';

import '../constants.dart';

/// Pre-`ready` watchdog deadline, mirroring the web, RN, and Unity SDKs: a
/// crashed or unreachable hosted page must never cover the game forever. After
/// `ready` there is no timeout — a student thinking through a quiz, or a parent
/// walking to another room to scan a code, is never force-closed.
const Duration kBreakLoadTimeout = Duration(seconds: 15);

// ---------------------------------------------------------------------------
// HostMessage — mirrors the `HostMessage` union in
// sdk/react-native/src/LevelMomentAd.ts. The page posts JSON shaped as:
//   { type: "ready" }
//   { type: "earnedReward", payload: { amount: 0 | 1 } }
//   { type: "signedIn" }            // sign-in gate only
//   { type: "dismissed" }
//   { type: "error", payload: { code, message } }
//   { type: "needCredential" }      // asking this host for a credential
//   { type: "credentialIssued", payload: { token } }
//   { type: "credentialInvalid" }
//   { type: "openExternal", payload: { url } }
// ---------------------------------------------------------------------------

/// Base type for messages posted by the hosted break page.
sealed class HostMessage {
  const HostMessage();

  /// Parse a raw JSON string from the page bridge. Returns null if the
  /// payload is malformed or of an unknown type (mirrors the RN SDK, which
  /// silently drops unparseable messages).
  static HostMessage? tryParse(String raw) {
    Object? decoded;
    try {
      decoded = json.decode(raw);
    } catch (_) {
      return null;
    }
    if (decoded is! Map<String, dynamic>) return null;
    final protocolVersion = decoded['protocolVersion'];
    if (protocolVersion != null &&
        protocolVersion != kLevelMomentProtocolVersion) {
      return null;
    }
    final type = decoded['type'];
    switch (type) {
      case 'ready':
        return const Ready();
      case 'earnedReward':
        final payload = decoded['payload'];
        if (payload is! Map<String, dynamic>) return null;
        final amount = payload['amount'];
        if (amount is! num || (amount != 0 && amount != 1)) return null;
        final rawRewardId = payload['rewardId'];
        if (rawRewardId != null &&
            (rawRewardId is! String ||
                rawRewardId.isEmpty ||
                rawRewardId.length > 256)) return null;
        return EarnedReward(amount.toInt(), rawRewardId as String?);
      case 'signedIn':
        return const SignedIn();
      case 'dismissed':
        return const Dismissed();
      case 'error':
        final payload = decoded['payload'];
        final p = payload is Map<String, dynamic> ? payload : const {};
        if (p['code'] != null && p['code'] is! String) return null;
        if (p['message'] != null && p['message'] is! String) return null;
        return ErrorMsg(
          (p['code'] as String?) ?? 'unknown',
          (p['message'] as String?) ?? 'Unknown error',
        );
      case 'needCredential':
        return const NeedCredential();
      case 'credentialIssued':
        final payload = decoded['payload'];
        final token = payload is Map<String, dynamic> ? payload['token'] : null;
        if (token is! String || token.isEmpty) return null;
        return CredentialIssued(token);
      case 'credentialInvalid':
        return const CredentialInvalid();
      case 'openExternal':
        final payload = decoded['payload'];
        final url = payload is Map<String, dynamic> ? payload['url'] : null;
        return url is String ? OpenExternal(url) : null;
      default:
        return null;
    }
  }
}

/// The page has mounted and is rendering.
class Ready extends HostMessage {
  const Ready();
}

/// The student answered. `amount` is 1 for a correct answer, 0 otherwise.
/// May fire multiple times within a single break session.
class EarnedReward extends HostMessage {
  final int amount;
  final String? rewardId;
  const EarnedReward(this.amount, [this.rewardId]);
}

/// Sign-in gate only: this device holds a valid credential for the game.
/// Terminal — ends the gate exactly once.
class SignedIn extends HostMessage {
  const SignedIn();
}

/// The break is over. Terminal — fires the dismiss path exactly once.
class Dismissed extends HostMessage {
  const Dismissed();
}

/// The page failed to load or show. Terminal — fires the dismiss path once.
class ErrorMsg extends HostMessage {
  final String code;
  final String message;
  const ErrorMsg(this.code, this.message);
}

/// The page wants the credential this host holds, instead of reading one off
/// its URL. Answered by [LevelMomentWebView.onNeedCredential]. Not terminal.
class NeedCredential extends HostMessage {
  const NeedCredential();
}

/// Pairing minted a credential for this device and game. Only reaches a host
/// that asked for custody — this SDK does, because its secure store outlives
/// the WebView's own. Not terminal: the break carries on afterwards.
class CredentialIssued extends HostMessage {
  final String token;
  const CredentialIssued(this.token);
}

/// The page threw its stored credential away after the server refused it. Drop
/// the secure-store copy too, or the next launch hands the same dead value
/// straight back. Not terminal.
class CredentialInvalid extends HostMessage {
  const CredentialInvalid();
}

/// Open [url] in the device's browser, outside this WebView. The page asks for
/// this when a parent chooses to approve the game from a browser rather than
/// scan the code. Parent sign-in happens outside the WebView because the game
/// can inspect that surface; never collect a Level Moment email code or other
/// parent credential inside it (RFC 8252). Not terminal: the break stays open
/// and keeps polling, so nothing has to navigate back.
class OpenExternal extends HostMessage {
  final String url;
  const OpenExternal(this.url);

  /// The address to hand the OS, or null when it does not belong to
  /// [hostedUrl]'s origin.
  ///
  /// The origin check is the guard, and it has to be the origin rather than
  /// just the scheme. This bridge belongs to whatever the WebView is currently
  /// showing: a redirect that took it elsewhere keeps posting messages, and
  /// `launchUrl` will full-screen whatever it is handed — a page asking for a
  /// Level Moment sign-in code in the device's own browser is precisely what a
  /// phishing site wants. Pinned to the hosted page's origin, the worst a wrong
  /// URL can do is nothing.
  ///
  /// Comparing origins also settles the scheme: http passes only when the game
  /// is configured against an http `breakUrl`, which is local development.
  Uri? launchableFrom(String hostedUrl) {
    final parsed = Uri.tryParse(url);
    final hosted = Uri.tryParse(hostedUrl);
    if (parsed == null || hosted == null) return null;
    if (!parsed.hasScheme ||
        !parsed.hasAuthority ||
        !hosted.hasScheme ||
        !hosted.hasAuthority ||
        parsed.userInfo.isNotEmpty ||
        hosted.userInfo.isNotEmpty) return null;
    if (parsed.scheme != hosted.scheme) return null;
    if (parsed.host != hosted.host) return null;
    // `port` resolves the scheme default (443/80), so https://a and
    // https://a:443 compare equal while https://a:8443 does not.
    if (parsed.port != hosted.port) return null;
    return parsed;
  }
}

/// What this shell tells the page it can do, on the URL it loads.
///
/// The page has to decide whether to offer "Approve in your browser" before it
/// can know anything about the shell around it, and a shell built before
/// [OpenExternal] existed drops the message unparsed — a button that silently
/// does nothing, next to a QR code that works. So every break and gate URL this
/// SDK builds names the capability, and a page loaded by an older build sees no
/// `caps` at all and offers the QR alone. Comma-separated, so adding a second
/// capability changes only this constant.
const String kHostCapabilitiesParam = 'caps';
const String kHostCapabilities = 'openExternal';

/// What a host answers [NeedCredential] with.
class CredentialReply {
  const CredentialReply({
    required this.token,
    required this.custody,
    this.customData,
    this.origin = 'https://levelmoment.com',
  });

  /// The credential this host holds for the placement, or `''` if none.
  final String token;

  /// Ask to be told about credentials the page mints or discards.
  final bool custody;
  final String? customData;
  final String origin;

  /// The JavaScript that hands this reply to the page.
  ///
  /// The reply is JSON-encoded and the encoding is the whole safety argument:
  /// a token is an opaque server-issued string, but it comes from outside this
  /// class, and interpolating it raw into a script would make any quote or
  /// backslash in it executable code inside the page. Encoding the reply and
  /// then encoding THAT as a string literal the page parses cannot escape its
  /// literal.
  String toInjection() {
    final inner = json.encode({
      'token': token,
      'custody': custody,
      'customData': customData,
      'protocolVersion': kLevelMomentProtocolVersion,
      'sdkVersion': kLevelMomentSdkVersion,
    });
    final literal = json.encode(inner);
    final originLiteral = json.encode(origin);
    return 'if (window.location.origin === $originLiteral) {'
        'window.__levelMomentDeliverCredential && '
        'window.__levelMomentDeliverCredential($literal);}'
        ';';
  }
}

// ---------------------------------------------------------------------------
// LevelMomentWebView — mirrors sdk/react-native/src/LevelMomentAdModal.tsx
// ---------------------------------------------------------------------------

/// Fullscreen WebView that loads [url] and forwards page messages to
/// [onMessage]. Renders a loading spinner until the page posts `ready`, and a
/// top-right close button that synthesizes a terminal [Dismissed].
///
/// With [hidden] true it renders nothing at all — no scaffold, no chrome, no
/// space in the layout — while still loading the page. That is what the
/// headless credential check needs: the page has an answer to post, but nothing
/// to show. A hidden view never touches [Navigator]; its owner decides when to
/// take it out of the tree.
class LevelMomentWebView extends StatefulWidget {
  final String url;
  final void Function(HostMessage message) onMessage;

  /// Render off-screen with no chrome. Used by the headless credential check.
  final bool hidden;

  /// Pre-`ready` watchdog window.
  final Duration loadTimeout;

  /// The page never posted `ready` within [loadTimeout]. When null the view
  /// falls back to the dismiss path, which is what a break wants: the game
  /// resumes as if the break had ended normally. The sign-in gate supplies this
  /// so a page that never came up reads as a technical failure rather than as a
  /// person saying no.
  final void Function()? onLoadTimeout;

  /// Answer the page's `needCredential`. Resolve with the credential this
  /// caller holds — or an empty token, promptly, when it holds none: the page
  /// waits only briefly before falling back to its own storage.
  ///
  /// It lives here rather than in [onMessage] because only this widget holds
  /// the controller that can run script inside the page.
  final Future<CredentialReply> Function()? onNeedCredential;

  const LevelMomentWebView({
    super.key,
    required this.url,
    required this.onMessage,
    this.hidden = false,
    this.loadTimeout = kBreakLoadTimeout,
    this.onLoadTimeout,
    this.onNeedCredential,
  });

  @override
  State<LevelMomentWebView> createState() => _LevelMomentWebViewState();
}

class _LevelMomentWebViewState extends State<LevelMomentWebView> {
  static const _backgroundColor = Color(0xFF060A14);

  late final WebViewController _controller;
  late final String _loadedUrl;
  Timer? _watchdog;
  bool _pageReady = false;
  int _navigationGeneration = 0;
  int _credentialRequestGeneration = 0;

  // Mirrors LevelMomentAdModal's dismissedRef: dismissed/error are terminal and
  // the dismiss path fires exactly once even if a second terminal arrives.
  bool _dismissed = false;

  @override
  void initState() {
    super.initState();
    _loadedUrl = widget.url;
    _watchdog = Timer(widget.loadTimeout, _onWatchdogFire);
    _controller = WebViewController()
      ..setJavaScriptMode(JavaScriptMode.unrestricted)
      ..setBackgroundColor(_backgroundColor)
      ..setNavigationDelegate(
        NavigationDelegate(
          onPageStarted: (_) => _navigationGeneration++,
          onNavigationRequest: (request) => _sameOrigin(request.url, _loadedUrl)
              ? NavigationDecision.navigate
              : NavigationDecision.prevent,
          onWebResourceError: _onWebResourceError,
        ),
      )
      ..addJavaScriptChannel(
        'ReactNativeWebView',
        onMessageReceived: _onChannelMessage,
      )
      ..loadRequest(Uri.parse(widget.url));
  }

  void _onWatchdogFire() {
    if (_pageReady || _dismissed) return;
    final onLoadTimeout = widget.onLoadTimeout;
    if (onLoadTimeout != null) {
      _dismissed = true;
      onLoadTimeout();
      return;
    }
    _closeRoute();
  }

  /// A failed load of the page itself is terminal. Subresource errors after
  /// `ready` are the page's own problem and are ignored.
  void _onWebResourceError(WebResourceError error) {
    if (_pageReady || _dismissed) return;
    if (error.isForMainFrame == false) return;
    _notifyTerminal(ErrorMsg('network_error', error.description));
    _closeRoute();
  }

  void _closeRoute() {
    if (widget.hidden) {
      _notifyTerminal(const Dismissed());
      return;
    }
    if (mounted && Navigator.of(context).canPop()) {
      Navigator.of(context).pop(); // dispose() delivers the terminal
    } else {
      _notifyTerminal(const Dismissed());
    }
  }

  void _onChannelMessage(JavaScriptMessage message) {
    unawaited(_handleChannelMessage(message));
  }

  Future<void> _handleChannelMessage(JavaScriptMessage message) async {
    if (_dismissed) return;
    final navigationGeneration = _navigationGeneration;
    final currentUrl = await _controller.currentUrl();
    if (_dismissed ||
        !mounted ||
        widget.url != _loadedUrl ||
        navigationGeneration != _navigationGeneration ||
        currentUrl == null ||
        !_sameOrigin(currentUrl, _loadedUrl)) return;
    final parsed = HostMessage.tryParse(message.message);
    if (parsed == null) return;

    if (parsed is Ready) {
      _watchdog?.cancel();
      if (mounted) setState(() => _pageReady = true);
      widget.onMessage(parsed);
      return;
    }

    if (parsed is NeedCredential) {
      // Answer by running script in the page. Reading the secure store is
      // asynchronous — by the time it resolves this view may already be gone,
      // so re-check before injecting into a controller nobody is watching.
      final ask = widget.onNeedCredential;
      if (ask != null) {
        final generation = ++_credentialRequestGeneration;
        final requestedNavigationGeneration = _navigationGeneration;
        unawaited(ask().then((reply) async {
          final injectionUrl = await _controller.currentUrl();
          if (_dismissed ||
              !mounted ||
              widget.url != _loadedUrl ||
              generation != _credentialRequestGeneration ||
              requestedNavigationGeneration != _navigationGeneration ||
              injectionUrl == null ||
              !_sameOrigin(injectionUrl, _loadedUrl)) return;
          _controller.runJavaScript(reply.toInjection());
        }).catchError((Object _) {
          // A store that will not answer leaves the page on its own stored
          // credential, which is where it was before this bridge existed.
        }));
      }
      widget.onMessage(parsed);
      return;
    }

    if (parsed is OpenExternal) {
      // Judged against the URL this view was opened with, so a WebView that
      // wandered off cannot send a parent anywhere but the hosted origin.
      final target = parsed.launchableFrom(widget.url);
      if (target != null) {
        // Out to the OS, never into this WebView. The surface stays open behind
        // the browser and keeps polling, so a parent who never comes back to
        // the app still connects the game.
        unawaited(launchUrl(target, mode: LaunchMode.externalApplication)
            .catchError((Object _) {
          // No browser took it. The pairing card still shows the code and the
          // QR, which is the path that needs no browser on this device at all.
          return false;
        }));
      }
      widget.onMessage(parsed);
      return;
    }

    if (parsed is Dismissed || parsed is ErrorMsg || parsed is SignedIn) {
      // Page-driven terminal: notify (guarded) then pop the route. The pop
      // re-enters dispose(), but the guard makes the second notify a no-op.
      // A hidden view owns no route — its owner removes it.
      _notifyTerminal(parsed);
      if (!widget.hidden && mounted && Navigator.of(context).canPop()) {
        Navigator.of(context).pop();
      }
      return;
    }

    // EarnedReward (non-terminal) — forward as-is, may repeat.
    widget.onMessage(parsed);
  }

  bool _sameOrigin(String first, String second) {
    final a = Uri.tryParse(first);
    final b = Uri.tryParse(second);
    if (a == null ||
        b == null ||
        !a.hasScheme ||
        !a.hasAuthority ||
        !b.hasScheme ||
        !b.hasAuthority ||
        a.userInfo.isNotEmpty ||
        b.userInfo.isNotEmpty) return false;
    return a.scheme.toLowerCase() == b.scheme.toLowerCase() &&
        a.host.toLowerCase() == b.host.toLowerCase() &&
        a.port == b.port;
  }

  /// Forward a terminal message to the ad, exactly once. Safe to call from
  /// dispose() — does not touch Navigator.
  void _notifyTerminal(HostMessage message) {
    if (_dismissed) return;
    _dismissed = true;
    widget.onMessage(message);
  }

  /// Close button: pop the route. The terminal notification is fired from
  /// [dispose], so EVERY exit path (close button, system back gesture,
  /// programmatic pop, page-posted dismissed/error) notifies the ad exactly
  /// once — no version-sensitive PopScope callback needed.
  void _onClosePressed() {
    if (mounted && Navigator.of(context).canPop()) {
      Navigator.of(context).pop();
    }
  }

  @override
  void dispose() {
    _watchdog?.cancel();
    // Whatever removed this route, guarantee the ad sees a terminal exactly
    // once. No-ops if a page-posted dismissed/error already fired. Delivered
    // on a microtask: dispose() runs while the widget tree is locked, and a
    // consumer calling setState from its dismiss callback would throw
    // "setState() called when widget tree was locked" and lose the event.
    if (!_dismissed) {
      _dismissed = true;
      final onMessage = widget.onMessage;
      scheduleMicrotask(() => onMessage(const Dismissed()));
    }
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    if (widget.hidden) {
      // Offstage keeps the platform view mounted and loading while taking no
      // space, painting nothing, and answering no hit tests — the Flutter
      // analogue of the web adapter's 0x0 iframe.
      return Offstage(
        child: SizedBox(
          width: 1,
          height: 1,
          child: WebViewWidget(controller: _controller),
        ),
      );
    }
    return Scaffold(
      backgroundColor: _backgroundColor,
      body: Stack(
        children: [
          Positioned.fill(
            child: WebViewWidget(controller: _controller),
          ),
          if (!_pageReady)
            const Positioned.fill(
              child: ColoredBox(
                color: _backgroundColor,
                child: Center(
                  child: CircularProgressIndicator(
                    valueColor: AlwaysStoppedAnimation<Color>(
                      Color(0xFF3B82F6),
                    ),
                  ),
                ),
              ),
            ),
          Positioned(
            top: 48,
            right: 16,
            child: Semantics(
              label: 'Close',
              button: true,
              child: GestureDetector(
                onTap: _onClosePressed,
                child: Container(
                  width: 40,
                  height: 40,
                  decoration: const BoxDecoration(
                    shape: BoxShape.circle,
                    color: Color(0xD90F172A),
                  ),
                  alignment: Alignment.center,
                  child: const Text(
                    '×',
                    style: TextStyle(
                      color: Color(0xFFE2E8F0),
                      fontSize: 24,
                      height: 26 / 24,
                      fontWeight: FontWeight.w300,
                    ),
                  ),
                ),
              ),
            ),
          ),
        ],
      ),
    );
  }
}
