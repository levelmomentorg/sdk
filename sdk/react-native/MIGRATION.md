# Migrate a React Native rewarded placement

## Prerequisites

- React Native 0.72 or later
- The immutable core and React Native 0.2 preview tarballs supplied to you

The public npm package is still 0.1.2. Pin the supplied preview artifacts until
0.2 is published.

## 1. Replace the dependency

Install both SDK tarballs and the native peers:

```bash
npm install ./PROVIDED_SDK_CORE_TARBALL.tgz ./PROVIDED_SDK_REACT_NATIVE_TARBALL.tgz react-native-webview react-native-keychain
```

## 2. Mount the modal

Add one modal host at the app root:

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

**Warning:** Access flows and breaks fail when `LevelMomentAdModal` is not
mounted.

## 3. Add the access flow

Use the access operations for a player-selected learning feature:

```ts
import { checkAccess, ensureAccess } from "@levelmoment/sdk-react-native";

const options = { placementId: "YOUR_PLACEMENT_ID" };

try {
  if (!(await checkAccess(options))) {
    const result = await ensureAccess(options);
    if (result !== "ready") return;
  }
  openLearningMode();
} catch {
  showTryAgainLater();
}
```

`checkAccess()` rejects on technical failure. `ensureAccess()` resolves
`ready`, `canceled`, or `technicalFailure`. Keep `ensureSignedIn()` and
`isSignedIn()` for identity-only features.

## 4. Replace rewarded loading

```ts
import { LevelMomentAd } from "@levelmoment/sdk-react-native";

const ad = LevelMomentAd.createForAdRequest("YOUR_PLACEMENT_ID");
let rewardGranted = false;
ad.addAdEventListener("loaded", () => ad.show());
ad.addAdEventListener("earnedReward", ({ amount }) => {
  if (amount === 1 && !rewardGranted) {
    rewardGranted = true;
    grantBonus();
  }
});
ad.addAdEventListener("closed", () => {
  resumeGame();
  ad.dispose();
});
ad.addAdEventListener("error", () => {
  resumeGame();
  ad.dispose();
});
ad.load();
```

A multi-question break can emit several reward callbacks. Guard the game
reward so the first correct answer grants it once. Dismissal and failure only
resume the game.

Production uses fixed service endpoints and managed credentials. Do not pass
`apiUrl`, `breakUrl`, or `studentToken`.

## 5. Configure sandbox testing

Pass test configuration only through `unsafeTesting`:

```ts
const options = {
  placementId: "YOUR_PLACEMENT_ID",
  unsafeTesting: {
    apiUrl: "http://YOUR_COMPUTER_IP:8080",
    breakUrl: "http://YOUR_COMPUTER_IP:3000/break",
    token: "YOUR_EPLY_SBX_TOKEN",
  },
};
```

**Warning:** Remove `unsafeTesting` from production builds.

## Verify the migration

1. Run the app's TypeScript check and native build.
2. Confirm a quiet access check does not show the modal.
3. Confirm the visible access flow can pair a fresh install.
4. Complete a break and confirm `closed` resumes the game exactly once.
5. Confirm a correct answer grants the game reward at most once.

Compare the result with the [React Native example](example/App.tsx).
