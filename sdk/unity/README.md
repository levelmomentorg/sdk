# LevelMoment Unity SDK (`com.levelmoment.sdk`)

**Status: 🟢 Verified end to end on the iOS Simulator — real question shown in the WebView, answered, reward bridged. Physical-device smoke pending.**

Unity Package Manager (UPM) package that integrates LevelMoment into Unity games (iOS, Android, and other WebView-capable platforms). Drop-in replacement for Google AdMob / Unity Ads **rewarded ads**: instead of a video, it shows a short educational break.

Per [ADR-001](../../docs/ADR-001-webview-rendering.md) this SDK is a **thin WebView shell**. It renders nothing itself — `Show()` opens the hosted `/break` page (`platform/web/app/break`) in a fullscreen WebView and bridges the page's events (`ready` / `earnedReward` / `dismissed` / `error`) to C# callbacks. The page owns all question rendering, answer submission, and impression queueing.

---

## Requirements

Unity has **no built-in WebView**, so the SDK needs a WebView provider. The bundled provider targets the open-source [`gree/unity-webview`](https://github.com/gree/unity-webview) plugin.

### 1. Install this package

Add to `Packages/manifest.json`:

```json
"com.levelmoment.sdk": "https://github.com/levelmomentorg/sdk.git?path=sdk/unity"
```

### 2. Install gree/unity-webview and the Input System package

gree's assembly definition references `Unity.InputSystem`. Without the
package, the `unity-webview` assembly does not compile and the SDK reports no
WebView provider. Add both to `Packages/manifest.json`:

```json
"net.gree.unity-webview": "https://github.com/gree/unity-webview.git?path=/dist/package-nofragment",
"com.unity.inputsystem": "1.11.2"
```

**Note:** The gree UPM packages namespace the plugin as
`Gree.UnityWebView.WebViewObject`. The SDK supports both this and the classic
global-namespace install; a `versionDefine` sets `LEVELMOMENT_GREE_UPM`
whenever the UPM package is present.

### 3. Add the scripting define

**Project Settings → Player → Other Settings → Scripting Define Symbols**, add:

```
LEVELMOMENT_GREE_WEBVIEW
```

This compiles the gree adapter (`Runtime/Gree/GreeWebViewAdapter.cs`), which self-registers as the WebView provider at startup. Without the define — or without a registered provider — `Show()` fails fast via `OnAdFailedToShow` with an actionable message; it never renders a broken screen.

> Using a different WebView plugin (Vuplex, 3D WebView)? Implement `ILevelMomentWebView` and call `LevelMomentWebViewRegistry.Register(() => new YourAdapter())` at startup instead of steps 2–3.

### 4. Create `Assets/link.xml`

```xml
<linker>
  <assembly fullname="LevelMomentSDK.Gree" preserve="all" />
  <assembly fullname="LevelMomentSDK.Runtime" preserve="all" />
  <assembly fullname="unity-webview" preserve="all" />
</linker>
```

**Warning:** Skipping this step fails silently. The gree adapter registers
itself through `[RuntimeInitializeOnLoadMethod]` and nothing references it
statically, so UnityLinker strips it from il2cpp builds. The project
compiles, editor play mode works, and only the built player reports no
WebView provider. The package ships its own `link.xml`, but Unity does not
honor it for git- or file-referenced packages.

### 5. Allow plain-HTTP for local development

iOS App Transport Security blocks the WebView from loading a local `/break`
page over HTTP. For development builds, add
`NSAppTransportSecurity > NSAllowsArbitraryLoads` to the exported Xcode
project's Info.plist. Production uses HTTPS and needs no exception.

---

## Quickstart

```csharp
using LevelMoment;

// 1. Initialize once (bootstrap scene)
LevelMomentAds.Initialize(new LevelMomentConfig
{
    ApiUrl   = "https://api.levelmoment.com",
    BreakUrl = "https://app.levelmoment.com/break",
    // Mock = true,  // bundled demo questions, no API/token
});

// 2. Preload in the background — synchronous, no network
RewardedAd ad = null;
RewardedAd.Load("your-placement-id", new RewardedAdLoadCallbacks
{
    OnAdLoaded       = a   => ad = a,
    OnAdFailedToLoad = err => Debug.LogWarning(err),
});

// 3. Show at a natural pause point — opens the hosted break in a WebView
ad.Show(new RewardedAdShowCallbacks
{
    OnUserEarnedReward = amount => { if (amount == 1) GrantBonus(); },
    OnAdDismissed      = ()     => ResumeGame(),   // always resume here
    OnAdFailedToShow   = err    => ResumeGame(),
});
```

The student session token defaults to the `?token=` param of the game's launch URL (`Application.absoluteURL` / deep link — the parent portal embeds it there). Pass one explicitly with `RewardedAd.Load(placementId, studentToken, callbacks)` if you read it yourself.

---

## API

| Member                                                           | Purpose                                                                                                                                                        |
| ---------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `LevelMomentAds.Initialize(LevelMomentConfig)`                   | One-time setup. Config: `ApiUrl`, `BreakUrl`, `Mock`, `CustomData` (SSV-parity, stamped onto every impression). Mirrors `MobileAds.Initialize()`.              |
| `LevelMomentAds.IsInitialized`                                   | True once initialized.                                                                                                                                         |
| `RewardedAd.Load(placementId, callbacks, format?)`               | Synchronous mark-ready (no network). `callbacks`: `OnAdLoaded`, `OnAdFailedToLoad`.                                                                            |
| `RewardedAd.Load(placementId, studentToken, callbacks, format?)` | As above, with an explicit token.                                                                                                                              |
| `ad.Show(callbacks)`                                             | Opens the hosted break in a WebView. `callbacks`: `OnUserEarnedReward(int amount)`, `OnAdDismissed`, `OnAdFailedToShow(error)`, `OnAdShowedFullScreenContent`. |
| `ad.IsLoaded`                                                    | True when loaded and not yet shown/destroyed.                                                                                                                  |
| `ad.Destroy()`                                                   | Release the ad + tear down the WebView (no callbacks fired).                                                                                                   |
| `ILevelMomentWebView` / `LevelMomentWebViewRegistry`             | WebView provider seam for custom plugins.                                                                                                                      |

**Callback semantics** (identical to the other LevelMoment SDKs):

- `OnUserEarnedReward(amount)` — fires once per graded answer: `amount == 1` for a correct answer, `0` otherwise. A quiz or deep dive fires one event per answered question (including when the break is closed early); accumulate and apply the reward in `OnAdDismissed`.
- `OnAdDismissed` — fires **exactly once** when the break ends (any outcome). Always resume the game here.
- `OnAdFailedToShow(error)` — fires instead of `OnAdDismissed` when the break can't be shown (not loaded, no WebView provider, page error).
- `format` — `flashcard` (default), `quiz`, or `deep_dive`.

A **15-second pre-`ready` watchdog** (mirroring `sdk/web`) guarantees a crashed or unreachable page can't cover the game forever — it resolves as a clean `OnAdDismissed`. After `ready` there is no timeout (a student thinking through a quiz is never force-closed).

---

## Verification

**This SDK has not been compiled by the Unity toolchain in this change** (no Unity available in the monorepo's JS/TS CI). C# is kept conservative (no C# 9+ features, all `#if` guards paired). Before shipping, verify in Unity:

1. **Open the package in Unity 2021.3 LTS** (or newer). Add both `com.levelmoment.sdk` and `net.gree.unity-webview` per the Requirements section, plus the `LEVELMOMENT_GREE_WEBVIEW` define.
   - Confirm the console is clean and that `LevelMomentSDK.Gree` compiles (it references the gree assembly `unity-webview`; if that name differs in your gree version, update the reference in `Runtime/Gree/LevelMomentSDK.Gree.asmdef`).
   - Without the define, confirm `LevelMomentSDK.Gree` is **excluded** (its `defineConstraints`) and the runtime compiles standalone.
2. **Run the EditMode tests**: `Window → General → Test Runner → EditMode → Run All`. Suites: `BreakUrlTests`, `HostMessageTests`, `LoadWatchdogTests`, `RewardedAdTests`. All are pure C# (no WebView, no network).
   - Headless: `Unity -batchmode -runTests -testPlatform EditMode -projectPath <proj>`.
3. **On-device smoke** (real WebView): build to iOS/Android with a live `BreakUrl`, trigger a break, and confirm the hosted page loads, an answer fires `OnUserEarnedReward`, and closing the break fires `OnAdDismissed` exactly once. Try `Mock = true` for an offline pass.

---

## Key files

| File                                      | Purpose                                                           |
| ----------------------------------------- | ----------------------------------------------------------------- |
| `Runtime/LevelMomentAds.cs`               | `Initialize` + config/clock/token seams.                          |
| `Runtime/RewardedAd.cs`                   | Load/Show, bridge handling, terminal-once guard, watchdog wiring. |
| `Runtime/RewardedAdCallbacks.cs`          | Load/Show callback bundles.                                       |
| `Runtime/LevelMomentConfig.cs`            | `ApiUrl` / `BreakUrl` / `Mock` / `CustomData`.                    |
| `Runtime/BreakUrl.cs`                     | Hosted `/break` URL builder (pure, tested).                       |
| `Runtime/HostMessage.cs`                  | Bridge JSON parser (pure, tested).                                |
| `Runtime/LoadWatchdog.cs`                 | Pre-`ready` timeout state machine (pure, tested).                 |
| `Runtime/ILevelMomentWebView.cs`          | WebView provider interface + registry.                            |
| `Runtime/NoWebViewFallback.cs`            | Fails `Show()` with install guidance when no provider is present. |
| `Runtime/LevelMomentRuntime.cs`           | Hidden MonoBehaviour that ticks the watchdog each frame.          |
| `Runtime/Gree/GreeWebViewAdapter.cs`      | gree/unity-webview adapter (`#if LEVELMOMENT_GREE_WEBVIEW`).      |
| `Runtime/Gree/LevelMomentSDK.Gree.asmdef` | Adapter assembly, gated by the define, references gree.           |
| `Tests/EditMode/`                         | NUnit EditMode suites (pure C#).                                  |
| `Samples~/BasicIntegration/`              | Minimal load → show → resume example.                             |

## Related

- [ADR-001 — WebView rendering](../../docs/ADR-001-webview-rendering.md)
- Flutter SDK (reference WebView shell): [`sdk/flutter/`](../flutter/README.md)
- React Native SDK (reference WebView shell): [`sdk/react-native/`](../react-native/README.md)
- Migrating from AdMob / Unity Ads: [MIGRATION.md](./MIGRATION.md)
