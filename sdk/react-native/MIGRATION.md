# Migrate a React Native rewarded placement

## Prerequisites

- React Native 0.72 or later

## 1. Replace the dependency

Install version 0.3.0 from npm with the native peers:

```bash
npm install @levelmoment/sdk-react-native@0.3.0 react-native-webview react-native-keychain
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
A `ready` result means the household had a subscription, or complimentary
access, when the parent linked the game; a lapse afterward does not unlink the
device.

## 4. Replace rewarded loading

```ts
import { LevelMomentAd } from "@levelmoment/sdk-react-native";

const ad = LevelMomentAd.createForAdRequest("YOUR_PLACEMENT_ID", {
  format: "practice_set",
  slot: { adType: "rewarded", targetDurationSeconds: 30, rewardAmount: 1 },
});
let rewardGranted = false;
ad.addAdEventListener("loaded", () => ad.show());
ad.addAdEventListener("earnedReward", ({ rewardId }) => {
  if (!rewardGranted) {
    rewardGranted = true;
    recordGrantedReward(rewardId);
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

A passed graded break emits one reward callback, regardless of question count.
Deduplicate game grants by `rewardId = breakSessionId`. Dismissal and failure
only resume the game; the game chooses the item or currency amount.

Declare the slot this placement fills: `adType` (`"rewarded"` when it grants
something, `"interstitial"` when it doesn't), `targetDurationSeconds` (how
long the moment holds the player: 30 for a reward, 18 for resuming play, never
under 18) and `rewardAmount` (what the game grants, for reporting). These
replace the old `format` choice. Pass `format: "practice_set"` with a rewarded slot as a floor,
as the example does. See
[slot sizes](https://levelmoment.com/docs/getting-started#slot-size).

Production uses fixed service endpoints and managed credentials. Do not pass
`apiUrl`, `breakUrl`, or `studentToken`.

## 5. Configure sandbox testing

Pass your `eply_sbx_` sandbox token alone inside `unsafeTesting`. With Expo,
keep it in `.env.development.local`, which only `npx expo start` loads; see
[Testing and sandbox](https://levelmoment.com/docs/testing#configure):

```ts
const sandboxToken = process.env.EXPO_PUBLIC_LM_SANDBOX_TOKEN;

// Debug builds only. A production build passes no unsafeTesting.
const testing = sandboxToken ? { unsafeTesting: { token: sandboxToken } } : {};
```

Spread `testing` into the options of every access call and every
`createForAdRequest`.

**Warning:** A production build must contain no `eply_sbx_` token. A build
that carries one serves every player sandbox breaks that earn nothing.

## Verify the migration

1. Run the app's TypeScript check and native build.
2. Confirm a quiet access check does not show the modal.
3. Confirm the visible access flow can pair a fresh install.
4. Complete a break and confirm `closed` resumes the game exactly once.
5. Confirm a server-confirmed passed graded break grants the game reward at most once per `rewardId`.

Compare the result with the [React Native example](example/App.tsx).
