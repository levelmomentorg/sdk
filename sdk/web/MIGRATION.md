# Migrate a web rewarded placement

## Prerequisites

- A placement ID from the Level Moment developer portal

## 1. Replace the dependency

Install version 0.3.1 from npm. It brings the matching `@levelmoment/sdk-core`:

```bash
npm install @levelmoment/sdk-web@0.3.1
```

## 2. Replace initialization

Create one client per moment with the placement ID and a slot declaration.
Production endpoints and player credentials are managed by Level Moment:

```ts
import { LevelMomentWebClient } from "@levelmoment/sdk-web";

const client = LevelMomentWebClient.initialize({
  placementId: "YOUR_PLACEMENT_ID",
  slot: { adType: "rewarded", targetDurationSeconds: 30, rewardAmount: 1 },
});
```

Declare the slot this placement fills: `adType` (`"rewarded"` when it grants
something, `"interstitial"` when it doesn't), `targetDurationSeconds` (how
long the moment holds the player: 30 for a reward, 18 for resuming play, never
under 18) and `rewardAmount` (what the game grants, for reporting). These
replace the old `format` choice. Pass `format: "practice_set"` with a rewarded slot as a floor,
as the example does. See
[slot sizes](https://levelmoment.com/docs/getting-started#slot-size).

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
`ensureSignedIn()` or `isSignedIn()` only where the feature needs identity.
A `ready` result means the household had a subscription, or complimentary
access, when the parent linked the game; a lapse afterward does not unlink the
device.

## 4. Replace rewarded loading

Keep the existing preload, reward, dismissal, and failure slots:

```ts
import type { LevelMomentWebAd } from "@levelmoment/sdk-web";

let pending: LevelMomentWebAd | null = null;
client.loadAd(
  {
    onAdLoaded: (ad) => (pending = ad),
    onAdFailedToLoad: () => resumeGame(),
  },
  { format: "practice_set" },
);

export function showRewardedBreak(): void {
  const ad = pending;
  pending = null;
  if (!ad) return;

  let rewardGranted = false;
  ad.show({
    onUserEarnedReward: ({ rewardId }) => {
      if (!rewardGranted) {
        rewardGranted = true;
        recordGrantedReward(rewardId);
        grantBonus();
      }
    },
    onAdDismissed: () => resumeGame(),
    onAdFailedToShow: () => resumeGame(),
  });
}
```

A passed graded break emits one reward callback, regardless of question count.
Deduplicate grants with `rewardId = breakSessionId`; do not grant for a child
answer alone. The game owns the reward value.

## 5. Configure sandbox testing

Pass your `eply_sbx_` sandbox token alone inside `unsafeTesting`, read from a
file only sandbox builds load. [Testing and
sandbox](https://levelmoment.com/docs/testing#configure) shows the file, the
build scripts and the build guard:

```ts
const sandboxToken = import.meta.env.VITE_LM_SANDBOX_TOKEN as
  | string
  | undefined;

const client = LevelMomentWebClient.initialize({
  placementId: "YOUR_PLACEMENT_ID",
  slot: { adType: "rewarded", targetDurationSeconds: 30, rewardAmount: 1 },
  // Sandbox builds only. A production build passes no unsafeTesting.
  ...(sandboxToken ? { unsafeTesting: { token: sandboxToken } } : {}),
});
```

**Warning:** A production build must contain no `eply_sbx_` token. A build
that carries one serves every player sandbox breaks that earn nothing.

## Verify the migration

1. Run the game's TypeScript check and production build.
2. Confirm `checkAccess()` stays invisible.
3. Confirm `ensureAccess()` opens the hosted flow when action is required.
4. Complete a break and confirm the game resumes exactly once.
5. Confirm a multi-question break grants its game reward at most once.

Compare the result with the [web example](example/main.ts).
