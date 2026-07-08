# Migrating from react-native-google-mobile-ads to LevelMoment

## 1. Replace the dependency

```bash
# REMOVE:
npm uninstall react-native-google-mobile-ads

# ADD:
npm install @levelmoment/sdk-react-native
```

## 2. Replace the import

```ts
// REMOVE:
import {
  RewardedAd,
  RewardedAdEventType,
  AdEventType,
} from "react-native-google-mobile-ads";

// ADD:
import { LevelMomentAd } from "@levelmoment/sdk-react-native";
```

## 3. Replace ad creation

```ts
// BEFORE:
const rewardedAd = RewardedAd.createForAdRequest("ca-app-pub-xxx/yyy", {
  requestNonPersonalizedAdsOnly: true,
});

// AFTER (identical method name — swap class name, add apiUrl + studentToken):
const rewardedAd = LevelMomentAd.createForAdRequest("your-game-id", {
  apiUrl: "https://api.levelmoment.com", // provided by LevelMoment
  studentToken: getTokenFromUrl(), // from ?token= URL param (new — one extra line)
});
```

## 4. Replace event listeners

```ts
// BEFORE:
const unsubscribeLoaded = rewardedAd.addAdEventListener(
  RewardedAdEventType.LOADED,
  () => setLoaded(true),
);
const unsubscribeEarned = rewardedAd.addAdEventListener(
  RewardedAdEventType.EARNED_REWARD,
  (reward) => grantBonus(reward),
);
const unsubscribeClosed = rewardedAd.addAdEventListener(
  AdEventType.CLOSED,
  () => resumeGame(),
);

// AFTER (identical structure — swap event type constants for strings):
const unsubscribeLoaded = rewardedAd.addAdEventListener("loaded", () =>
  setLoaded(true),
);
const unsubscribeEarned = rewardedAd.addAdEventListener(
  "earnedReward",
  (reward) => {
    grantBonus(reward);
    // reward.amount === 1 → correct answer
    // reward.amount === 0 → skipped / wrong answer
  },
);
const unsubscribeClosed = rewardedAd.addAdEventListener("closed", () =>
  resumeGame(),
);
```

## 5. load() and show() — unchanged

```ts
// BEFORE and AFTER — identical:
rewardedAd.load();

// later, at the natural pause point:
if (loaded) {
  rewardedAd.show();
}
```

## 6. Render the question

The one genuine difference: LevelMoment returns a **question** your app must render.

```ts
// Listen for the ad to load, then access the question:
rewardedAd.addAdEventListener("loaded", () => {
  setLoaded(true);
  // The ad handle exposes .question — render it in your UI
});

// When the player answers, the SDK fires earnedReward automatically.
// No extra code needed — notifyCorrectAnswer/notifyDismissed are called
// internally by the SDK when the question UI reports an answer.
```

## Event name mapping

| react-native-google-mobile-ads      | LevelMoment      |
| ----------------------------------- | ---------------- |
| `RewardedAdEventType.LOADED`        | `'loaded'`       |
| `AdEventType.ERROR`                 | `'error'`        |
| `AdEventType.OPENED`                | `'opened'`       |
| `AdEventType.CLOSED`                | `'closed'`       |
| `RewardedAdEventType.EARNED_REWARD` | `'earnedReward'` |

## Getting a student token

```ts
import { Linking } from "react-native";

async function getTokenFromUrl(): Promise<string> {
  const url = await Linking.getInitialURL();
  if (!url) return "";
  const params = new URL(url).searchParams;
  return params.get("token") ?? "";
}
```
