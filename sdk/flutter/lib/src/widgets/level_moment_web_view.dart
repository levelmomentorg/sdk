// LevelMomentWebView — fullscreen WebView host for the educational break page.
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

import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:webview_flutter/webview_flutter.dart';

// ---------------------------------------------------------------------------
// HostMessage — mirrors the `HostMessage` union in
// sdk/react-native/src/LevelMomentAd.ts. The page posts JSON shaped as:
//   { type: "ready" }
//   { type: "earnedReward", payload: { amount: 0 | 1 } }
//   { type: "dismissed" }
//   { type: "error", payload: { code, message } }
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
    final type = decoded['type'];
    switch (type) {
      case 'ready':
        return const Ready();
      case 'earnedReward':
        final payload = decoded['payload'];
        final amount = payload is Map<String, dynamic> ? payload['amount'] : null;
        return EarnedReward(amount is num ? amount.toInt() : 0);
      case 'dismissed':
        return const Dismissed();
      case 'error':
        final payload = decoded['payload'];
        final p = payload is Map<String, dynamic> ? payload : const {};
        return ErrorMsg(
          (p['code'] as String?) ?? 'unknown',
          (p['message'] as String?) ?? 'Unknown error',
        );
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
  const EarnedReward(this.amount);
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

// ---------------------------------------------------------------------------
// LevelMomentWebView — mirrors sdk/react-native/src/LevelMomentAdModal.tsx
// ---------------------------------------------------------------------------

/// Fullscreen WebView that loads [url] and forwards page messages to
/// [onMessage]. Renders a loading spinner until the page posts `ready`, and a
/// top-right close button that synthesizes a terminal [Dismissed].
class LevelMomentWebView extends StatefulWidget {
  final String url;
  final void Function(HostMessage message) onMessage;

  const LevelMomentWebView({
    super.key,
    required this.url,
    required this.onMessage,
  });

  @override
  State<LevelMomentWebView> createState() => _LevelMomentWebViewState();
}

class _LevelMomentWebViewState extends State<LevelMomentWebView> {
  static const _backgroundColor = Color(0xFF060A14);

  late final WebViewController _controller;
  bool _pageReady = false;

  // Mirrors LevelMomentAdModal's dismissedRef: dismissed/error are terminal and
  // the dismiss path fires exactly once even if a second terminal arrives.
  bool _dismissed = false;

  @override
  void initState() {
    super.initState();
    _controller = WebViewController()
      ..setJavaScriptMode(JavaScriptMode.unrestricted)
      ..setBackgroundColor(_backgroundColor)
      ..addJavaScriptChannel(
        'ReactNativeWebView',
        onMessageReceived: _onChannelMessage,
      )
      ..loadRequest(Uri.parse(widget.url));
  }

  void _onChannelMessage(JavaScriptMessage message) {
    final parsed = HostMessage.tryParse(message.message);
    if (parsed == null) return;

    if (parsed is Ready) {
      if (mounted) setState(() => _pageReady = true);
      widget.onMessage(parsed);
      return;
    }

    if (parsed is Dismissed || parsed is ErrorMsg) {
      // Page-driven terminal: notify (guarded) then pop the route. The pop
      // re-enters dispose(), but the guard makes the second notify a no-op.
      _notifyTerminal(parsed);
      if (mounted && Navigator.of(context).canPop()) {
        Navigator.of(context).pop();
      }
      return;
    }

    // EarnedReward (non-terminal) — forward as-is, may repeat.
    widget.onMessage(parsed);
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
    // Whatever removed this route, guarantee the ad sees a terminal exactly
    // once. No-ops if a page-posted dismissed/error already fired.
    _notifyTerminal(const Dismissed());
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
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
