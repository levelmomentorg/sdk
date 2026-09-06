# LevelMoment Unity SDK (`com.levelmoment.sdk`)

**Preview:** Validate the WebView provider and hosted break on each target
device before release. The repository version is `0.2.0`; use the immutable
preview artifact or reference supplied for your partner integration.

Use `AGENT-INSTRUCTIONS.md` in the complete partner packet supplied with this
preview. Give the agent that packet, the immutable artifact or source reference,
the placement ID, target slot, and reward action.

Unity Package Manager (UPM) package that integrates LevelMoment into Unity games (iOS, Android, and other WebView-capable platforms). Drop-in replacement for Google AdMob / Unity Ads **rewarded ads**: instead of a video, it shows a Level Moment with a mini-lesson or quiz.

`Show()` opens the hosted `/break` page in a fullscreen WebView and reports
answers and completion through C# callbacks. `Load()` prepares the shell; it
does not preload question content.

**Note:** Credentials travel through the bridge and never in the hosted URL.
Unity sends `custody: false`; use `UnsafeTesting` for sandbox credentials.

**Parent approval opens in the device's browser.** When a parent chooses to approve the game in a browser rather than scan the pairing code, the SDK opens the approval link outside the WebView. Parent sign-in stays outside the game surface. Never collect a Level Moment email code or other parent credential inside it. After approval, return to the game; the break resumes polling and completes the connection automatically. Only links on the loaded Level Moment origin are passed to the OS. Break and gate URLs announce browser support with `caps=openExternal`.

---

## Requirements

Unity has **no built-in WebView**, so the SDK needs a WebView provider. The bundled provider targets the open-source [`gree/unity-webview`](https://github.com/gree/unity-webview) plugin.

### 1. Install this package

In Unity Package Manager, add the immutable `0.2.0` preview artifact or
reference supplied for your partner integration. Do not point a partner build
at a moving branch.

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

This compiles the bundled gree adapter, which registers as the WebView provider
at startup. Without the define or a registered provider, `Show()` reports a
failure through `OnAdFailedToShow`.

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
using System;
using LevelMoment;

// 1. Initialize once (bootstrap scene)
LevelMomentAds.Initialize(new LevelMomentConfig
{
    // Production uses https://levelmoment.com/api and /break by default.
    // Mock = true,  // bundled demo questions, no API/token
});

// 2. Opt in to the sign-in gate when enabling learning breaks at startup.
LevelMomentAds.EnsureSignedIn("YOUR_PLACEMENT_ID", result =>
{
    switch (result)
    {
        case EnsureSignedInResult.Ready:
            StartGame();
            break;
        case EnsureSignedInResult.Canceled:
            ShowBreaksAreOffScreen();   // a person said no — offer a manual retry
            break;
        case EnsureSignedInResult.TechnicalFailure:
            ShowTryAgainLater();
            break;
    }
});

// 3. Create a fresh handle every time this existing rewarded slot opens.
PauseGame();
var granted = false;
var finished = false;
Action finish = () =>
{
    if (finished) return;
    finished = true;
    ResumeGame();
    PrepareNextBreak();
};
RewardedAd.Load("YOUR_PLACEMENT_ID", new RewardedAdLoadCallbacks
{
    OnAdLoaded = ad => ad.Show(new RewardedAdShowCallbacks
    {
        OnUserEarnedReward = amount =>
        {
            if (!finished && amount == 1 && !granted)
            {
                granted = true;
                GrantBonus();
            }
        },
        OnAdDismissed = finish,
        OnAdFailedToShow = _ => finish(),
    }),
    OnAdFailedToLoad = _ => finish(),
});
```

No token appears anywhere above — a paired device's credential lives in the
hosted page's storage. Use `UnsafeTesting` with an `eply_sbx_` token for sandbox
work. The deprecated `studentToken` overload is refused in production.

A game that passes an explicit token needs a WebView provider implementing `ILevelMomentScriptableWebView` for the token to reach the page — see API below. The bundled gree adapter implements it.

---

## API

| Member                                                                      | Purpose                                                                                                                                                                                                                                                                      |
| --------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `LevelMomentAds.Initialize(LevelMomentConfig)`                              | One-time setup. Production uses canonical endpoints. Set `Mock` for the offline preview and `UnsafeTesting` for sandbox URLs and credentials.                                                                                                                                |
| `LevelMomentAds.IsInitialized`                                              | True once initialized.                                                                                                                                                                                                                                                       |
| `LevelMomentAds.EnsureSignedIn(placementId, callback, studentToken?)`       | Optional identity gate. Opens the hosted `?mode=gate` surface and reports `EnsureSignedInResult.Ready` / `Canceled` / `TechnicalFailure` exactly once. Enable learning breaks on `Ready`; normal gameplay can continue after the other outcomes.                             |
| `LevelMomentAds.IsSignedIn(placementId, onResult, onError, studentToken?)`  | Authoritative credential check through a hidden `?mode=check` surface. Calls `onError`, not `onResult(false)`, when it cannot get an answer.                                                                                                                                 |
| `LevelMomentAds.EnsureAccess(placementId, callback, studentToken?)`         | Optional access gate on `/access?mode=gate`; reports the same `EnsureSignedInResult` values and callback semantics as `EnsureSignedIn`.                                                                                                                                      |
| `LevelMomentAds.CheckAccess(placementId, onResult, onError, studentToken?)` | Optional access check on `/access?mode=check`; `onResult(false)` means the access flow is needed, while technical failures call `onError`.                                                                                                                                   |
| `RewardedAd.Load(placementId, callbacks, format?)`                          | Synchronous mark-ready (no network). `callbacks`: `OnAdLoaded`, `OnAdFailedToLoad`.                                                                                                                                                                                          |
| `RewardedAd.Load(placementId, studentToken, callbacks, format?)`            | Deprecated compatibility overload. The token is accepted only with an `eply_sbx_` credential in `UnsafeTesting`.                                                                                                                                                             |
| `ad.Show(callbacks)`                                                        | Opens the hosted break in a WebView. `callbacks`: `OnUserEarnedReward(int amount)`, `OnAdDismissed`, `OnAdFailedToShow(error)`, `OnAdShowedFullScreenContent`.                                                                                                               |
| `callbacks.OnUserEarnedRewardItem`                                          | Optional answer callback with `Amount` and the hosted page's opaque `RewardId`.                                                                                                                                                                                              |
| `ad.IsLoaded`                                                               | True when loaded and not yet shown/destroyed.                                                                                                                                                                                                                                |
| `ad.Destroy()`                                                              | Release the ad + tear down the WebView (no callbacks fired).                                                                                                                                                                                                                 |
| `ILevelMomentWebView` / `LevelMomentWebViewRegistry`                        | WebView provider seam for custom plugins.                                                                                                                                                                                                                                    |
| `ILevelMomentHeadlessWebView`                                               | Optional provider extension: `OpenHidden(url)` keeps the credential check off screen. The bundled gree adapter implements it; providers that do not still work, with the check briefly visible.                                                                              |
| `ILevelMomentScriptableWebView`                                             | Optional provider extension: `EvaluateJS(js)` delivers a credential to the hosted page when the page asks for one. Required only if you pass an explicit `studentToken` — a paired device with no explicit token needs nothing here. The bundled gree adapter implements it. |

Every `studentToken?` parameter is deprecated compatibility surface. It accepts
only an `eply_sbx_` credential when `UnsafeTesting` is configured; production
credentials stay in the hosted bridge and are never supplied in game config.

**Callback semantics** (identical to the other LevelMoment SDKs):

- `OnUserEarnedReward(amount)` — fires once per graded answer: `amount == 1` for a correct answer, `0` otherwise. Grant the chosen bonus on the first correct callback only.
- `OnUserEarnedRewardItem(item)` — optional equivalent answer callback with the opaque impression `RewardId`, when the hosted page supplies one.
- `OnAdDismissed` — fires **exactly once** when the break ends. Always resume the game here.
- `OnAdFailedToShow(error)` — fires instead of `OnAdDismissed` when the break can't be shown (not loaded, no WebView provider, page error).
- `format` — `flashcard` (default), `quiz`, or `deep_dive`.

A **15-second pre-`ready` watchdog** guarantees a crashed or unreachable page
can't cover the game forever — it resolves as a clean `OnAdDismissed`. After
`ready` there is no timeout (a student thinking through a quiz is never
force-closed).

`Show()` catches WebView-provider startup failures. If the provider factory or `Open()` throws, the SDK reports `webview_error` through `OnAdFailedToShow` and tears down the ad. Exceptions thrown by your own callbacks propagate to you unchanged.

**Startup gate semantics** (shared with the other LevelMoment SDKs and defined
by the Unity SDK's `EnsureSignedInResult`):

- `Ready` — enable learning breaks. Normal gameplay can start independently.
- `Canceled` — a person closed the gate, or a parent denied the connection. Show your own "learning breaks are off" state or a retry action; do not retry automatically.
- `TechnicalFailure` — the gate could not run (no `Initialize()`, a bad config string, no WebView provider, or the surface never came up). Retry later.
- `IsSignedIn` is authoritative, not a cached flag — it re-validates the credential against the server every call, because a credential can be revoked between launches. An error means _unknown_, never _signed out_: treat it as "try again", and leave the current state alone.
- Both methods allow the hosted page 15 seconds to load. `EnsureSignedIn` then waits without a deadline for approval, so pairing takes as long as a parent takes. `IsSignedIn` has no approval step and is bounded end to end: it calls `onError` if the page does not load in 15 seconds, if the page's credential check runs longer than 10 seconds (the page reports `check_timeout`), or if 30 seconds pass with no answer at all.
- `EnsureAccess` and `CheckAccess` use the same outcome and deadline rules on `/access`; use them when access enablement is a separate publisher flow from identity sign-in.

---

## Preview verification

Automated C# checks do not replace validation in a real Unity editor, an
il2cpp build, and each target device. Before release:

1. **Open the package in Unity 2021.3 LTS** (or newer). Add both the SDK and
   `net.gree.unity-webview` per the Requirements section, plus the
   `LEVELMOMENT_GREE_WEBVIEW` define.
   - Confirm the console is clean and that the bundled gree adapter compiles.
   - Confirm the package also compiles without the optional bundled provider when
     the scripting define is absent.
   - Run the EditMode suites in the editor so bridge parsing meets the real
     `JsonUtility`.
2. **On-device smoke** (real WebView): build to iOS/Android with the canonical
   hosted service, trigger a break, and confirm the hosted page loads, an answer
   fires `OnUserEarnedReward`, and closing the break fires `OnAdDismissed`
   exactly once. Try `Mock = true` for an offline pass.

---

## Related

- [Integration documentation](https://levelmoment.com/docs/porting)
- React Native SDK (reference WebView shell): [`sdk/react-native/`](../react-native/README.md)
- Migrating from AdMob / Unity Ads: [MIGRATION.md](./MIGRATION.md)

`Samples~/PortalExample.cs` contains the portal setup example and is included in the sample compilation checks.
