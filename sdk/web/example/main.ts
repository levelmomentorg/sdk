import {
  LevelMomentWebClient,
  type LevelMomentWebAd,
} from "@levelmoment/sdk-web";

const placementId = "YOUR_PLACEMENT_ID";
const client = LevelMomentWebClient.initialize({ placementId });
let pending: LevelMomentWebAd | null = null;

client.loadAd({
  onAdLoaded: (ad) => {
    pending = ad;
  },
  onAdFailedToLoad: (error) =>
    console.error("Level Moment load failed", error.code),
});

export function showRewardedBreak(): void {
  const ad = pending;
  pending = null;
  if (!ad) return;

  let rewardGranted = false;
  ad.show({
    onUserEarnedReward: ({ amount }) => {
      if (amount === 1 && !rewardGranted) {
        rewardGranted = true;
        grantBonus();
      }
    },
    onAdDismissed: () => {
      resumeGame();
    },
    onAdFailedToShow: () => resumeGame(),
  });
}

function grantBonus(): void {
  console.info("Grant the game's configured bonus");
}

function resumeGame(): void {
  console.info("Resume the game");
}
