# Port — Unity (Unity Ads SDK / AdMob Unity → LevelMoment)

You are inside `/levelmoment port`, platform = unity.

## What the LevelMoment Unity SDK looks like

```csharp
using LevelMoment;

public class AdsManager : MonoBehaviour,
    ILevelMomentLoadListener,
    ILevelMomentShowListener
{
    private const string PlacementId = "{{PLACEMENT_ID}}";
    private Question _currentQuestion;

    void Awake()
    {
        LevelMomentAds.Initialize(new LevelMomentConfig
        {
            ApiUrl       = "https://api.levelmoment.com",
            PlacementId  = PlacementId,
            StudentToken = GetTokenFromUrl(),
        });
        LevelMomentAds.Load(PlacementId, this);
    }

    void ShowAd()
    {
        if (!LevelMomentAds.IsReady(PlacementId)) return;
        _currentQuestion = LevelMomentAds.Show(PlacementId, this);
        if (_currentQuestion != null) RenderQuestion(_currentQuestion);
    }

    public void OnAnswer(int selectedIndex)
    {
        bool correct = selectedIndex == _currentQuestion.meta.correctIndex;
        LevelMomentAds.NotifyAnswer(PlacementId, selectedIndex, correct, this);
    }

    public void OnLevelMomentShowComplete(string id, LevelMomentShowCompletionState s)
    {
        HideQuestion();
        if (s == LevelMomentShowCompletionState.Completed) GrantReward();
        Time.timeScale = 1f;
        LevelMomentAds.Load(PlacementId, this);
    }
    // ... other interface methods stubbed
}
```

Mapping:

| Unity Ads SDK                                     | LevelMoment                                                       |
| ------------------------------------------------- | ----------------------------------------------------------------- |
| `UnityAds.Initialize(gameId, testMode, listener)` | `LevelMomentAds.Initialize(LevelMomentConfig)`                    |
| `UnityAds.Load(adUnitId, listener)`               | `LevelMomentAds.Load(placementId, listener)`                      |
| `UnityAds.Show(adUnitId, listener)`               | `LevelMomentAds.Show(placementId, listener)` → returns `Question` |
| `IUnityAdsLoadListener`                           | `ILevelMomentLoadListener`                                        |
| `IUnityAdsShowListener`                           | `ILevelMomentShowListener`                                        |
| `UnityAdsShowCompletionState.COMPLETED`           | `LevelMomentShowCompletionState.Completed`                        |
| `UnityAdsShowCompletionState.SKIPPED`             | `LevelMomentShowCompletionState.Skipped`                          |
| `OnUnityAdsShowClick`                             | (removed — no equivalent)                                         |
| `IUnityAdsInitializationListener`                 | (removed — synchronous init)                                      |

For **AdMob Unity plugin** (`GoogleMobileAds.Api`):

| AdMob Unity                              | LevelMoment                                                                     |
| ---------------------------------------- | ------------------------------------------------------------------------------- |
| `MobileAds.Initialize(status => ...)`    | `LevelMomentAds.Initialize(LevelMomentConfig)`                                  |
| `RewardedAd.Load(adUnitId, request, cb)` | `LevelMomentAds.Load(placementId, this)`                                        |
| `rewardedAd.Show(reward => ...)`         | `LevelMomentAds.Show(placementId, this)` + handle in `ILevelMomentShowListener` |
| `OnAdFullScreenContentClosed += ...`     | `OnLevelMomentShowComplete(_, _)`                                               |

## Step 1 — update `Packages/manifest.json`

```diff
 {
   "dependencies": {
-    "com.unity.ads": "4.x.x",
-    "com.google.ads.mobile": "8.x.x",
+    "com.levelmoment.sdk": "https://github.com/levelmomentorg/sdk.git?path=sdk/unity#v0.1.0",
     ...
   }
 }
```

Choose whichever of `com.unity.ads` / `com.google.ads.mobile` was
present. If both exist, ask the user — they may be using AdMob mediation
with Unity Ads as an adapter, which is more complex.

Note: Unity's package format for git URLs uses `?path=...#<ref>`. The
user may need to refresh the Package Manager (`Window → Package
Manager → Refresh`).

## Step 2 — replace `using` directives

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

## Step 3 — interface declarations

```diff
 public class AdsManager : MonoBehaviour,
-    IUnityAdsInitializationListener,
-    IUnityAdsLoadListener,
-    IUnityAdsShowListener
+    ILevelMomentLoadListener,
+    ILevelMomentShowListener
```

Remove `IUnityAdsInitializationListener` entirely — LevelMoment init is
synchronous, no listener needed.

## Step 4 — replace `Initialize` and `Load`

```diff
 void Awake()
 {
-    UnityAds.Initialize("1234567", false, this);
+    LevelMomentAds.Initialize(new LevelMomentConfig
+    {
+        ApiUrl       = "https://api.levelmoment.com",
+        PlacementId  = "{{PLACEMENT_ID}}",
+        StudentToken = GetTokenFromUrl(),
+    });
+    LevelMomentAds.Load("{{PLACEMENT_ID}}", this);
 }
```

Substitute `{{PLACEMENT_ID}}`.

If the user had a `OnInitializationComplete()` method that loaded the ad
after init, fold that load call into `Awake()` and remove the method.

Add the `GetTokenFromUrl()` helper from
`.claude/skills/levelmoment/templates/unity-get-token.cs.tmpl` if it doesn't
already exist in the class.

## Step 5 — replace `Show`

This is the largest semantic change. Unity Ads returns nothing; LevelMoment
returns a `Question` for the user's UI to render.

```diff
 void ShowAd()
 {
-    if (UnityAds.IsReady("Rewarded_Android"))
-        UnityAds.Show("Rewarded_Android", this);
+    if (!LevelMomentAds.IsReady("{{PLACEMENT_ID}}")) return;
+    _currentQuestion = LevelMomentAds.Show("{{PLACEMENT_ID}}", this);
+    if (_currentQuestion != null) RenderQuestion(_currentQuestion);
 }
```

Add a `private Question _currentQuestion;` field to the class.

Add a stub `RenderQuestion(Question q)` method with a
`// LEVELMOMENT_TODO: render the question UI` comment if the user doesn't
already have UI for displaying ad questions.

## Step 6 — replace listener interface methods

Rename:

```diff
-public void OnUnityAdsAdLoaded(string adUnitId) { ... }
-public void OnUnityAdsFailedToLoad(string adUnitId, UnityAdsLoadError error, string message) { ... }
+public void OnLevelMomentAdLoaded(string placementId) { ... }
+public void OnLevelMomentAdFailedToLoad(string placementId, LevelMomentLoadError error, string message) { ... }

-public void OnUnityAdsShowStart(string adUnitId) { ... }
-public void OnUnityAdsShowComplete(string adUnitId, UnityAdsShowCompletionState state) { ... }
-public void OnUnityAdsShowFailure(string adUnitId, UnityAdsShowError error, string message) { ... }
-public void OnUnityAdsShowClick(string adUnitId) { ... }
+public void OnLevelMomentShowStart(string placementId) { ... }
+public void OnLevelMomentShowComplete(string placementId, LevelMomentShowCompletionState state) { ... }
+public void OnLevelMomentShowFailure(string placementId, LevelMomentShowError error, string message) { ... }
```

Remove `OnUnityAdsShowClick` and `OnInitializationComplete` entirely.

In `OnLevelMomentShowComplete`, replace the enum comparison:

```diff
-if (state == UnityAdsShowCompletionState.COMPLETED) GrantReward();
+if (state == LevelMomentShowCompletionState.Completed) GrantReward();
```

Camel-case, not SCREAMING_CASE.

Preserve `Time.timeScale = 0f/1f` calls and the user's `GrantReward()` /
`ResumeGame()` calls.

## Step 7 — add answer notification

A new call site has no AdMob/Unity equivalent: when the player answers
the question, the user must call `LevelMomentAds.NotifyAnswer(...)`.

Add a stub method:

```csharp
public void OnAnswer(int selectedIndex)
{
    bool correct = selectedIndex == _currentQuestion.meta.correctIndex;
    LevelMomentAds.NotifyAnswer("{{PLACEMENT_ID}}", selectedIndex, correct, this);
    // LEVELMOMENT_TODO: wire this to your question UI button handlers
}
```

## Step 8 — flag for review

- **AdMob mediation adapters** in `Assets/Plugins/Android/` or via UPM —
  not supported by LevelMoment. Flag.
- **GDPR consent / IDFA tracking permission** — `RequestTrackingAuthorization`
  on iOS, `UMP` integration. Flag — LevelMoment handles consent on the parent
  side.
- **`Application.RequestUserAuthorization`** for ad tracking. Flag.
- **Custom mediation networks** (AppLovin, IronSource, Mintegral). Flag.
- **Banner / interstitial / app-open ads** — not supported. Flag.

## Step 9 — Unity-specific platform tips

- Unity 2021.3 LTS or newer recommended (the WebView plugin requires it).
- iOS builds: enable "App Transport Security" exception for the
  `levelmoment.com` domain if the user's project has ATS restrictions.
- Android builds: add `<uses-permission android:name="android.permission.INTERNET" />`
  to `AndroidManifest.xml` if not already present.
- WebGL builds: tokens flow via the URL query string; the SDK reads
  `Application.absoluteURL`.

Flag any of these that aren't already configured.
