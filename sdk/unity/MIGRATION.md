# Migrate a Unity rewarded placement

## Prerequisites

- Unity 2021.3 or later
- The immutable 0.2 preview commit supplied to you
- A pinned gree Unity WebView provider

## 1. Replace the packages

Add the pinned SDK and WebView packages to `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.levelmoment.sdk": "https://github.com/levelmomentorg/sdk.git?path=sdk/unity#PROVIDED_PREVIEW_COMMIT",
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

## 4. Replace rewarded loading

Keep the load and show slots, then map the callbacks:

```csharp
RewardedAd pending = null;
RewardedAd.Load("YOUR_PLACEMENT_ID", new RewardedAdLoadCallbacks
{
    OnAdLoaded = ad => pending = ad,
    OnAdFailedToLoad = error => ResumeGame(),
});
if (pending == null) return;

bool rewardGranted = false;
pending.Show(new RewardedAdShowCallbacks
{
    OnUserEarnedReward = amount =>
    {
        if (amount == 1 && !rewardGranted)
        {
            rewardGranted = true;
            GrantBonus();
        }
    },
    OnAdDismissed = () => ResumeGame(),
    OnAdFailedToShow = error => ResumeGame(),
});
```

A multi-question break can emit several reward callbacks. Guard the game
reward so the first correct answer grants it once. Dismissal and failure only
resume the game.

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

Put local endpoints and the sandbox credential inside `UnsafeTesting`:

```csharp
LevelMomentAds.Initialize(new LevelMomentConfig
{
    UnsafeTesting = new UnsafeTesting
    {
        ApiUrl = "http://YOUR_COMPUTER_IP:8080",
        BreakUrl = "http://YOUR_COMPUTER_IP:3000/break",
        Token = "YOUR_EPLY_SBX_TOKEN",
    },
});
```

**Warning:** Remove `UnsafeTesting` from production builds. Production
configuration rejects endpoint overrides and explicit player tokens.

## Verify the migration

1. Compile in the editor and make an il2cpp build.
2. Confirm `CheckAccess()` stays hidden.
3. Confirm `EnsureAccess()` opens pairing when action is required.
4. Complete a break and confirm dismissal resumes the game once.
5. Remove the provider define in a test branch and confirm the failure callback
   reports the missing provider.

Compare the loading flow with the
[Unity example](Samples~/BasicIntegration/AdBreakExample.cs).
