# Migrate a Unity rewarded placement

## Prerequisites

- Unity 2021.3 LTS or later, including Unity 6 (6000.x)
- A placement ID from the game's page at <https://levelmoment.com/games>.
  It starts with `pl_`.
- A pinned gree Unity WebView provider

## 1. Replace the packages

Add the pinned SDK and WebView packages to `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.levelmoment.sdk": "https://github.com/levelmomentorg/sdk.git?path=/sdk/unity#sdk-v0.4.3",
    "net.gree.unity-webview": "https://github.com/gree/unity-webview.git?path=/dist/package-nofragment#20f14061ca4b45e8490c1e89e00684a5d3ed6be0"
  }
}
```

Do not use a moving branch such as `main`. The SDK brings the Input System
package and the web request module, turns on its gree adapter, and preserves
its assemblies from stripping. See `README.md` for the provider setup.

### From 0.3.1

- Remove `LEVELMOMENT_GREE_WEBVIEW` from Scripting Define Symbols and delete
  `Assets/link.xml` if it only preserved the SDK and gree assemblies.
- Move `Assets/Resources/LevelMomentSandboxToken.txt` to
  `UserSettings/LevelMomentSandboxToken.txt` and delete its `.meta` file. A
  release build fails while it stays under `Resources`.
- Delete the game's own code that loads the token into `UnsafeTesting`, and
  its sandbox build guard. The SDK does both.

## 2. Replace initialization

```csharp
using LevelMoment;

LevelMomentAds.Initialize(new LevelMomentConfig());
```

Production endpoints and player credentials are managed by Level Moment. Do
not set `ApiUrl`, `BreakUrl`, or an explicit token.

## 3. Add the access flow

Use `CheckAccess()` for an invisible check and `EnsureAccess()` after the
player chooses learning:

```csharp
LevelMomentAds.CheckAccess(
    "YOUR_PLACEMENT_ID",
    ready =>
    {
        if (ready)
        {
            OpenLearningMode();
            return;
        }
        LevelMomentAds.EnsureAccess("YOUR_PLACEMENT_ID", result =>
        {
            if (result == EnsureSignedInResult.Ready) OpenLearningMode();
            else if (result == EnsureSignedInResult.TechnicalFailure) ShowTryAgainLater();
        });
    },
    error => ShowTryAgainLater());
```

Use `EnsureSignedIn()` or `IsSignedIn()` only for identity-only features.
A `ready` result means the player's Level Moment household had a subscription,
or complimentary access, when the device was linked; a lapse afterward does not
unlink the device. Call `EnsureAccess()` from the player's tap, before each
break, never at startup.

## 4. Replace rewarded loading

Keep the load and show slots, then map the callbacks:

```csharp
RewardedAd pending = null;
var slot = new LevelMomentAdSlot("rewarded", 30, rewardAmount: 1);
RewardedAd.Load("YOUR_PLACEMENT_ID", new RewardedAdLoadCallbacks
{
    OnAdLoaded = ad => pending = ad,
    OnAdFailedToLoad = error => ResumeGame(),
}, format: "practice_set", slot: slot);
if (pending == null) return;

bool rewardGranted = false;
pending.Show(new RewardedAdShowCallbacks
{
    OnUserEarnedReward = reward =>
    {
        if (!rewardGranted)
        {
            rewardGranted = true;
            RecordGrantedReward(reward.RewardId);
            GrantBonus();
        }
    },
    OnAdDismissed = () => ResumeGame(),
    OnAdFailedToShow = error => ResumeGame(),
});
```

A passed graded break emits one reward callback, regardless of question count.
Deduplicate game grants by `RewardId = breakSessionId`. A break that ends
without a reward, because the player didn't pass it, reaches `OnAdDismissed`
with no reward callback: grant nothing and resume the game. Failure also only
resumes the game. The game chooses the item or currency amount.

Declare the slot this placement fills with `LevelMomentAdSlot`: the ad type
("rewarded" when it grants something, "interstitial" when it doesn't),
how long the moment holds the player (30 seconds for a reward, 18 for resuming
play, never under 18) and the reward amount, for reporting. These replace the
old `format` choice. Pass `format: "practice_set"` with a rewarded slot as a
floor, as the example does. See
[slot sizes](https://levelmoment.com/docs/getting-started#slot-size).

A revive, a retry that refills lives, or a second chance grants something, so
it is a rewarded slot: declare `"rewarded"` with 18 seconds. `rewardAmount` is
only reported to Level Moment and changes nothing the player gets. When the
reward varies, pass what this show grants, such as 1 for one revive, or 0 when
it isn't a count.

## 5. Replace interstitial loading (optional)

`InterstitialAd` swaps in for AdMob's `InterstitialAd` the same way: keep the
load and show call sites, drop the reward callback, and declare an
`"interstitial"` slot of 18 seconds. Pass no `format`.

```csharp
InterstitialAd pending = null;
var slot = new LevelMomentAdSlot("interstitial", 18);
InterstitialAd.Load("YOUR_PLACEMENT_ID", new InterstitialAdLoadCallbacks
{
    OnAdLoaded = ad => pending = ad,
    OnAdFailedToLoad = error => ResumeGame(),
}, slot: slot);
if (pending == null) return;

pending.Show(new InterstitialAdShowCallbacks
{
    OnAdDismissed = () => ResumeGame(),
    OnAdFailedToShow = error => ResumeGame(),
});
```

An interstitial break grants nothing, so it needs no reward guard. Map
dismissal and failure to whatever your old interstitial's close callback did.
See [InterstitialAd](README.md#interstitialad) to tell a break that never
loaded from one the player finished.

## 6. Configure sandbox testing

Put the game's `eply_sbx_` sandbox token alone in
`UserSettings/LevelMomentSandboxToken.txt` in the Unity project folder:

```bash
mkdir -p UserSettings && pbpaste > UserSettings/LevelMomentSandboxToken.txt
```

The SDK reads it in the editor and development builds, and a release build
never includes it. Write no code that loads the token and no build guard;
`new LevelMomentConfig()` is the whole configuration. Don't put the token under
a `Resources` folder: a release build fails while one is there. [Testing and
sandbox](https://levelmoment.com/docs/testing#configure) covers checking that
the file is gitignored.

**Warning:** A release build must contain no `eply_sbx_` token. A build that
carries one serves every player sandbox breaks that earn nothing.
[Testing and sandbox](https://levelmoment.com/docs/testing#production-build)
shows how to search an iOS export and an Android build for it.

## 7. Optional: require an internet connection

Set `RequireInternet = true` and `GameName` in `LevelMomentConfig` to block
play with a full-screen "No internet connection" message while the device is
offline. Pause gameplay and input from `OnConnectionChange`. See
[Require an internet connection](README.md#require-an-internet-connection).

## Verify the migration

1. Compile in the editor and make an il2cpp build. For iOS, build the exported
   Xcode project once for a device, without signing, as
   [Going live](https://levelmoment.com/docs/going-live#token) shows. A batch
   build passes `-buildTarget iOS`.
2. Confirm `CheckAccess()` stays hidden.
3. Confirm `EnsureAccess()` opens pairing when action is required.
4. Complete a break and confirm dismissal resumes the game once.
5. Remove the gree package in a test branch and confirm the failure callback
   reports the missing provider.
6. Turn on airplane mode and confirm a break fails at once and the game
   resumes. With `RequireInternet`, confirm the message appears and goes away
   when the connection returns.

Compare the loading flow with the
[Unity example](Samples~/BasicIntegration/AdBreakExample.cs).
