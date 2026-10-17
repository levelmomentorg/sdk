# Migrate a Unity rewarded placement

## Prerequisites

- Unity 2021.3 or later
- A pinned gree Unity WebView provider

## 1. Replace the packages

Add the pinned SDK and WebView packages to `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.levelmoment.sdk": "https://github.com/levelmomentorg/sdk.git?path=/sdk/unity#sdk-v0.3.0",
    "net.gree.unity-webview": "https://github.com/gree/unity-webview.git?path=/dist/package-nofragment#PINNED_GREE_COMMIT",
    "com.unity.inputsystem": "1.11.2",
    "com.unity.modules.jsonserialize": "1.0.0"
  }
}
```

Do not use a moving branch such as `main`.

Add `LEVELMOMENT_GREE_WEBVIEW` to Scripting Define Symbols and preserve the
runtime assemblies in `Assets/link.xml`. See `README.md` for the provider
setup.

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
A `ready` result means the household had a subscription, or complimentary
access, when the parent linked the game; a lapse afterward does not unlink the
device.

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
Deduplicate game grants by `RewardId = breakSessionId`. Dismissal and failure
only resume the game; the game chooses the item or currency amount.

Declare the slot this placement fills with `LevelMomentAdSlot`: the ad type
("rewarded" when it grants something, "interstitial" when it doesn't),
how long the moment holds the player (30 seconds for a reward, 18 for resuming
play, never under 18) and the reward amount, for reporting. These replace the
old `format` choice. Pass `format: "practice_set"` with a rewarded slot as a floor, as the
example does. See
[slot sizes](https://levelmoment.com/docs/getting-started#slot-size).

## 5. Replace interstitial loading (optional)

`InterstitialAd` swaps in for AdMob's `InterstitialAd` the same way: keep the
load and show slots, drop the reward callback.

```csharp
InterstitialAd pending = null;
InterstitialAd.Load("YOUR_PLACEMENT_ID", new InterstitialAdLoadCallbacks
{
    OnAdLoaded = ad => pending = ad,
    OnAdFailedToLoad = error => ResumeGame(),
});
if (pending == null) return;

pending.Show(new InterstitialAdShowCallbacks
{
    OnAdDismissed = () => ResumeGame(),
    OnAdFailedToShow = error => ResumeGame(),
});
```

No reward guard is needed — an interstitial break has nothing to grant.
Dismissal and failure both resume the game, exactly as for a rewarded break.

## 6. Configure sandbox testing

Pass your `eply_sbx_` sandbox token alone inside `UnsafeTesting`, read only
in the editor and development builds. [Testing and
sandbox](https://levelmoment.com/docs/testing#configure) shows the token file
and the build guard that fails a release build carrying it:

```csharp
var config = new LevelMomentConfig();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
var sandboxToken = UnityEngine.Resources.Load<UnityEngine.TextAsset>("LevelMomentSandboxToken")?.text.Trim();
if (!string.IsNullOrEmpty(sandboxToken)) config.UnsafeTesting = new UnsafeTesting { Token = sandboxToken };
#endif
LevelMomentAds.Initialize(config);
```

**Warning:** A release build must contain no `eply_sbx_` token. A build that
carries one serves every player sandbox breaks that earn nothing.

## Verify the migration

1. Compile in the editor and make an il2cpp build.
2. Confirm `CheckAccess()` stays hidden.
3. Confirm `EnsureAccess()` opens pairing when action is required.
4. Complete a break and confirm dismissal resumes the game once.
5. Remove the provider define in a test branch and confirm the failure callback
   reports the missing provider.

Compare the loading flow with the
[Unity example](Samples~/BasicIntegration/AdBreakExample.cs).
