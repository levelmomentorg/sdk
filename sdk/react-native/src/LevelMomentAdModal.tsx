import { sameHostedOrigin, isBridgeMessage } from "@levelmoment/sdk-core";
// LevelMomentAdModal — WebView host for every Level Moment hosted surface.
//
// Add this component once at the root of your app. LevelMomentAd.show() and the
// startup gate (ensureSignedIn / isSignedIn) drive it via the module-level
// handler in ./modalHost — the same pattern AdMob's RN SDK uses.
//
// It owns two slots. The visible slot is a fullscreen modal: a break, or the
// sign-in gate running its pairing flow. The hidden slot is an off-screen
// WebView with no chrome, used by the headless credential check — it lives
// alongside the visible one so a check running in the background cannot evict a
// break the player is in the middle of.
//
// All question and pairing UI lives in the hosted /break page
// (platform/web/app/break); this component just owns the native presentation
// and bridges postMessage events back to the caller.

import React, { useCallback, useEffect, useRef, useState } from "react";
import {
  Linking,
  Modal,
  StyleSheet,
  TouchableOpacity,
  View,
  Text,
  ActivityIndicator,
} from "react-native";
import { WebView, type WebViewMessageEvent } from "react-native-webview";
import {
  credentialInjection,
  externalUrlToOpen,
  type HostMessage,
  isTerminal,
} from "./hostMessage.js";
import { _registerModalHandler, type ModalShowParams } from "./modalHost.js";

export {
  _registerModalHandler,
  _hasModalHandler,
  _triggerModal,
  type ModalShowParams,
  type ModalHandle,
} from "./modalHost.js";

// Pre-`ready` watchdog deadline, mirroring sdk/web, sdk/flutter and sdk/unity:
// a crashed or unreachable hosted page must never cover the game forever. After
// `ready` there is no timeout — a student thinking through a quiz, or a parent
// walking to another room to scan a code, is never force-closed.
const READY_TIMEOUT_MS = 15_000;

/**
 * The shared lifecycle of one hosted surface: parse the bridge, hold the
 * pre-`ready` watchdog, and deliver a terminal message exactly once. Both slots
 * use it, so a break and the gate cannot drift apart.
 */
function useHostedSurface(
  params: ModalShowParams | null,
  onFinished: () => void,
) {
  const [pageReady, setPageReady] = useState(false);
  const terminalRef = useRef(false);
  const activeParams = useRef(params);
  activeParams.current = params;
  const navigationEpoch = useRef(0);
  // The WebView showing this surface, so `needCredential` can be answered by
  // running script inside the page. Nothing else reaches into the WebView.
  const webViewRef = useRef<WebView<unknown> | null>(null);

  useEffect(() => {
    terminalRef.current = false;
    setPageReady(false);
  }, [params]);

  const finish = useCallback(
    (msg: HostMessage | null) => {
      if (terminalRef.current) return;
      terminalRef.current = true;
      if (msg) params?.onMessage(msg);
      onFinished();
    },
    [params, onFinished],
  );

  const dismiss = useCallback(() => finish({ type: "dismissed" }), [finish]);

  const failLoad = useCallback(
    (message: string) =>
      finish({ type: "error", payload: { code: "network_error", message } }),
    [finish],
  );

  useEffect(() => {
    if (!params || pageReady) return;
    const timeoutMs = params.loadTimeoutMs ?? READY_TIMEOUT_MS;
    if (timeoutMs <= 0) return;
    const onLoadTimeout = params.onLoadTimeout;
    const timer = setTimeout(() => {
      if (terminalRef.current) return;
      if (onLoadTimeout) {
        terminalRef.current = true;
        onFinished();
        onLoadTimeout();
        return;
      }
      dismiss();
    }, timeoutMs);
    return () => clearTimeout(timer);
  }, [params, pageReady, dismiss, onFinished]);

  const handleWebViewMessage = useCallback(
    (event: WebViewMessageEvent) => {
      if (
        !params ||
        terminalRef.current ||
        activeParams.current !== params ||
        !sameHostedOrigin(event.nativeEvent.url, params.url)
      )
        return;
      let parsed: HostMessage;
      try {
        parsed = JSON.parse(event.nativeEvent.data) as HostMessage;
      } catch {
        return;
      }
      if (!isBridgeMessage(parsed)) return;
      if (parsed.type === "ready") {
        setPageReady(true);
        params.onMessage(parsed);
        return;
      }
      if (parsed.type === "needCredential") {
        // Answer by running script in the page. The caller reads the keychain,
        // which is asynchronous — by the time it resolves this surface may
        // already be gone, so re-check the ref rather than injecting into a
        // WebView that has been unmounted or handed to a newer request.
        const ask = params.onNeedCredential;
        if (ask) {
          const epoch = navigationEpoch.current;
          void ask()
            .then((reply) => {
              if (
                terminalRef.current ||
                activeParams.current !== params ||
                navigationEpoch.current !== epoch
              )
                return;
              webViewRef.current?.injectJavaScript(
                credentialInjection(reply, params.url),
              );
            })
            .catch(() => {
              // The hosted page can recover if secure storage is unavailable.
            });
        }
        params.onMessage(parsed);
        return;
      }
      // Judged against the URL this surface was opened with, so a WebView that
      // wandered off cannot send a parent anywhere but the hosted origin.
      const externalUrl = externalUrlToOpen(parsed, params.url);
      if (externalUrl) {
        // Out to the OS, never into this WebView. Parent sign-in happens
        // outside the WebView because the game can inspect that surface; never
        // collect a Level Moment email code or other parent credential inside
        // it. The surface stays open behind the browser and keeps polling, so a
        // parent who never comes back to the app still connects the game.
        void Linking.openURL(externalUrl).catch(() => {
          // No browser took it. The pairing card still shows the code and the
          // QR, which is the path that needs no app at all.
        });
        params.onMessage(parsed);
        return;
      }
      if (isTerminal(parsed)) {
        finish(parsed);
        return;
      }
      params.onMessage(parsed);
    },
    [params, finish],
  );

  // Android navigation callbacks are incomplete for redirects/subframes.
  // credentialInjection also checks the destination document before delivery.
  const allowNavigation = (request: { url: string }): boolean => {
    if (params && sameHostedOrigin(request.url, params.url)) return true;
    navigationEpoch.current += 1;
    failLoad("The Level Moment page tried to leave its hosted origin.");
    return false;
  };
  const onLoadStart = () => {
    navigationEpoch.current += 1;
  };
  return {
    pageReady,
    dismiss,
    failLoad,
    handleWebViewMessage,
    webViewRef,
    allowNavigation,
    onLoadStart,
  };
}

export const LevelMomentAdModal: React.FC = () => {
  const [visible, setVisible] = useState<ModalShowParams | null>(null);
  const [hidden, setHidden] = useState<ModalShowParams | null>(null);

  // Each slot's occupant, tracked outside React state so the handler can tell a
  // displaced caller before the re-render swaps the WebView out from under it.
  // Reading it inside a state updater would not do: React may run an updater
  // twice, and onDisplaced must fire exactly once.
  const occupants = useRef<{
    visible: ModalShowParams | null;
    hidden: ModalShowParams | null;
  }>({ visible: null, hidden: null });

  useEffect(() => {
    _registerModalHandler((params) => {
      const slot = params.hidden ? "hidden" : "visible";
      occupants.current[slot]?.onDisplaced?.();
      occupants.current[slot] = params;
      if (params.hidden) setHidden(params);
      else setVisible(params);
      return {
        close: () => {
          // Only the current occupant may drop the slot. A caller that has
          // already been displaced would otherwise unmount its successor's
          // WebView.
          if (occupants.current[slot] !== params) return;
          occupants.current[slot] = null;
          if (params.hidden) setHidden(null);
          else setVisible(null);
        },
      };
    });
    return () => _registerModalHandler(null);
  }, []);

  const clearVisible = useCallback(() => {
    occupants.current.visible = null;
    setVisible(null);
  }, []);
  const clearHidden = useCallback(() => {
    occupants.current.hidden = null;
    setHidden(null);
  }, []);

  const visibleSurface = useHostedSurface(visible, clearVisible);
  const hiddenSurface = useHostedSurface(hidden, clearHidden);

  return (
    <>
      <Modal
        visible={visible !== null}
        animationType="slide"
        presentationStyle="fullScreen"
        onRequestClose={visibleSurface.dismiss}
        statusBarTranslucent
      >
        <View style={styles.container}>
          {visible ? (
            <WebView<unknown>
              ref={visibleSurface.webViewRef}
              source={{ uri: visible.url }}
              style={styles.webview}
              onMessage={visibleSurface.handleWebViewMessage}
              onError={(event) =>
                visibleSurface.failLoad(
                  event.nativeEvent.description ||
                    "The Level Moment page failed to load.",
                )
              }
              onShouldStartLoadWithRequest={visibleSurface.allowNavigation}
              onLoadStart={visibleSurface.onLoadStart}
              onOpenWindow={() =>
                visibleSurface.failLoad(
                  "Open links from the Level Moment screen.",
                )
              }
              javaScriptCanOpenWindowsAutomatically={false}
              javaScriptEnabled
              domStorageEnabled
              originWhitelist={["http://*", "https://*"]}
              allowsBackForwardNavigationGestures={false}
              containerStyle={styles.webviewContainer}
              startInLoadingState={false}
            />
          ) : null}
          {!visibleSurface.pageReady && visible ? (
            <View style={styles.loadingOverlay} pointerEvents="none">
              <ActivityIndicator size="large" color="#3b82f6" />
            </View>
          ) : null}
          <TouchableOpacity
            accessibilityLabel="Close"
            accessibilityRole="button"
            style={styles.closeButton}
            onPress={visibleSurface.dismiss}
          >
            <Text style={styles.closeText}>×</Text>
          </TouchableOpacity>
        </View>
      </Modal>
      {/* The headless check. Kept in the tree so it actually loads, but out of
          layout, out of the a11y tree, and unreachable by touch. */}
      {hidden ? (
        <View
          style={styles.hiddenHost}
          pointerEvents="none"
          accessibilityElementsHidden
          importantForAccessibility="no-hide-descendants"
        >
          <WebView<unknown>
            ref={hiddenSurface.webViewRef}
            source={{ uri: hidden.url }}
            onMessage={hiddenSurface.handleWebViewMessage}
            onError={(event) =>
              hiddenSurface.failLoad(
                event.nativeEvent.description ||
                  "The Level Moment sign-in check failed to load.",
              )
            }
            onShouldStartLoadWithRequest={hiddenSurface.allowNavigation}
            onLoadStart={hiddenSurface.onLoadStart}
            javaScriptCanOpenWindowsAutomatically={false}
            javaScriptEnabled
            domStorageEnabled
            originWhitelist={["http://*", "https://*"]}
            startInLoadingState={false}
          />
        </View>
      ) : null}
    </>
  );
};

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: "#060a14" },
  webview: { flex: 1, backgroundColor: "#060a14" },
  webviewContainer: { backgroundColor: "#060a14" },
  hiddenHost: {
    position: "absolute",
    top: 0,
    left: 0,
    width: 0,
    height: 0,
    opacity: 0,
    overflow: "hidden",
  },
  loadingOverlay: {
    ...StyleSheet.absoluteFillObject,
    alignItems: "center",
    justifyContent: "center",
    backgroundColor: "#060a14",
  },
  closeButton: {
    position: "absolute",
    top: 48,
    right: 16,
    width: 40,
    height: 40,
    borderRadius: 20,
    backgroundColor: "rgba(15, 23, 42, 0.85)",
    alignItems: "center",
    justifyContent: "center",
  },
  closeText: {
    color: "#e2e8f0",
    fontSize: 24,
    lineHeight: 26,
    fontWeight: "300",
  },
});
