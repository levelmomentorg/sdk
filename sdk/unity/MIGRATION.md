# Migrating from AdMob (Google Mobile Ads) rewarded ads to LevelMoment

LevelMoment mirrors the AdMob Unity plugin's **rewarded ad** API: `Load()` in the
background, `Show()` at a pause point, grant a bonus in the reward callback,
resume the game when it closes. The swap is mostly renaming — the load → show →
resume structure is identical.

**One real difference, called out up front:** AdMob renders a video natively.
LevelMoment renders an educational break in a **WebView**, so you must install a
WebView provider. See step 1. There is no fallback renderer — without a provider,
`Show()` fails cleanly via `OnAdFailedToShow` (you just resume the game, exactly
like AdMob's `OnAdFullScreenContentFailed`).

## 1. Install a WebView provider (new — no AdMob equivalent)

Unity has no built-in WebView. Add [`gree/unity-webview`](https://github.com/gree/unity-webview)
and the `LEVELMOMENT_GREE_WEBVIEW` scripting define — see
[README.md → Requirements](./README.md#requirements). (Prefer another plugin?
Implement `ILevelMomentWebView` and register it.)

## 2. Replace the namespace import

```csharp
// REMOVE:
using GoogleMobileAds.Api;

// ADD:
using LevelMoment;
```

Both plugins expose a type named `RewardedAd`; the namespace swap disambiguates it.

## 3. Replace initialization

```csharp
// BEFORE (AdMob):
MobileAds.Initialize(initStatus => { });

// AFTER (LevelMoment):
LevelMomentAds.Initialize(new LevelMomentConfig
{
    ApiUrl   = "https://api.levelmoment.com",
    BreakUrl = "https://app.levelmoment.com/break",   // the hosted break page
});
```

## 4. Replace Load()

```csharp
// BEFORE (AdMob):
RewardedAd _rewardedAd;
RewardedAd.Load(adUnitId, new AdRequest(), (RewardedAd ad, LoadAdError error) =>
{
    if (error != null || ad == null) { Retry(); return; }
    _rewardedAd = ad;
});

// AFTER (LevelMoment):
RewardedAd _rewardedAd;
RewardedAd.Load("your-placement-id", new RewardedAdLoadCallbacks
{
    OnAdLoaded       = ad  => _rewardedAd = ad,
    OnAdFailedToLoad = err => Retry(),
});
```

`Load()` is synchronous (no network) — the hosted page fetches the question when
`Show()` opens it. There is no `AdRequest`.

## 5. Replace the full-screen callbacks + Show()

AdMob wires `FullScreenContentCallback` handlers on the ad, then calls
`Show(reward => …)`. LevelMoment passes one `RewardedAdShowCallbacks` bundle to
`Show()`:

```csharp
// BEFORE (AdMob):
_rewardedAd.OnAdFullScreenContentOpened += () => Pause();
_rewardedAd.OnAdFullScreenContentClosed += () => { Resume(); PreloadNext(); };
_rewardedAd.OnAdFullScreenContentFailed += (AdError e) => Resume();
_rewardedAd.Show((Reward reward) =>
{
    if (reward.Amount > 0) GrantBonus();
});

// AFTER (LevelMoment):
_rewardedAd.Show(new RewardedAdShowCallbacks
{
    OnAdShowedFullScreenContent = ()     => Pause(),
    OnUserEarnedReward          = amount => { if (amount == 1) GrantBonus(); },
    OnAdDismissed               = ()     => { Resume(); PreloadNext(); },
    OnAdFailedToShow            = err    => Resume(),
});
```

## Event / API mapping

| AdMob (Google Mobile Ads)                             | LevelMoment                                       | Notes                                            |
| ----------------------------------------------------- | ------------------------------------------------- | ------------------------------------------------ |
| `MobileAds.Initialize(cb)`                            | `LevelMomentAds.Initialize(config)`               | config = `ApiUrl` + `BreakUrl` (+ `Mock`)        |
| `RewardedAd.Load(adUnitId, adRequest, cb)`            | `RewardedAd.Load(placementId, callbacks)`         | synchronous; no `AdRequest`                      |
| `LoadAdError` in the load callback                    | `RewardedAdLoadCallbacks.OnAdFailedToLoad(error)` | `LevelMomentAdError { Code, Message }`           |
| `rewardedAd.Show(reward => …)`                        | `ad.Show(callbacks)`                              | reward handler moves into the callback bundle    |
| `Reward.Amount` (`> 0` = earned)                      | `OnUserEarnedReward(int amount)`                  | `1` = correct answer, `0` = wrong/skipped        |
| `OnAdFullScreenContentOpened`                         | `OnAdShowedFullScreenContent`                     | fires on the page's `ready`                      |
| `OnAdFullScreenContentClosed`                         | `OnAdDismissed`                                   | fires exactly once; always resume here           |
| `OnAdFullScreenContentFailed(AdError)`                | `OnAdFailedToShow(LevelMomentAdError)`            | includes the no-WebView-provider case            |
| `OnAdImpressionRecorded` / `OnAdClicked` / `OnAdPaid` | _(none)_                                          | impressions are recorded server-side by the page |
| `rewardedAd.Destroy()`                                | `ad.Destroy()`                                    | identical intent                                 |
| _(native video)_                                      | hosted `/break` WebView                           | **requires a WebView provider** (step 1)         |

## Getting the student token

The parent portal embeds a short-lived session token in the game's launch URL.
The SDK reads `?token=` from `Application.absoluteURL` (WebGL) / the deep-link URL
automatically. To supply it yourself, use the explicit-token overload:

```csharp
RewardedAd.Load("your-placement-id", GetTokenFromUrl(), new RewardedAdLoadCallbacks { /* … */ });
```

## Full before/after example

See [`Samples~/BasicIntegration/AdBreakExample.cs`](./Samples~/BasicIntegration/AdBreakExample.cs)
for a complete `Initialize → Load → Show → resume` MonoBehaviour.
