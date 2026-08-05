# Port — React Native (`react-native-google-mobile-ads` → LevelMoment)

You are inside `/levelmoment port`, platform = react-native.

## What the LevelMoment RN SDK looks like

```tsx
import {
  LevelMomentAd,
  LevelMomentAdModal,
} from "@levelmoment/sdk-react-native";

// 1. Add <LevelMomentAdModal /> ONCE at app root (App.tsx).
//    This is the fullscreen overlay that renders questions in a WebView.
export default function App() {
  return (
    <>
      <YourAppRoot />
      <LevelMomentAdModal />
    </>
  );
}

// 2. In any screen that shows ads:
const rewardedAd = LevelMomentAd.createForAdRequest("pl_xxx...", {
  breakUrl: "https://app.levelmoment.com/break", // REQUIRED — where the hosted /break page lives
  apiUrl: "https://api.levelmoment.com",
  studentToken: await getTokenFromUrl(),
  // mock: true,  // bundled mock questions — no live API/token (handy for a first integration)
});

const unsubLoaded = rewardedAd.addAdEventListener("loaded", () =>
  setLoaded(true),
);
const unsubEarned = rewardedAd.addAdEventListener("earnedReward", (reward) => {
  // reward.amount === 1 → correct answer
  if (reward.amount === 1) grantBonus();
});
const unsubClosed = rewardedAd.addAdEventListener("closed", () => {
  setLoaded(false);
  resumeGame();
});

rewardedAd.load();
// later:
if (loaded) rewardedAd.show();
```

API name mapping (memorize before editing):

| react-native-google-mobile-ads        | LevelMoment                                  |
| ------------------------------------- | -------------------------------------------- |
| `RewardedAd.createForAdRequest(id)`   | `LevelMomentAd.createForAdRequest(id, opts)` |
| `RewardedAdEventType.LOADED`          | `"loaded"`                                   |
| `RewardedAdEventType.EARNED_REWARD`   | `"earnedReward"`                             |
| `AdEventType.OPENED`                  | `"opened"`                                   |
| `AdEventType.CLOSED`                  | `"closed"`                                   |
| `AdEventType.ERROR`                   | `"error"`                                    |
| `requestNonPersonalizedAdsOnly: true` | (not needed — remove)                        |

## Step 1 — update package.json

Edit `package.json`:

```diff
 "dependencies": {
-  "react-native-google-mobile-ads": "^14.x",
+  "@levelmoment/sdk-react-native": "^0.1.0",
+  "react-native-webview": "^13.0.0",
   ...
 }
```

If `react-native-webview` is already listed, just leave it.

Ask before running `npm install` — it may regenerate the lockfile and
trigger native rebuilds.

## Step 2 — replace the import

In every file from the inventory:

```diff
-import {
-  RewardedAd,
-  RewardedAdEventType,
-  AdEventType,
-} from "react-native-google-mobile-ads";
+import { LevelMomentAd } from "@levelmoment/sdk-react-native";
```

If the file also imported `mobileAds`, `BannerAd`, `InterstitialAd`, or
other types not supported by LevelMoment, leave a `// LEVELMOMENT_TODO:` and
flag in summary.

## Step 3 — add `<LevelMomentAdModal />` at app root

Find the root component. Usually `App.tsx`, `App.jsx`, or the file
referenced by `AppRegistry.registerComponent`. Add the import and render
the modal **once** at the top level, outside any conditional rendering.

Template at `.claude/skills/levelmoment/templates/rn-app-root.tsx.tmpl`.

If the user has a navigator (`react-navigation`, etc.), put
`<LevelMomentAdModal />` as a sibling of `<NavigationContainer>`, not inside
a screen.

## Step 4 — replace ad creation

```diff
-const rewardedAd = RewardedAd.createForAdRequest("ca-app-pub-xxx/yyy", {
-  requestNonPersonalizedAdsOnly: true,
-});
+const rewardedAd = LevelMomentAd.createForAdRequest("{{PLACEMENT_ID}}", {
+  breakUrl: "https://app.levelmoment.com/break", // REQUIRED
+  apiUrl: "https://api.levelmoment.com",
+  studentToken: await getTokenFromUrl(),
+});
```

`breakUrl` is a **required** option (the WebView shell needs it to find the
hosted `/break` page); omitting it is a compile error. For a first
integration with no live API or student token, pass `mock: true` instead of
`apiUrl`/`studentToken` — the page serves bundled mock questions.

Substitute `{{PLACEMENT_ID}}` with the placement ID from the port flow.

If the file doesn't already have a `getTokenFromUrl` helper, insert the
helper from `.claude/skills/levelmoment/templates/rn-get-token.ts.tmpl` at
the top of the file.

### Greenfield / non-game demos (synthetic break-point)

Many RN apps (and engine "demo" apps like `react-native-game-engine`) have
no natural game-over / level-complete seam. Don't force one — fire the break
on a **deterministic synthetic event** (validated porting that engine,
LevelMoment #60): e.g. every Nth tap in the existing touch/system handler,
guarded by a module-level boolean so it fires once per trigger (not every
frame). Document it plainly as synthetic. This still exercises the full SDK
path (install → `<LevelMomentAdModal/>` → `load()`/`show()` → WebView → event
bridge) — which is the point for an SDK-ergonomics port.

### Pre-publish dependency (Metro `file:` resolver)

If `@levelmoment/sdk-react-native` isn't on npm yet, install it (and its
`@levelmoment/sdk-core` peer) as `file:` deps pointing at a local checkout,
then teach **Metro** to resolve them out of the monorepo (the single
highest-risk step of an RN port — #60):

```js
// metro.config.js
const path = require("path");
const sdk = "/abs/path/to/levelmoment/sdk";
config.watchFolders = [path.join(sdk, "react-native"), path.join(sdk, "core")];
config.resolver.extraNodeModules = {
  "@levelmoment/sdk-react-native": path.join(sdk, "react-native"),
  "@levelmoment/sdk-core": path.join(sdk, "core"),
  // pin a single React/react-native copy to avoid duplicate-module errors:
  react: path.join(projectRoot, "node_modules/react"),
  "react-native": path.join(projectRoot, "node_modules/react-native"),
};
```

Flag in your summary that the `file:` path is build-machine-local — switch to
a published version range once the SDK is on npm.

## Step 5 — replace event listeners

```diff
-const unsubscribeLoaded = rewardedAd.addAdEventListener(
-  RewardedAdEventType.LOADED,
-  () => setLoaded(true),
-);
+const unsubscribeLoaded = rewardedAd.addAdEventListener(
+  "loaded",
+  () => setLoaded(true),
+);

-const unsubscribeEarned = rewardedAd.addAdEventListener(
-  RewardedAdEventType.EARNED_REWARD,
-  (reward) => grantBonus(reward),
-);
+const unsubscribeEarned = rewardedAd.addAdEventListener(
+  "earnedReward",
+  (reward) => {
+    if (reward.amount === 1) grantBonus(reward);
+  },
+);

-const unsubscribeClosed = rewardedAd.addAdEventListener(
-  AdEventType.CLOSED,
-  () => resumeGame(),
-);
+const unsubscribeClosed = rewardedAd.addAdEventListener(
+  "closed",
+  () => resumeGame(),
+);
```

Preserve the user's reward / resume logic — only the surrounding event
listener call changes.

## Step 6 — `load()` and `show()` stay the same

These method names are identical between RN AdMob and LevelMoment. **Do not
change them.** Only the class name (`RewardedAd` → `LevelMomentAd`) and the
import differ.

## Step 7 — clean up

- Remove the `react-native-google-mobile-ads` import if no symbols from
  it remain in the file.
- If `App.tsx` has `await mobileAds().initialize()` (the package's
  global init), remove it — LevelMoment needs no equivalent.
- If iOS `Info.plist` or `AndroidManifest.xml` has `GADApplicationIdentifier`
  or `com.google.android.gms.ads.APPLICATION_ID`, leave them (the user
  may want to keep AdMob for other ad types) but flag in summary.

## Step 8 — things to flag

- **Mediation adapters** — `react-native-google-mobile-ads/applovin`,
  `/meta`, etc. LevelMoment has no mediation. Flag.
- **UMP consent** — `mobileAds().setRequestConfiguration({...})` or
  `AdsConsent.requestInfoUpdate(...)`. Flag — LevelMoment handles consent
  on the parent side.
- **Banner / app open / native ads** — `BannerAd`, `AppOpenAd`,
  `NativeAd`. Not supported by LevelMoment. Flag.
- **Custom test-mode toggles** — `__DEV__` checks gating AdMob test IDs.
  Suggest replacing with `?mock=true` URL param on the WebView.
