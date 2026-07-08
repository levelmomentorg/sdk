// LevelMomentAdModal — fullscreen WebView host for the educational break page.
//
// Add this component once at the root of your app. LevelMomentAd.show() drives it
// via a module-level handler — the same pattern AdMob's RN SDK uses.
//
// All question UI lives in the hosted /break page (platform/web/app/break);
// this component just owns the native fullscreen presentation and bridges
// postMessage events back to LevelMomentAd.

import React, { useCallback, useEffect, useRef, useState } from "react";
import {
  Modal,
  StyleSheet,
  TouchableOpacity,
  View,
  Text,
  ActivityIndicator,
} from "react-native";
import { WebView, type WebViewMessageEvent } from "react-native-webview";
import type { HostMessage } from "./LevelMomentAd.js";

export interface ModalShowParams {
  url: string;
  onMessage: (msg: HostMessage) => void;
}

let _globalHandler: ((params: ModalShowParams) => void) | null = null;

/** @internal Called by LevelMomentAdModal when it mounts. */
export function _registerModalHandler(
  handler: ((params: ModalShowParams) => void) | null,
): void {
  _globalHandler = handler;
}

/** @internal Returns true if LevelMomentAdModal is mounted and ready. */
export function _hasModalHandler(): boolean {
  return _globalHandler !== null;
}

/** @internal Triggered by LevelMomentAd.show(). */
export function _triggerModal(params: ModalShowParams): void {
  _globalHandler?.(params);
}

interface ActiveBreak {
  url: string;
  onMessage: (msg: HostMessage) => void;
}

export const LevelMomentAdModal: React.FC = () => {
  const [active, setActive] = useState<ActiveBreak | null>(null);
  const [pageReady, setPageReady] = useState(false);
  const dismissedRef = useRef(false);

  useEffect(() => {
    _registerModalHandler((params) => {
      dismissedRef.current = false;
      setPageReady(false);
      setActive(params);
    });
    return () => _registerModalHandler(null);
  }, []);

  const dismiss = useCallback(() => {
    if (dismissedRef.current) return;
    dismissedRef.current = true;
    active?.onMessage({ type: "dismissed" });
    setActive(null);
    setPageReady(false);
  }, [active]);

  const handleWebViewMessage = useCallback(
    (event: WebViewMessageEvent) => {
      if (!active) return;
      let parsed: HostMessage;
      try {
        parsed = JSON.parse(event.nativeEvent.data) as HostMessage;
      } catch {
        return;
      }
      if (parsed.type === "ready") {
        setPageReady(true);
        active.onMessage(parsed);
        return;
      }
      if (parsed.type === "dismissed" || parsed.type === "error") {
        if (dismissedRef.current) return;
        dismissedRef.current = true;
        active.onMessage(parsed);
        setActive(null);
        setPageReady(false);
        return;
      }
      active.onMessage(parsed);
    },
    [active],
  );

  return (
    <Modal
      visible={active !== null}
      animationType="slide"
      presentationStyle="fullScreen"
      onRequestClose={dismiss}
      statusBarTranslucent
    >
      <View style={styles.container}>
        {active ? (
          <WebView
            source={{ uri: active.url }}
            style={styles.webview}
            onMessage={handleWebViewMessage}
            javaScriptEnabled
            domStorageEnabled
            originWhitelist={["*"]}
            allowsBackForwardNavigationGestures={false}
            containerStyle={styles.webviewContainer}
            startInLoadingState={false}
          />
        ) : null}
        {!pageReady && active ? (
          <View style={styles.loadingOverlay} pointerEvents="none">
            <ActivityIndicator size="large" color="#3b82f6" />
          </View>
        ) : null}
        <TouchableOpacity
          accessibilityLabel="Close"
          accessibilityRole="button"
          style={styles.closeButton}
          onPress={dismiss}
        >
          <Text style={styles.closeText}>×</Text>
        </TouchableOpacity>
      </View>
    </Modal>
  );
};

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: "#060a14" },
  webview: { flex: 1, backgroundColor: "#060a14" },
  webviewContainer: { backgroundColor: "#060a14" },
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
