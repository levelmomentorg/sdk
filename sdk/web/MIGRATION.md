# Migrate a web rewarded placement

## Prerequisites

- A placement ID from the Level Moment developer portal
- The immutable core and web 0.2 preview tarballs supplied to you

The public npm package is still 0.1.2. Pin the supplied preview artifacts until
0.2 is published.

## 1. Replace the dependency

Install both preview tarballs so npm does not resolve the older public core:

```bash
npm install ./PROVIDED_SDK_CORE_TARBALL.tgz ./PROVIDED_SDK_WEB_TARBALL.tgz
```

## 2. Replace initialization

Create one client with the placement ID. Production endpoints and player
credentials are managed by Level Moment:

```ts
import { LevelMomentWebClient } from "@levelmoment/sdk-web";

const client = LevelMomentWebClient.initialize({
  placementId: "YOUR_PLACEMENT_ID",
});
```

Do not pass `apiUrl`, `breakUrl`, or `studentToken` in production.

## 3. Add the access flow

Use `checkAccess()` for a quiet check and `ensureAccess()` after the player
chooses learning:

```ts
try {
  if (!(await client.checkAccess())) {
    const result = await client.ensureAccess();
    if (result !== "ready") return;
  }
  openLearningMode();
} catch {
  showTryAgainLater();
}
```

`false` means the player must take action. A rejected check is a technical
failure and does not prove that access is unavailable. Keep
`ensureSignedIn()` or `isSignedIn()` only where the feature needs identity
without checking access.

## 4. Replace rewarded loading

Keep the existing preload, reward, dismissal, and failure slots:

```ts
import type { LevelMomentWebAd } from "@levelmoment/sdk-web";

let pending: LevelMomentWebAd | null = null;
client.loadAd({
  onAdLoaded: (ad) => (pending = ad),
  onAdFailedToLoad: () => resumeGame(),
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
    onAdDismissed: () => resumeGame(),
    onAdFailedToShow: () => resumeGame(),
  });
}
```

A multi-question break can emit several reward callbacks. Guard the game
reward so the first correct answer grants it once.

## 5. Configure sandbox testing

Put local endpoints and an `eply_sbx_` credential inside `unsafeTesting`:

```ts
const client = LevelMomentWebClient.initialize({
  placementId: "YOUR_PLACEMENT_ID",
  unsafeTesting: {
    apiUrl: "http://localhost:8080",
    breakUrl: "http://localhost:3000/break",
    token: "YOUR_EPLY_SBX_TOKEN",
  },
});
```

**Warning:** Remove `unsafeTesting` from production builds. Production
configuration rejects endpoint overrides and explicit player tokens.

## Verify the migration

1. Run the game's TypeScript check and production build.
2. Confirm `checkAccess()` stays invisible.
3. Confirm `ensureAccess()` opens the hosted flow when action is required.
4. Complete a break and confirm the game resumes exactly once.
5. Confirm a multi-question break grants its game reward at most once.

Compare the result with the [web example](example/main.ts).
