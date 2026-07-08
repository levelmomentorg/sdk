# @levelmoment/sdk-react-native

**Status: ✅ Scaffolded — thin WebView wrapper over the hosted `/break` page**

React Native SDK for iOS and Android. Drop-in replacement for `react-native-google-mobile-ads` rewarded ads.

See [`MIGRATION.md`](MIGRATION.md) for a line-by-line swap guide and [`docs/ADR-001-webview-rendering.md`](../../docs/ADR-001-webview-rendering.md) for the architecture rationale.

---

## What's Done

- **Sample iPhone app** — `example/` is an Expo app you can run on your phone via Expo Go. See [`example/README.md`](example/README.md).
- **`LevelMomentAd`** — mirrors `RewardedAd` from `react-native-google-mobile-ads`
  - `LevelMomentAd.createForAdRequest(placementId, { breakUrl, apiUrl?, studentToken?, format?, mock? })`
  - `ad.addAdEventListener(event, listener)` — returns unsubscribe function
  - `ad.load()` / `ad.show()` — load/show separation preserved
  - `ad.dispose()` — clears listeners
- **`LevelMomentAdModal`** — fullscreen `<Modal>` containing a `<WebView>` pointing at the hosted `/break` page. Add once at app root; `ad.show()` drives it.
- **postMessage bridge** — listens for `ready` / `earnedReward` / `dismissed` / `error` from the page and dispatches them to consumers via the standard event listeners.

## What's Not Done

- [ ] No tests — add Jest tests for URL building + message dispatch
- [ ] No native pre-warm — `load()` resolves immediately; `show()` triggers the WebView fetch. Adds a small visible delay at the pause point. Could be improved by mounting the WebView hidden during `load()` and revealing on `show()`.
- [ ] Image loading inside the page is plain `<img>` — fine, but works only with public URLs

---

## Architecture

This SDK does **not** render questions. All UI for the 8 question types and the session flow lives in [`platform/web/app/break`](../../platform/web/app/break). The SDK is a thin wrapper that:

1. Builds a URL with placement / token / format query params
2. Mounts a fullscreen WebView pointing at that URL
3. Listens for `window.ReactNativeWebView.postMessage` events from the page
4. Translates them to the existing `addAdEventListener` events

Net code: ~220 LOC (was ~1700 with the embedded RN renderer).

---

## Setup

```bash
npm install   # from repo root

cd sdk/react-native
npx tsc --noEmit
npm run build
```

`react-native` and `react-native-webview` are peer dependencies — consumers must install both. `react-native-webview` is bundled in Expo Go, so the example app needs no extra setup.

---

## Usage

### 1. Add `<LevelMomentAdModal />` once at your app root

```tsx
import { LevelMomentAdModal } from "@levelmoment/sdk-react-native";

export default function App() {
  return (
    <>
      <YourGame />
      <LevelMomentAdModal />
    </>
  );
}
```

### 2. Load and show ads

```ts
import { LevelMomentAd } from "@levelmoment/sdk-react-native";

const ad = LevelMomentAd.createForAdRequest("your-placement-id", {
  breakUrl: "https://app.levelmoment.com/break",
  apiUrl: "https://api.levelmoment.com",
  studentToken: getTokenFromDeepLink(),
  format: "quiz",
});

ad.addAdEventListener("earnedReward", (r) => r.amount === 1 && grantBonus());
ad.addAdEventListener("closed", () => resumeGame());

ad.load();
// ...later, at the natural pause point:
ad.show();
```

---

## Key Files

| File                         | Purpose                                             |
| ---------------------------- | --------------------------------------------------- |
| `src/LevelMomentAd.ts`       | Mirrors `RewardedAd`; builds URL, dispatches events |
| `src/LevelMomentAdModal.tsx` | Fullscreen `<Modal>` + `<WebView>` host (~150 LOC)  |
| `src/index.ts`               | Public exports                                      |
| `MIGRATION.md`               | Swap guide from `react-native-google-mobile-ads`    |

---

## Event Name Mapping

| react-native-google-mobile-ads      | LevelMoment      |
| ----------------------------------- | ---------------- |
| `RewardedAdEventType.LOADED`        | `'loaded'`       |
| `AdEventType.ERROR`                 | `'error'`        |
| `AdEventType.OPENED`                | `'opened'`       |
| `AdEventType.CLOSED`                | `'closed'`       |
| `RewardedAdEventType.EARNED_REWARD` | `'earnedReward'` |
