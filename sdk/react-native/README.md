# @levelmoment/sdk-react-native

Level Moment is a rewarded break for iOS and Android games. This package
follows the load/show lifecycle used by common rewarded-ad SDKs.

**Preview:** Validate the hosted break and WebView provider on every target
device before release. The repository version is `0.2.0`; it is not the
current public npm release. Use the immutable preview artifact or reference
supplied for your partner integration.

## Install

Install the immutable `0.2.0` preview artifact or reference supplied for your
partner integration. The public npm channel currently provides `0.1.2`, which
does not necessarily match this preview README. Add `react-native-webview` and,
when durable native credentials are needed, `react-native-keychain` as peer
dependencies.

Give a coding agent the placement ID, immutable SDK source, target slot, reward
action, and verification commands. Require it to load the current
[porting documentation](https://levelmoment.com/docs/porting) and this package's
[migration guide](./MIGRATION.md) before it edits the game.

Install `react-native-keychain` when the app needs durable native credential
storage. Expo Go uses the hosted storage fallback; use a development build for
native keychain support.

## Use

Mount the host once at the application root:

```tsx
import { LevelMomentAdModal } from "@levelmoment/sdk-react-native";

export function App() {
  return (
    <>
      <Game />
      <LevelMomentAdModal />
    </>
  );
}
```

Create a break with the default production service:

```ts
import { LevelMomentAd } from "@levelmoment/sdk-react-native";

function showBreak() {
  pauseGame();
  const ad = LevelMomentAd.createForAdRequest("YOUR_PLACEMENT_ID", {
    slot: {
      adType: "rewarded",
      targetDurationSeconds: 30,
      slotType: "item_reward",
      dimensions: { item: "sword", color: "blue" },
    },
  });
  let granted = false;
  let finished = false;
  const finish = () => {
    if (finished) return;
    finished = true;
    ad.dispose();
    resumeGame();
    prepareNextBreak();
  };

  ad.addAdEventListener("loaded", () => ad.show());
  ad.addAdEventListener("earnedReward", ({ rewardId }) => {
    if (!finished && !granted) {
      granted = true;
      recordGrantedReward(rewardId);
      grantBonus();
    }
  });
  ad.addAdEventListener("closed", finish);
  ad.addAdEventListener("error", finish);
  ad.load();
}
```

`load()` prepares a new handle. The hosted activity loads when `show()` opens,
so there is no instant-display promise. Create a new handle for each target
slot, grant once for the server-confirmed passed break, and resume after
`closed` or an error. Dedupe grants by `rewardId = breakSessionId`; the game
owns the item or currency amount. Register `slotType` and dimension codes in
the studio's Break performance screen before using them. One `placementId`
supports many items and level transitions; an integer `afterLevel` dimension
lets the report compare individual levels without registering each level.

The normal configuration uses the canonical Level Moment hosted service. Use
`unsafeTesting` only for local or sandbox endpoints and an `eply_sbx_` test
credential; do not put production URLs or player tokens in the app config.

## Optional learning-access flow

When a player chooses learning, open the access flow:

```ts
import { ensureAccess } from "@levelmoment/sdk-react-native";

const result = await ensureAccess({ placementId: "YOUR_PLACEMENT_ID" });
if (result === "ready") openLearningChoice();
else resumeGame();
```

This does not need to block normal game startup. The result is `ready`,
`canceled`, or `technicalFailure`; preserve gameplay when it is canceled.
Use `checkAccess({ placementId })` for a noninteractive check: `false` means
the player needs the access flow, while a technical failure rejects the
promise.

See [`MIGRATION.md`](MIGRATION.md) and the signed-in [developer documentation](https://levelmoment.com/docs) for more details.
