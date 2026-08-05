# Port — Unity (Unity Ads SDK / AdMob Unity → LevelMoment)

You are inside `/levelmoment port`, platform = unity.

The LevelMoment Unity SDK is a **thin WebView shell** (ADR-001): it renders
nothing. `Show()` opens the hosted `/break` page in a fullscreen WebView and
calls back on reward / dismiss. The old "returns a `Question` you render" API is
gone — do **not** reintroduce `ILevelMomentShowListener`, `NotifyAnswer`,
`RenderQuestion`, or a `Question` type. Mirror the AdMob rewarded-ad load → show →
resume shape instead.

## What the LevelMoment Unity SDK looks like

```csharp
using LevelMoment;

public class AdsManager : MonoBehaviour
{
    private const string PlacementId = "{{PLACEMENT_ID}}";
    private RewardedAd _rewardedAd;

    void Awake()
    {
        LevelMomentAds.Initialize(new LevelMomentConfig
        {
            ApiUrl   = "{{API_URL}}",
            BreakUrl = "{{BREAK_URL}}",   // the hosted /break page
        });
        LoadNext();
    }

    void LoadNext()
    {
        RewardedAd.Load(PlacementId, new RewardedAdLoadCallbacks
        {
            OnAdLoaded       = ad  => _rewardedAd = ad,
            OnAdFailedToLoad = err => Debug.LogWarning(err),
        });
    }

    public void ShowAd()
    {
        if (_rewardedAd == null || !_rewardedAd.IsLoaded) return;
        Time.timeScale = 0f;
        _rewardedAd.Show(new RewardedAdShowCallbacks
        {
            OnUserEarnedReward = amount => { if (amount == 1) GrantReward(); },
            OnAdDismissed      = ()     => { Time.timeScale = 1f; LoadNext(); },
            OnAdFailedToShow   = err    => { Time.timeScale = 1f; },
        });
    }
}
```

Mapping (Unity Ads SDK):

| Unity Ads SDK                                     | LevelMoment                                                  |
| ------------------------------------------------- | ------------------------------------------------------------ |
| `UnityAds.Initialize(gameId, testMode, listener)` | `LevelMomentAds.Initialize(LevelMomentConfig)` (synchronous) |
| `UnityAds.Load(adUnitId, loadListener)`           | `RewardedAd.Load(placementId, RewardedAdLoadCallbacks)`      |
| `UnityAds.Show(adUnitId, showListener)`           | `ad.Show(RewardedAdShowCallbacks)`                           |
| `IUnityAdsLoadListener`                           | `RewardedAdLoadCallbacks` (`OnAdLoaded`, `OnAdFailedToLoad`) |
| `IUnityAdsShowListener`                           | `RewardedAdShowCallbacks` (see below)                        |
| `OnUnityAdsShowComplete(_, COMPLETED)`            | `OnUserEarnedReward(amount)` where `amount == 1`             |
| `UnityAdsShowCompletionState.SKIPPED`             | `OnUserEarnedReward(0)` / `OnAdDismissed`                    |
| `IUnityAdsInitializationListener`                 | (removed — synchronous init)                                 |
| `OnUnityAdsShowClick`                             | (removed — no equivalent)                                    |

For **AdMob Unity plugin** (`GoogleMobileAds.Api`):

| AdMob Unity                              | LevelMoment                                             |
| ---------------------------------------- | ------------------------------------------------------- |
| `MobileAds.Initialize(status => ...)`    | `LevelMomentAds.Initialize(LevelMomentConfig)`          |
| `RewardedAd.Load(adUnitId, request, cb)` | `RewardedAd.Load(placementId, RewardedAdLoadCallbacks)` |
| `rewardedAd.Show(reward => ...)`         | `ad.Show(RewardedAdShowCallbacks)`                      |
| `Reward.Amount` (`> 0` = earned)         | `OnUserEarnedReward(int amount)` (`1` = correct)        |
| `OnAdFullScreenContentOpened`            | `OnAdShowedFullScreenContent`                           |
| `OnAdFullScreenContentClosed`            | `OnAdDismissed` (fires once; always resume here)        |
| `OnAdFullScreenContentFailed(AdError)`   | `OnAdFailedToShow(LevelMomentAdError)`                  |
| `rewardedAd.Destroy()`                   | `ad.Destroy()`                                          |

`RewardedAdShowCallbacks`: `OnUserEarnedReward(int amount)` (1 = correct answer,
0 = wrong/skipped — may fire multiple times), `OnAdDismissed` (fires exactly
once; always resume the game), `OnAdFailedToShow(LevelMomentAdError)`, and the
optional `OnAdShowedFullScreenContent` (fires on the page's `ready`).

## Step 1 — install a WebView provider (new — no AdMob/Unity Ads equivalent)

Unity has no built-in WebView, and the SDK renders the break in one. Add
`gree/unity-webview` and the `LEVELMOMENT_GREE_WEBVIEW` scripting define. In
`Packages/manifest.json`:

```json
"net.gree.unity-webview": "https://github.com/gree/unity-webview.git?path=/dist/package"
```

Then **Project Settings → Player → Other Settings → Scripting Define Symbols**,
add `LEVELMOMENT_GREE_WEBVIEW`. This compiles the bundled gree adapter, which
self-registers as the WebView provider. Without it, `Show()` fails fast via
`OnAdFailedToShow` (never a broken screen). Using another plugin (Vuplex, 3D
WebView)? Implement `ILevelMomentWebView` and call
`LevelMomentWebViewRegistry.Register(() => new YourAdapter())` at startup.

## Step 2 — update `Packages/manifest.json`

```diff
 {
   "dependencies": {
-    "com.unity.ads": "4.x.x",
-    "com.google.ads.mobile": "8.x.x",
+    "com.levelmoment.sdk": "https://github.com/levelmomentorg/sdk.git?path=sdk/unity#v0.1.0",
+    "net.gree.unity-webview": "https://github.com/gree/unity-webview.git?path=/dist/package",
     ...
   }
 }
```

Choose whichever of `com.unity.ads` / `com.google.ads.mobile` was present. If
both exist, ask the user — they may be using AdMob mediation with Unity Ads as an
adapter, which is more complex. Unity's git-URL package format uses `?path=...#<ref>`;
the user may need `Window → Package Manager → Refresh`.

## Step 3 — replace `using` directives

```diff
-using UnityEngine.Advertisements;
+using LevelMoment;
```

or

```diff
-using GoogleMobileAds.Api;
+using LevelMoment;
```

In every `.cs` file from the inventory.

## Step 4 — remove the listener interfaces

```diff
 public class AdsManager : MonoBehaviour
-    , IUnityAdsInitializationListener
-    , IUnityAdsLoadListener
-    , IUnityAdsShowListener
 {
```

LevelMoment uses **callback bundles passed to `Load()`/`Show()`**, not interfaces
the class implements. Delete every `IUnityAds*`/AdMob listener declaration and the
interface methods that back them (`OnUnityAdsAdLoaded`, `OnUnityAdsShowComplete`,
`OnUnityAdsShowClick`, `OnInitializationComplete`, etc.); their logic moves into
the callback lambdas in Steps 5–6.

## Step 5 — replace `Initialize` and `Load`

```diff
 void Awake()
 {
-    UnityAds.Initialize("1234567", false, this);
+    LevelMomentAds.Initialize(new LevelMomentConfig
+    {
+        ApiUrl   = "{{API_URL}}",
+        BreakUrl = "{{BREAK_URL}}",
+    });
+    LoadNext();
 }
+
+private RewardedAd _rewardedAd;
+
+private void LoadNext()
+{
+    RewardedAd.Load("{{PLACEMENT_ID}}", new RewardedAdLoadCallbacks
+    {
+        OnAdLoaded       = ad  => _rewardedAd = ad,
+        OnAdFailedToLoad = err => Debug.LogWarning($"[LevelMoment] load failed: {err}"),
+    });
+}
```

Substitute `{{API_URL}}`, `{{BREAK_URL}}`, `{{PLACEMENT_ID}}`. Init is
synchronous — if the user had an `OnInitializationComplete()` that loaded after
init, fold its load call into `LoadNext()` and remove the method.

The student token is read automatically from the game's launch `?token=` URL
param (`Application.absoluteURL` / deep link). For native builds that need to
supply it explicitly, add the `GetTokenFromUrl()` helper from
`.claude/skills/levelmoment/templates/unity-get-token.cs.tmpl` and use the
explicit-token overload: `RewardedAd.Load("{{PLACEMENT_ID}}", GetTokenFromUrl(), callbacks)`.

## Step 6 — replace `Show`

The SDK renders the break itself — there is **no** `Question` to render and no
`IsReady`/`NotifyAnswer`. Guard on the loaded ad, then `Show()` with the callback
bundle:

```diff
 void ShowAd()
 {
-    if (UnityAds.IsReady("Rewarded_Android"))
-        UnityAds.Show("Rewarded_Android", this);
+    if (_rewardedAd == null || !_rewardedAd.IsLoaded) return;
+    Time.timeScale = 0f;
+    _rewardedAd.Show(new RewardedAdShowCallbacks
+    {
+        OnUserEarnedReward = amount => { if (amount == 1) GrantReward(); },
+        OnAdDismissed      = ()     => { Time.timeScale = 1f; LoadNext(); },
+        OnAdFailedToShow   = err    => { Time.timeScale = 1f; },
+    });
 }
```

Preserve the user's `Time.timeScale = 0f/1f` pause/resume and their
`GrantReward()` / `ResumeGame()` calls — move them into the callbacks. Delete any
`RenderQuestion`, `_currentQuestion`, `OnAnswer`, or `meta.correctIndex` code left
over from the old API; the hosted page handles all of it.

## Step 7 — flag for review

- **AdMob mediation adapters** in `Assets/Plugins/Android/` or via UPM —
  not supported by LevelMoment. Flag.
- **GDPR consent / IDFA tracking permission** — `RequestTrackingAuthorization`
  on iOS, `UMP` integration. Flag — LevelMoment handles consent on the parent
  side.
- **`Application.RequestUserAuthorization`** for ad tracking. Flag.
- **Custom mediation networks** (AppLovin, IronSource, Mintegral). Flag.
- **Banner / interstitial / app-open ads** — not supported. Flag.

## Step 8 — Unity-specific platform tips

- Unity 2021.3 LTS or newer recommended (the WebView plugin requires it).
- iOS builds: enable an "App Transport Security" exception for the
  `levelmoment.com` domain if the user's project has ATS restrictions.
- Android builds: add `<uses-permission android:name="android.permission.INTERNET" />`
  to `AndroidManifest.xml` if not already present.
- WebGL builds: tokens flow via the URL query string; the SDK reads
  `Application.absoluteURL`.

Flag any of these that aren't already configured.
