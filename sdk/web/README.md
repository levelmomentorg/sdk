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

Give a coding agent the placement ID, immutable SDK source, target slot, reward
action, and verification commands. Require it to load the current
[porting documentation](https://levelmoment.com/docs/porting) and this package's
[migration guide](./MIGRATION.md) before it edits the game.

## Use

```ts
import {
  LevelMomentWebClient,
  type LevelMomentWebAd,
} from "@levelmoment/sdk-web";

const client = LevelMomentWebClient.initialize({
  placementId: "YOUR_PLACEMENT_ID",
  slot: {
    adType: "rewarded",
    targetDurationSeconds: 30,
    slotType: "item_reward",
    dimensions: { item: "sword", color: "blue" },
  },
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
    onUserEarnedReward: ({ rewardId }) => {
      if (!finished && !granted) {
        granted = true;
        recordGrantedReward(rewardId);
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
resume the game after dismissal or show failure. The callback fires once for a
server-confirmed passed graded break. Its `rewardId` equals `breakSessionId`;
dedupe grants by that ID. Your game decides the item or currency amount.

Register `item_reward`, `item`, and `color` in the game's Break performance
screen before sending those codes. The same `placementId` serves sword,
character, costume, and level-transition breaks; use an integer `afterLevel`
dimension to group all level breaks and compare individual levels. Unknown or
retired reporting fields are omitted from reports without blocking the break.

The standard production configuration needs only `placementId`; the SDK always
uses the canonical Level Moment hosted origin. For local or sandbox development,
pass test endpoints and an `eply_sbx_` credential through `unsafeTesting`. Use
`mock: true` for local UI work without live questions. Do not set production
URLs or player tokens in the normal configuration.

## Test the real pairing path locally

`unsafeTesting.realPairing` runs pairing, a learner session, and the learner's own topics against a hosted page on your machine, instead of sandbox content.

Prerequisites:

- The Level Moment API and web app running locally, with a placement registered for your game.
- Your game served from `http://127.0.0.1:<port>` or `http://localhost:<port>`. Any other page refuses the option.

Pass the local break page as `breakUrl`. Serve the game from `127.0.0.1` when the page is on `localhost`, so the page's storage is partitioned the way it is in production.

```ts
const client = LevelMomentWebClient.initialize({
  placementId: "YOUR_LOCAL_PLACEMENT_ID",
  unsafeTesting: {
    realPairing: true,
    breakUrl: "http://localhost:3000/break",
  },
});
```

`realPairing` takes no token and no `apiUrl`; the page uses its own API. The SDK logs a warning while it is on.

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
bonus from one server-confirmed passed graded break, deduplicated by
`rewardId`, and resumes play once on dismissal or failure.
