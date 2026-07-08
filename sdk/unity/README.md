# LevelMoment Unity SDK (`com.levelmoment.sdk`)

**Status: 🟡 Partial — runtime + EditMode tests landed; PlayMode integration tests pending**

Unity Package Manager (UPM) package that integrates LevelMoment into Unity games (iOS, Android, WebGL, PC). Drop-in replacement for the Unity Ads SDK and Google AdMob Unity Plugin.

---

## What's Done

- Unity package manifest (`package.json`, UPM format)
- `Runtime/LevelMomentAds.cs` — load/show/notify static API mirroring `UnityAds`
- `Runtime/Models.cs` — `LevelMomentConfig`, `Question`, listener interfaces
- `Runtime/ImpressionQueue.cs` — `PlayerPrefs`-backed buffered flush
- `Runtime/UrlSafety.cs` — refuses credential-shaped query keys so the student token never leaks into a URL
- `Samples~/BasicIntegration/` — minimal example scene script
- `Tests/EditMode/` — NUnit EditMode suite for `UrlSafety` + `ImpressionQueue` (run from **Window → General → Test Runner** in Unity)

## What's Not Done

- [ ] PlayMode integration tests against a mock `UnityWebRequest` (Load → Show → NotifyAnswer happy path)
- [ ] Unity Editor setup wizard (API URL + placement ID configuration)
- [ ] CI: headless `Unity -runTests -testPlatform EditMode` step in GitHub Actions

## Running tests locally

Open the project root (or any Unity project that includes this package via `Packages/manifest.json`) in **Unity 2022.1+**, then:

1. `Window → General → Test Runner`
2. Select the **EditMode** tab
3. Click **Run All**

The EditMode suite needs no network or scene; it covers pure C# logic only.

---

## Planned API (C#)

```csharp
// Initialize once (GameManager.Start or similar)
LevelMoment.Initialize("your-game-id", studentToken, testMode: false);

// Preload while game runs — mirrors UnityAds.Load()
LevelMoment.Load("interstitial", new LevelMomentLoadOptions {
    onLoaded = placementId => Debug.Log("Ready"),
    onFailedToLoad = (id, err) => Debug.LogWarning(err),
});

// At natural pause point — mirrors UnityAds.Show()
LevelMoment.Show("interstitial", new LevelMomentShowOptions {
    onComplete = (id, result) => {
        if (result == LevelMomentShowResult.Answered) GrantReward();
        ResumeGame();
    },
});
```

---

## When to Build

Build the Unity SDK after:

1. The REST API contract is stable (all routes implemented and tested)
2. There is confirmed demand from Unity game developers

The REST API is the real contract — any Unity version of the SDK is just a C# HTTP client wrapper over the same endpoints used by `sdk/web` and `sdk/flutter`.

---

## Related

- REST API: [`platform/api/`](../../platform/api/README.md)
- Flutter SDK (reference for the load/show pattern): [`sdk/flutter/`](../flutter/README.md)
