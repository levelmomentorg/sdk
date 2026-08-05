// LevelMoment sample iPhone app.
//
// Run with `npm start` and scan the QR code with the Expo Go app on your
// iPhone. See README.md for full instructions.
//
// Architecture: <LevelMomentAdModal /> hosts a fullscreen WebView that loads
// the hosted /break page (platform/web/app/break). All question UI lives
// there. This app is a thin host that proves the SDK contract.

import React, { useState } from "react";
import {
  SafeAreaView,
  ScrollView,
  StyleSheet,
  Switch,
  Text,
  TextInput,
  TouchableOpacity,
  View,
} from "react-native";
import { StatusBar } from "expo-status-bar";
import {
  LevelMomentAd,
  LevelMomentAdModal,
} from "@levelmoment/sdk-react-native";
import type { BreakFormat } from "@levelmoment/sdk-core";

const FORMATS: { id: BreakFormat; label: string; subtitle: string }[] = [
  {
    id: "flashcard",
    label: "Flashcard",
    subtitle: "Single question — fastest",
  },
  {
    id: "quiz",
    label: "Quiz",
    subtitle: "Multi-question session with summary",
  },
  {
    id: "deep_dive",
    label: "Deep Dive",
    subtitle: "Lesson + quiz with summary",
  },
];

export default function App(): React.ReactElement {
  const [mockMode, setMockMode] = useState(true);
  const [breakUrl, setBreakUrl] = useState("http://localhost:3000/break");
  const [apiUrl, setApiUrl] = useState("https://api.example.com");
  const [studentToken, setStudentToken] = useState("demo-token");
  const [placementId, setPlacementId] = useState("demo-placement");
  const [format, setFormat] = useState<BreakFormat>("quiz");
  const [status, setStatus] = useState("Ready");

  const showBreak = (): void => {
    setStatus("Loading…");

    const ad = LevelMomentAd.createForAdRequest(placementId, {
      breakUrl,
      apiUrl,
      studentToken,
      format,
      mock: mockMode,
    });

    ad.addAdEventListener("loaded", () => {
      setStatus("Loaded — opening break");
      ad.show();
    });
    ad.addAdEventListener("error", (err) => {
      setStatus(`Error: ${err.code} — ${err.message}`);
    });
    ad.addAdEventListener("earnedReward", (reward) => {
      setStatus(
        reward.amount === 1
          ? "Reward earned — correct!"
          : "Answered (no reward)",
      );
    });
    ad.addAdEventListener("closed", () => {
      setStatus("Dismissed — ready for next break");
      ad.dispose();
    });

    ad.load();
  };

  return (
    <SafeAreaView style={styles.safe}>
      <StatusBar style="dark" />
      <ScrollView
        style={styles.scroll}
        contentContainerStyle={styles.content}
        keyboardShouldPersistTaps="handled"
      >
        <Text style={styles.title}>LevelMoment Sample</Text>
        <Text style={styles.subtitle}>
          Test the questions experience as a player would see it.
        </Text>

        <View style={styles.card}>
          <View style={styles.row}>
            <View style={{ flex: 1 }}>
              <Text style={styles.rowLabel}>Mock mode</Text>
              <Text style={styles.rowHint}>
                Page uses bundled questions — no API needed.
              </Text>
            </View>
            <Switch value={mockMode} onValueChange={setMockMode} />
          </View>
        </View>

        <View style={styles.card}>
          <Text style={styles.cardTitle}>Connection</Text>
          <Text style={styles.label}>Break page URL</Text>
          <TextInput
            style={styles.input}
            value={breakUrl}
            onChangeText={setBreakUrl}
            autoCapitalize="none"
            autoCorrect={false}
            placeholder="http://<your-laptop-ip>:3000/break"
          />
          <Text style={styles.helper}>
            Run `cd platform/web && npm run dev` and use your laptop&apos;s LAN
            IP.
          </Text>
          {!mockMode ? (
            <>
              <Text style={styles.label}>API URL</Text>
              <TextInput
                style={styles.input}
                value={apiUrl}
                onChangeText={setApiUrl}
                autoCapitalize="none"
                autoCorrect={false}
                placeholder="https://api.levelmoment.com"
              />
              <Text style={styles.label}>Student session token</Text>
              <TextInput
                style={styles.input}
                value={studentToken}
                onChangeText={setStudentToken}
                autoCapitalize="none"
                autoCorrect={false}
                secureTextEntry
              />
              <Text style={styles.label}>Placement ID</Text>
              <TextInput
                style={styles.input}
                value={placementId}
                onChangeText={setPlacementId}
                autoCapitalize="none"
                autoCorrect={false}
              />
            </>
          ) : null}
        </View>

        <View style={styles.card}>
          <Text style={styles.cardTitle}>Break format</Text>
          {FORMATS.map((f) => {
            const selected = f.id === format;
            return (
              <TouchableOpacity
                key={f.id}
                style={[styles.formatRow, selected && styles.formatRowSelected]}
                onPress={() => setFormat(f.id)}
              >
                <View style={styles.radio}>
                  {selected ? <View style={styles.radioDot} /> : null}
                </View>
                <View style={{ flex: 1 }}>
                  <Text style={styles.formatLabel}>{f.label}</Text>
                  <Text style={styles.formatSubtitle}>{f.subtitle}</Text>
                </View>
              </TouchableOpacity>
            );
          })}
        </View>

        <TouchableOpacity style={styles.cta} onPress={showBreak}>
          <Text style={styles.ctaText}>Show ad break</Text>
        </TouchableOpacity>

        <Text style={styles.status}>{status}</Text>
      </ScrollView>

      <LevelMomentAdModal />
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  safe: { flex: 1, backgroundColor: "#F4F6FB" },
  scroll: { flex: 1 },
  content: { padding: 20, paddingBottom: 80 },
  title: { fontSize: 28, fontWeight: "700", color: "#1A1F36", marginTop: 8 },
  subtitle: {
    fontSize: 15,
    color: "#5C6478",
    marginTop: 6,
    marginBottom: 24,
  },
  card: {
    backgroundColor: "#fff",
    borderRadius: 16,
    padding: 16,
    marginBottom: 16,
    shadowColor: "#000",
    shadowOffset: { width: 0, height: 1 },
    shadowOpacity: 0.06,
    shadowRadius: 4,
    elevation: 1,
  },
  cardTitle: {
    fontSize: 14,
    fontWeight: "600",
    color: "#1A1F36",
    marginBottom: 12,
    textTransform: "uppercase",
    letterSpacing: 0.6,
  },
  row: { flexDirection: "row", alignItems: "center", gap: 12 },
  rowLabel: { fontSize: 16, fontWeight: "600", color: "#1A1F36" },
  rowHint: { fontSize: 13, color: "#5C6478", marginTop: 2 },
  label: { fontSize: 13, color: "#5C6478", marginTop: 8, marginBottom: 4 },
  helper: { fontSize: 12, color: "#94A3B8", marginTop: 4 },
  input: {
    borderWidth: 1,
    borderColor: "#E1E5EE",
    borderRadius: 10,
    paddingVertical: 10,
    paddingHorizontal: 12,
    fontSize: 15,
    color: "#1A1F36",
    backgroundColor: "#FAFBFD",
  },
  formatRow: {
    flexDirection: "row",
    alignItems: "center",
    paddingVertical: 12,
    paddingHorizontal: 12,
    borderRadius: 12,
    gap: 12,
    marginBottom: 4,
  },
  formatRowSelected: { backgroundColor: "#EEF1FB" },
  radio: {
    width: 22,
    height: 22,
    borderRadius: 11,
    borderWidth: 2,
    borderColor: "#5B6BFF",
    alignItems: "center",
    justifyContent: "center",
  },
  radioDot: {
    width: 10,
    height: 10,
    borderRadius: 5,
    backgroundColor: "#5B6BFF",
  },
  formatLabel: { fontSize: 16, fontWeight: "600", color: "#1A1F36" },
  formatSubtitle: { fontSize: 13, color: "#5C6478", marginTop: 2 },
  cta: {
    backgroundColor: "#5B6BFF",
    borderRadius: 14,
    paddingVertical: 16,
    alignItems: "center",
    marginTop: 8,
  },
  ctaText: { color: "#fff", fontSize: 17, fontWeight: "600" },
  status: {
    marginTop: 16,
    fontSize: 13,
    color: "#5C6478",
    textAlign: "center",
  },
});
