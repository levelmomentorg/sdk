# Migrating from Unity Ads SDK to LevelMoment

LevelMoment mirrors the Unity Ads rewarded-ad API. Most of the swap is renaming
prefixes — the structure stays identical.

## 1. Add the package

In `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.levelmoment.sdk": "https://github.com/levelmoment/sdk-unity.git"
  }
}
```

Remove `com.unity.ads` from the same file.

## 2. Replace the namespace import

```csharp
// REMOVE:
using UnityEngine.Advertisements;

// ADD:
using LevelMoment;
```

## 3. Replace Initialize()

```csharp
// BEFORE:
UnityAds.Initialize("1234567", false, this);   // implements IUnityAdsInitializationListener

// AFTER (no initialization listener needed):
LevelMomentAds.Initialize(new LevelMomentConfig
{
    ApiUrl       = "https://api.levelmoment.com",
    PlacementId  = "your-game-id",             // from LevelMoment developer portal
    StudentToken = GetTokenFromUrl(),           // new — one extra line (see below)
});
```

## 4. Replace Load()

```csharp
// BEFORE:
UnityAds.Load("Rewarded_Android", this);   // implements IUnityAdsLoadListener

// AFTER (identical structure — swap class/interface names):
LevelMomentAds.Load("your-game-id", this);     // implements ILevelMomentLoadListener
```

## 5. Replace Show()

```csharp
// BEFORE:
UnityAds.Show("Rewarded_Android", this);   // implements IUnityAdsShowListener

// AFTER — Show() returns the Question to render; call NotifyAnswer() when done:
var question = LevelMomentAds.Show("your-game-id", this);   // implements ILevelMomentShowListener
if (question != null)
{
    RenderQuestion(question);              // your UI — SDK owns no UI
}
```

## 6. Replace the load listener interface

```csharp
// BEFORE:
public void OnUnityAdsAdLoaded(string adUnitId) { ... }
public void OnUnityAdsFailedToLoad(string adUnitId, UnityAdsLoadError error, string message) { ... }

// AFTER (rename only):
public void OnLevelMomentAdLoaded(string placementId) { ... }
public void OnLevelMomentAdFailedToLoad(string placementId, LevelMomentLoadError error, string message) { ... }
```

## 7. Replace the show listener interface

```csharp
// BEFORE:
public void OnUnityAdsShowStart(string adUnitId) { ... }
public void OnUnityAdsShowComplete(string adUnitId, UnityAdsShowCompletionState state)
{
    if (state == UnityAdsShowCompletionState.COMPLETED) { /* reward */ }
    ResumeGame();
}
public void OnUnityAdsShowFailure(string adUnitId, UnityAdsShowError error, string message) { ... }
public void OnUnityAdsShowClick(string adUnitId) { }   // not in LevelMoment — remove

// AFTER (rename + remove OnUnityAdsShowClick):
public void OnLevelMomentShowStart(string placementId) { ... }
public void OnLevelMomentShowComplete(string placementId, LevelMomentShowCompletionState state)
{
    if (state == LevelMomentShowCompletionState.Completed) { /* reward */ }
    ResumeGame();                          // always resume here
}
public void OnLevelMomentShowFailure(string placementId, LevelMomentShowError error, string message) { ... }
```

## 8. Add answer submission (new — replaces watching a video)

```csharp
// When the player selects an answer in your question UI:
var isCorrect = selectedIndex == question.meta.correctIndex;
LevelMomentAds.NotifyAnswer(placementId, selectedIndex, isCorrect, this);
// → fires OnLevelMomentShowComplete automatically
```

## API mapping

| Unity Ads SDK                                     | LevelMoment SDK                              | Notes                                 |
| ------------------------------------------------- | -------------------------------------------- | ------------------------------------- |
| `UnityAds.Initialize(gameId, testMode, listener)` | `LevelMomentAds.Initialize(config)`          | config includes ApiUrl + StudentToken |
| `UnityAds.Load(adUnitId, listener)`               | `LevelMomentAds.Load(placementId, listener)` | identical shape                       |
| `UnityAds.Show(adUnitId, listener)`               | `LevelMomentAds.Show(placementId, listener)` | returns `Question` to render          |
| `UnityAds.IsReady(adUnitId)`                      | `LevelMomentAds.IsReady(placementId)`        | identical                             |
| `IUnityAdsLoadListener`                           | `ILevelMomentLoadListener`                   | identical shape                       |
| `IUnityAdsShowListener`                           | `ILevelMomentShowListener`                   | same; remove `OnUnityAdsShowClick`    |
| `UnityAdsShowCompletionState.COMPLETED`           | `LevelMomentShowCompletionState.Completed`   | camelCase                             |
| `UnityAdsShowCompletionState.SKIPPED`             | `LevelMomentShowCompletionState.Skipped`     | camelCase                             |
| `IUnityAdsInitializationListener`                 | _(not needed)_                               | remove the interface                  |
| _(video watched)_                                 | `LevelMomentAds.NotifyAnswer(...)`           | new — player answers a question       |

## Getting the student token

The parent portal embeds a short-lived session token in the game's launch URL.
Read it at startup:

```csharp
// WebGL
private string GetTokenFromUrl()
{
    var uri = new Uri(Application.absoluteURL);
    var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
    return query["token"] ?? string.Empty;
}

// Android deep link
private async Task<string> GetTokenFromUrl()
{
    var url = await Task.Run(() =>
    {
        // Use Unity's AndroidJavaClass to read the launch Intent data URI
        using var activity = new AndroidJavaClass("com.unity3d.player.UnityPlayer")
            .GetStatic<AndroidJavaObject>("currentActivity");
        return activity.Call<AndroidJavaObject>("getIntent")
                       .Call<AndroidJavaObject>("getData")
                      ?.Call<string>("toString") ?? string.Empty;
    });
    var uri = new Uri(url);
    var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
    return query["token"] ?? string.Empty;
}
```

## Full before/after example

```csharp
// ---- BEFORE (Unity Ads) ----
using UnityEngine.Advertisements;

public class AdsManager : MonoBehaviour,
    IUnityAdsInitializationListener,
    IUnityAdsLoadListener,
    IUnityAdsShowListener
{
    void Awake()
    {
        UnityAds.Initialize("1234567", false, this);
    }

    public void OnInitializationComplete()
    {
        UnityAds.Load("Rewarded_Android", this);
    }

    void ShowAd()
    {
        if (UnityAds.IsReady("Rewarded_Android"))
            UnityAds.Show("Rewarded_Android", this);
    }

    public void OnUnityAdsAdLoaded(string id) { }
    public void OnUnityAdsFailedToLoad(string id, UnityAdsLoadError e, string msg) { }
    public void OnUnityAdsShowStart(string id) { Time.timeScale = 0f; }
    public void OnUnityAdsShowComplete(string id, UnityAdsShowCompletionState s)
    {
        if (s == UnityAdsShowCompletionState.COMPLETED) GrantReward();
        Time.timeScale = 1f;
        UnityAds.Load("Rewarded_Android", this); // preload next
    }
    public void OnUnityAdsShowFailure(string id, UnityAdsShowError e, string msg) { Time.timeScale = 1f; }
    public void OnUnityAdsShowClick(string id) { }
    public void OnInitializationFailed(UnityAdsInitializationError e, string msg) { }
}


// ---- AFTER (LevelMoment) ----
using LevelMoment;

public class AdsManager : MonoBehaviour,
    ILevelMomentLoadListener,
    ILevelMomentShowListener
{
    private const string PlacementId = "your-game-id";
    private Question _currentQuestion;

    void Awake()
    {
        LevelMomentAds.Initialize(new LevelMomentConfig
        {
            ApiUrl      = "https://api.levelmoment.com",
            PlacementId = PlacementId,
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

    // Player answered — call from your question UI button handler:
    public void OnAnswer(int selectedIndex)
    {
        bool correct = selectedIndex == _currentQuestion.meta.correctIndex;
        LevelMomentAds.NotifyAnswer(PlacementId, selectedIndex, correct, this);
    }

    public void OnLevelMomentAdLoaded(string id) { }
    public void OnLevelMomentAdFailedToLoad(string id, LevelMomentLoadError e, string msg) { }
    public void OnLevelMomentShowStart(string id) { Time.timeScale = 0f; }
    public void OnLevelMomentShowComplete(string id, LevelMomentShowCompletionState s)
    {
        HideQuestion();
        if (s == LevelMomentShowCompletionState.Completed) GrantReward();
        Time.timeScale = 1f;
        LevelMomentAds.Load(PlacementId, this); // preload next
    }
    public void OnLevelMomentShowFailure(string id, LevelMomentShowError e, string msg) { Time.timeScale = 1f; }
}
```
