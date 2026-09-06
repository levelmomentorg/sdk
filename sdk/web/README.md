# @levelmoment/sdk-web

Level Moment is a rewarded break for browser and HTML5 games. The SDK opens
the hosted experience in a fullscreen iframe and reports familiar rewarded
placement events.

**Preview:** Validate the hosted break and browser behavior in every target
browser before release. The repository version is `0.2.0`; it is not the
current public npm release. Use the immutable preview artifact or reference
supplied for your partner integration.

## Install

Install the immutable `0.2.0` preview artifact or reference supplied for your
partner integration. The public npm channel currently provides `0.1.2`, which
does not necessarily match this preview README.

Use `AGENT-INSTRUCTIONS.md` in the complete partner packet supplied with this
preview. Give the agent that packet, the immutable artifact or source reference,
the placement ID, target slot, and reward action.

## Use

```ts
import {
  LevelMomentWebClient,
  type LevelMomentWebAd,
} from "@levelmoment/sdk-web";

const client = LevelMomentWebClient.initialize({
  placementId: "YOUR_PLACEMENT_ID",
});

let nextAd: LevelMomentWebAd | undefined;
const prepare = () => {
  client.loadAd({
    onAdLoaded: (ad) => (nextAd = ad),
    onAdFailedToLoad: (error) => console.error(error),
  });
};

function showBreak() {
  const ad = nextAd;
  if (!ad) return;
  nextAd = undefined;
  pauseGame();

  let granted = false;
  let finished = false;
  const finish = () => {
    if (finished) return;
    finished = true;
    resumeGame();
    prepare();
  };
  ad.show({
    onUserEarnedReward: ({ amount }) => {
      if (!finished && amount === 1 && !granted) {
        granted = true;
        grantBonus();
      }
    },
    onAdDismissed: finish,
    onAdFailedToShow: finish,
  });
}

prepare();
```

`loadAd()` prepares an ad handle. The hosted break loads when `show()` opens;
the SDK does not promise an instant display or preload the activity. Always
resume the game after dismissal or show failure. An optional `rewardId` is an
opaque correlation value for server callbacks.

The standard production configuration needs only `placementId`; the SDK always
uses the canonical Level Moment hosted origin. For local or sandbox development,
pass test endpoints and an `eply_sbx_` credential through `unsafeTesting`. Use
`mock: true` for local UI work without live questions. Do not set production
URLs or player tokens in the normal configuration.

## Optional learning-access flow

When a player chooses learning, open the access flow:

```ts
const result = await client.ensureAccess();
if (result === "ready") openLearningChoice();
else resumeGame();
```

This does not need to block normal game startup. The flow returns `ready`,
`canceled`, or `technicalFailure`; preserve gameplay when it is canceled.
Use `client.checkAccess()` for a noninteractive check: `false` means the
player needs the access flow, while a technical failure rejects the promise.

See [`MIGRATION.md`](MIGRATION.md) and the signed-in [developer documentation](https://levelmoment.com/docs) for more details.

Create a new handle after every terminal callback. The example grants one game
bonus on the first correct answer and resumes play once on dismissal or
failure. An earned bonus survives a later show failure.
