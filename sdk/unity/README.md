# LevelMoment Unity SDK (`com.levelmoment.sdk`)

**Preview:** Validate the WebView provider and hosted break on each target
device before release. The repository version is `0.2.0`; use the immutable
preview artifact or reference supplied for your partner integration.

Use `AGENT-INSTRUCTIONS.md` in the complete partner packet supplied with this
preview. Give the agent that packet, the immutable artifact or source reference,
the placement ID, target slot, and reward action.

Unity Package Manager (UPM) package that integrates LevelMoment into Unity games (iOS, Android, and other WebView-capable platforms). Drop-in replacement for Google AdMob / Unity Ads **rewarded ads**: instead of a video, it shows a Level Moment break — a quick question or a practice set.

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
- `OnAdFailedToShow(error)` — fires instead of `OnAdDismissed` when the break can't be shown (not loaded, no WebView provider, page error, or the ad was already shown — see below).
- `format` — `quick_question` (default), `practice_set`, `mastery_round`, or `intro_lesson`. The first two fill a rewarded slot (one question, then a set); the last two fill an interstitial slot (a longer set, and the same set opening on an instruction panel). Declare the slot and Level Moment picks the format inside it, since which one suits the learner is not something a game can see. A format you pass is a floor: the break is never smaller than the one you sized the slot against.
- One `Show()` per handle: once a `RewardedAd`/`InterstitialAd` has been shown, `IsLoaded` is false and a second `Show()` reports `OnAdFailedToShow` with `not_loaded` rather than reopening it — get a fresh handle from `Load()` for each break.

A **15-second pre-`ready` watchdog** guarantees a crashed or unreachable page
can't cover the game forever — it resolves as a clean `OnAdDismissed`. After
`ready` there is no timeout (a student thinking through a question is never
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

## InterstitialAd

`InterstitialAd` is the same hosted `/break` WebView shell as `RewardedAd` —
same `Load()`/`Show()` lifecycle, same watchdog and dismiss/failure semantics
— for a break slot that grants nothing (the placement AdMob calls a plain
interstitial). `InterstitialAdShowCallbacks` has no reward callbacks; use it
where the game doesn't want to grant a bonus for the break.

```csharp
InterstitialAd.Load("YOUR_PLACEMENT_ID", new InterstitialAdLoadCallbacks
{
    OnAdLoaded = ad => ad.Show(new InterstitialAdShowCallbacks
    {
        OnAdDismissed = ResumeGame,
        OnAdFailedToShow = _ => ResumeGame(),
    }),
    OnAdFailedToLoad = _ => ResumeGame(),
});
```

The shell announces `kind=interstitial` on the `/break` URL it opens
(`RewardedAd` sends no `kind`), so the hosted page can tell which placement it
is serving. **The hosted `/break` page does not yet read this param** — until
it does, the page renders the same experience for both kinds.

---

## AppLovin MAX compatibility facade

`LevelMoment.Compat.Max` (`Runtime/Compat/`) is a MAX-shaped facade over
`InterstitialAd`/`RewardedAd`, for a game migrating off AppLovin MAX. Map each
MAX ad-unit id to a Level Moment placement, then keep the rest of your
integration unchanged:

```csharp
using LevelMoment;
using LevelMoment.Compat.Max;

// LevelMomentAds.Initialize(...) must run before InitializeSdk() —
// InitializeSdk() does not configure endpoints/credentials itself, and a
// Load() before Initialize() fails with "not_initialized".
LevelMomentAds.Initialize(new LevelMomentConfig());

// Declare the slot: the placement, the break format, the target duration in
// seconds, and the reward amount this ad unit already grants the player.
LevelMomentMaxSdk.MapAdUnit(
    "YOUR_MAX_AD_UNIT_ID", "YOUR_PLACEMENT_ID", "practice_set", 30, 50);

// OnSdkInitializedEvent arrives on a later frame, as MAX's does.
LevelMomentMaxSdkCallbacks.OnSdkInitializedEvent += () =>
{
    LevelMomentMaxSdkCallbacks.Interstitial.OnAdLoadedEvent += (adUnitId, adInfo) => { /* unchanged */ };
    LevelMomentMaxSdk.LoadInterstitial("YOUR_MAX_AD_UNIT_ID");
};
LevelMomentMaxSdk.InitializeSdk();
```

It has **zero dependency on the AppLovin plugin** — `AdInfo`, `ErrorInfo`, and
`Reward` in `MaxCompatTypes.cs` are Level Moment's own types, shape-compatible
with MAX's (same member names) but not the same types, so a callback body
that reads a member MAX has and these stand-ins don't will not compile. See
that file's header for the exact member list. This facade is a separate
migration track from `MIGRATION.md`, which covers a direct AdMob/Unity Ads
port to `RewardedAd`/`InterstitialAd`.

Runtime divergences worth knowing before you rely on this facade:

- **`OnAdReceivedRewardEvent` fires at most once per `ShowRewardedAd`**, on
  the first graded answer with a positive amount, matching MAX's grant-once
  shape. The underlying `RewardedAdShowCallbacks.OnUserEarnedRewardItem`
  still fires once per graded answer (amount 0 for a wrong one) if you need
  per-answer granularity — it's just not behind the MAX-shaped event.
- **The slot's duration decides how much the break holds.** Level Moment fits
  questions to the seconds you declare: a fifteen-second slot holds several
  addition questions or one long-division question. The count stays fixed for
  the slot; how long a break actually runs varies with the learner. Leave the
  duration at 0 to take the format's default (15 seconds for a quick question,
  30 for a practice set, 60 for a mastery round or intro lesson).
- **`Reward.Amount` is the amount you declared for the ad unit.** MAX reads
  that from its dashboard; there is no dashboard here, so the mapping states
  it. An ad unit mapped without one reports 1 for a correct answer instead, so
  a handler that grants `reward.Amount` would pay 1. Declare the amount to keep
  it accurate.
- **`Reward.Label` is an opaque impression id**, not MAX's dashboard-configured
  currency name — do not display it to a player or compare it against a
  currency string.
- **`placement`/`customData` on `ShowInterstitial`/`ShowRewardedAd` are
  accepted and ignored** — there is no per-show placement override, and
  custom data set this way never reaches the reward webhook. Passing a
  non-empty value logs a one-time `Debug.LogWarning` so this isn't silent.
  Configure `LevelMomentConfig.CustomData` at `Initialize()` time instead.
- **Callbacks arrive on a later frame**, as MAX's do. No facade call raises
  an event before it returns; events are queued and delivered in order on the
  SDK's per-frame tick. Calling `LoadInterstitial`/`ShowInterstitial`/etc.
  from inside a callback is therefore safe, including for the same ad unit:
  it runs as a fresh call, on the frame that delivered the callback. `ShowX`
  from inside `OnAdLoadedEvent` and `LoadX` from inside a failure callback
  both work unchanged. A handler that retries a failing call on every delivery
  keeps retrying, once per delivery, for as long as the call keeps failing —
  cap or delay the retry. Two handlers that each retry the same failing call
  make the backlog grow every frame; the SDK logs one warning once 512
  callbacks are waiting.
- **`IsInterstitialReady`/`IsRewardedAdReady` read current state directly**,
  with no delivery delay. A handle is ready the moment its `Load` succeeds,
  before `OnAdLoadedEvent` is delivered. Trigger a show from either
  `OnAdLoadedEvent` or a readiness poll, not both: under MAX the two go true
  together, here a poll can show first and the `OnAdLoadedEvent` handler's
  `ShowX` then reports `already_showing`.
- **Error codes a switch on `ErrorInfo.Code` should know about**:
  `ShowInterstitial`/`ShowRewardedAd` fail with `not_loaded` when nothing is
  loaded for that ad unit (call Load first) and with `already_showing` when
  that ad unit's ad is already on screen (a duplicate Show, not a reload) —
  two distinct codes for two distinct situations.
- **A `Load` issued while that ad unit's break is on screen is held**,
  not run immediately or dropped — it runs once the break ends, so
  `OnAdLoadedEvent`/`OnAdLoadFailedEvent` for that reload arrive AFTER
  `OnAdHiddenEvent`, not before or during.

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
