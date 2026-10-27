# LevelMoment Unity SDK (`com.levelmoment.sdk`)

Version `0.4.2` installs from the public repository's `sdk-v0.4.2` tag.

Give a coding agent the placement ID, immutable SDK source, target slot, reward
action, and generated port plan. Require it to load the current
[porting documentation](https://levelmoment.com/docs/porting) and this package's
[migration guide](./MIGRATION.md) before it edits the game.

Unity Package Manager (UPM) package that integrates LevelMoment into Unity games (iOS, Android, and other WebView-capable platforms). Drop-in replacement for Google AdMob / Unity Ads **rewarded ads**: instead of a video, it shows a Level Moment break — a quick question or a practice set.

`Show()` opens the hosted `/break` page in a fullscreen WebView and reports
answers and completion through C# callbacks. `Load()` prepares the shell; it
does not preload question content.

**Note:** Credentials travel through the bridge and never in the hosted URL.
The Unity SDK keeps no copy of the player's Level Moment credential; the hosted
page stores it. Use `UnsafeTesting` only for sandbox credentials.

**Linking a device can open the device's browser.** When the player chooses to
approve the game in a browser instead of scanning the pairing code, the SDK
opens the approval link outside the WebView, so Level Moment sign-in never
happens inside the game. Never collect a Level Moment email code or any other
Level Moment credential in your own UI. After approval the player returns to
the game, and the break finishes linking on its own. The SDK passes only links
on the loaded Level Moment origin to the OS.

---

## Requirements

The package supports Unity 2021.3 LTS and later, including Unity 6 (6000.x).

Unity has **no built-in WebView**, so the SDK needs a WebView provider. The bundled provider targets the open-source [`gree/unity-webview`](https://github.com/gree/unity-webview) plugin.

### 1. Install this package and gree

Add both lines to the `dependencies` in `Packages/manifest.json`. Pin the SDK
tag, not a branch:

```json
"com.levelmoment.sdk": "https://github.com/levelmomentorg/sdk.git?path=/sdk/unity#sdk-v0.4.2",
"net.gree.unity-webview": "https://github.com/gree/unity-webview.git?path=/dist/package-nofragment#20f14061ca4b45e8490c1e89e00684a5d3ed6be0"
```

The package handles the rest of the gree setup:

- It depends on the Input System package and the web request module, which
  gree's assembly needs to compile.
- Its gree adapter compiles whenever the gree package is present and registers
  itself as the WebView provider at startup.
- Its editor hands UnityLinker a `link.xml` for the SDK and gree assemblies on
  every build, so il2cpp doesn't strip the self-registering adapter. Unity
  ignores a `link.xml` inside a git- or file-referenced package, so the game
  would otherwise need its own.
- If **Active Input Handling** is **Input Manager (Old)**, it sets it to
  **Both** and logs it, because the Input System package otherwise offers a
  switch to the new backends alone, which breaks every legacy `Input` call. It
  takes effect when the editor next starts, and it changes
  `ProjectSettings/ProjectSettings.asset`, so commit that change with the SDK.
  A project on **New** or **Both** is left alone.

### 2. Reference the SDK from assembly definitions

A game whose scripts sit in an assembly definition (`.asmdef`) adds
`LevelMomentSDK.Runtime` to that assembly's `references`, and so does each test
assembly that touches SDK types. Scripts in Unity's default `Assembly-CSharp`
need nothing.

### 3. Other WebView plugins

For Vuplex or 3D WebView, implement `ILevelMomentWebView` and call
`LevelMomentWebViewRegistry.Register(() => new YourAdapter())` at startup
instead of installing gree. A classic `.unitypackage` install of gree, without
the UPM package, needs the scripting define `LEVELMOMENT_GREE_WEBVIEW` and an
`Assets/link.xml` that preserves `unity-webview`.

### 4. Sandbox token

Put the game's sandbox token alone in `UserSettings/LevelMomentSandboxToken.txt`
in the project folder. Unity's standard `.gitignore` already excludes
`UserSettings/`. In the editor and development builds, `LevelMomentAds.Initialize`
uses it when the config sets no `UnsafeTesting`; the editor copies it into a
development build for the length of that build, and a release build never
includes it. A token file left under `Assets/**/Resources` by 0.3.1 fails a
release build. Set `UseSandboxTokenFile = false` to test real device linking in
a development build. See [Testing and sandbox](https://levelmoment.com/docs/testing).

### 5. Allow plain-HTTP for local development

iOS App Transport Security blocks the WebView from loading a local `/break`
page over HTTP. In development builds only, add
`NSAppTransportSecurity > NSAllowsLocalNetworking` (true) to the exported
Xcode project's Info.plist, for example from a post-process build step that
checks `BuildOptions.Development`. It lets the WebView load
`http://localhost`. Leave it out of release builds; production uses HTTPS
and needs no exception.

**Note:** iOS ignores `NSAllowsArbitraryLoads` in a dict that also contains
`NSAllowsLocalNetworking`.

### macOS standalone players

The package includes a native macOS WebView
(`Runtime/Plugins/macOS/LevelMomentWebView.bundle`). In a macOS standalone
player it registers itself and takes over from gree, with no scripting define.
The break opens as a real web view inside the player window. Its questions and
answers are named controls that VoiceOver, Voice Control, Switch Control, the
keyboard, and UI automation can reach. A provider the game registers itself
still takes precedence. In the Unity Editor, gree remains the provider.

**Note:** The SDK reads page messages once per frame. With **Run In
Background** off, a player that is not the active app pauses its frame loop,
so reward and dismiss callbacks arrive when the player becomes active again.
Automation that drives the break through the accessibility API without
activating the player window does not receive those callbacks until the window
becomes active.

---

## Quickstart

This is the [Quickstart](https://levelmoment.com/docs/getting-started) flow
for one rewarded moment.

```csharp
using System;
using LevelMoment;

// 1. Initialize once, in your bootstrap scene.
LevelMomentAds.Initialize(new LevelMomentConfig());

// 2. From the player's tap on the moment's button: check access, then show.
public void OnWatchForCoinsTapped()
{
    LevelMomentAds.EnsureAccess("YOUR_PLACEMENT_ID", access =>
    {
        // Canceled or TechnicalFailure: carry on without a break.
        if (access != EnsureSignedInResult.Ready) return;
        ShowRewardedBreak();
    });
}

// 3. Load a fresh handle for every break.
void ShowRewardedBreak()
{
    PauseGame();
    var granted = false;
    var finished = false;
    Action finish = () =>
    {
        if (finished) return;
        finished = true;
        ResumeGame();
    };
    // A reward moment: 30 seconds, about 5 questions. rewardAmount is for reporting.
    var slot = new LevelMomentAdSlot("rewarded", 30, rewardAmount: 50);
    RewardedAd.Load("YOUR_PLACEMENT_ID", new RewardedAdLoadCallbacks
    {
        OnAdLoaded = ad => ad.Show(new RewardedAdShowCallbacks
        {
            OnUserEarnedReward = reward =>
            {
                if (!finished && !granted)
                {
                    granted = true;
                    RecordGrantedReward(reward.RewardId);
                    GrantCoins(50);
                }
            },
            OnAdDismissed = finish,
            OnAdFailedToShow = _ => finish(),
        }),
        OnAdFailedToLoad = _ => finish(),
    }, format: "practice_set", slot: slot);
}
```

A game with no bootstrap scene object can initialize from a static method
instead. Without `RequireInternet`, `Initialize` shows nothing until a break or
gate opens, so it's safe in batch mode:

```csharp
static class LevelMomentBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init() => LevelMomentAds.Initialize(new LevelMomentConfig());
}
```

With `RequireInternet`, a batch-mode run on a machine with no network pauses
the game, so leave `RequireInternet` off when `Application.isBatchMode` is
true.

Call `EnsureAccess` before each break, from a tap the player chose. In sandbox
it returns `Ready` after a brief check. `EnsureSignedIn` and `IsSignedIn` check
only that the device is linked, not that the household has access; use them
only for a feature that needs the link and nothing else.

Size and declare each slot as the Quickstart's
[slot table](https://levelmoment.com/docs/getting-started#slot-size) shows:
30 seconds for a moment that grants a reward, 18 for one that resumes play or
grants nothing, and `format: "practice_set"` on every rewarded slot.

No token appears anywhere above — a paired device's credential lives in the
hosted page's storage. For sandbox, use the token file in Requirements step 4
or `UnsafeTesting` with an `eply_sbx_` token. The deprecated `studentToken` overload is refused in production.

A game that passes an explicit token needs a WebView provider implementing `ILevelMomentScriptableWebView` for the token to reach the page — see API below. The bundled gree adapter implements it.

One `placementId` is the stable integration key for the whole game. To report
different item or level breaks, register the slot type and dimensions in the
studio's Break performance screen, then declare them on each handle:

```csharp
using System.Collections.Generic;

var slot = new LevelMomentAdSlot(
    "rewarded", 30, slotType: "item_reward",
    dimensions: new Dictionary<string, object> {
        { "item", "sword" }, { "color", "blue" }
    });
RewardedAd.Load("YOUR_PLACEMENT_ID", callbacks,
    format: "practice_set", slot: slot);
```

For level transitions, register an integer `afterLevel` range once and send
the current level number. You do not need a key or catalog option per level.
Unknown reporting codes are omitted from reports without blocking the break.

## Require an internet connection

A break, `EnsureSignedIn`, `EnsureAccess`, `IsSignedIn`, or `CheckAccess`
called while `Application.internetReachability` reports no connection fails at
once without opening a WebView. A break calls `OnAdFailedToShow` with
`network_error`, the gates return `TechnicalFailure`, and the checks call
`onError`. While a hosted page loads, the SDK shows a spinner and a close
button. Closing it ends a break with `OnAdDismissed` and makes the gates return
`Canceled`.

To block play until the connection returns, set `RequireInternet`. The SDK
then shows a full-screen "No internet connection" message with a **Try again**
button whenever the device reports no connection, and removes it when the
connection returns.

`OnConnectionChange` runs on the main thread with `false` when the message
appears and `true` when it goes away. When the device is offline at
`Initialize()`, the first call arrives on the next frame.
`LevelMomentAds.IsOnline` reads the same state. The SDK reads reachability every
2 seconds and whenever the app regains focus or resumes. Call
`LevelMomentAds.Dispose()` when the game tears down.

Resume only when the device is online and no break is open. A break that is
open stays above the message until it ends, and it can end while the device
is offline:

```csharp
bool breakOpen;

LevelMomentAds.Initialize(new LevelMomentConfig
{
    RequireInternet = true,
    GameName = "YOUR_GAME_NAME",
    OnConnectionChange = online =>
    {
        if (!online) PauseGame();
        else if (!breakOpen) ResumeGame();
    },
});

// Set breakOpen = true before Load(), and call this from the break's
// dismiss and failure callbacks.
void OnBreakFinished()
{
    breakOpen = false;
    if (LevelMomentAds.IsOnline) ResumeGame();
}
```

A call made offline reports its failure first, while `IsOnline` can still read
true. `OnConnectionChange(false)` arrives on the next frame, so a game that
resumes in the failure callback pauses again a frame later.

The message is drawn with IMGUI over everything Unity renders, inside the safe
area. IMGUI can't stop input reaching the rest of the game: taps still reach
uGUI buttons, and your scripts still read `Input` and the Input System. Turn
off your own input while the device is offline. `PauseGame()` and
`ResumeGame()` above are yours; for uGUI, disable the `EventSystem` so no
button under the message responds:

```csharp
using UnityEngine.EventSystems;

[SerializeField] EventSystem eventSystem;       // the scene's EventSystem
[SerializeField] MonoBehaviour playerInput;     // your own input component

void PauseGame()
{
    Time.timeScale = 0f;
    AudioListener.pause = true;  // stop the game's sound
    playerInput.enabled = false;
    eventSystem.enabled = false;
}

void ResumeGame()
{
    eventSystem.enabled = true;
    playerInput.enabled = true;
    AudioListener.pause = false;
    Time.timeScale = 1f;
}
```

**Warning:** Hold the `EventSystem` in a field. `EventSystem.current` is null
while the `EventSystem` is disabled, so code that re-enables it through
`EventSystem.current` does nothing, and every button stays dead after the
connection returns.

Instead of the `EventSystem`, you can put a `CanvasGroup` on the UI root and
set its `interactable` and `blocksRaycasts` to false while offline.

**Note:** Only the device's own connection state counts. When the device is
online and Level Moment can't be reached, nothing is blocked: the break or
check fails and the game carries on.

---

## API

| Member                                                                      | Purpose                                                                                                                                                                                                                                                                      |
| --------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `LevelMomentAds.Initialize(LevelMomentConfig)`                              | One-time setup. Production uses canonical endpoints. Set `Mock` for the offline preview and `UnsafeTesting` for sandbox URLs and credentials.                                                                                                                                |
| `LevelMomentAds.IsInitialized`                                              | True once initialized.                                                                                                                                                                                                                                                       |
| `LevelMomentAds.IsOnline`                                                   | With `RequireInternet`, false while the no-connection message shows; it follows the watcher, so it can lag the device by up to 2 seconds. Without it, a fresh read of the device.                                                                                            |
| `LevelMomentAds.Dispose()`                                                  | Removes the no-connection message and stops watching the connection. Loaded breaks keep working.                                                                                                                                                                             |
| `LevelMomentAds.EnsureSignedIn(placementId, callback, studentToken?)`       | Optional identity gate. Opens the hosted `?mode=gate` surface and reports `EnsureSignedInResult.Ready` / `Canceled` / `TechnicalFailure` exactly once. Enable learning breaks on `Ready`; normal gameplay can continue after the other outcomes.                             |
| `LevelMomentAds.IsSignedIn(placementId, onResult, onError, studentToken?)`  | Authoritative credential check through a hidden `?mode=check` surface. Calls `onError`, not `onResult(false)`, when it cannot get an answer.                                                                                                                                 |
| `LevelMomentAds.EnsureAccess(placementId, callback, studentToken?)`         | Optional access gate on `/access?mode=gate`; reports the same `EnsureSignedInResult` values and callback semantics as `EnsureSignedIn`.                                                                                                                                      |
| `LevelMomentAds.CheckAccess(placementId, onResult, onError, studentToken?)` | Optional access check on `/access?mode=check`; `onResult(false)` means the access flow is needed, while technical failures call `onError`.                                                                                                                                   |
| `RewardedAd.Load(placementId, callbacks, format?, slot?)`                   | Synchronous mark-ready (no network). `callbacks`: `OnAdLoaded`, `OnAdFailedToLoad`. `slot`: a `LevelMomentAdSlot` declaring the moment.                                                                                                                                      |
| `RewardedAd.Load(placementId, studentToken, callbacks, format?)`            | Deprecated compatibility overload. The token is accepted only with an `eply_sbx_` credential in `UnsafeTesting`.                                                                                                                                                             |
| `InterstitialAd.Load(placementId, callbacks, format?, slot?)`               | Same as `RewardedAd.Load`, for a moment that grants nothing. Pass a `slot`. With no `slot` and no `format`, it declares an 18-second interstitial slot and asks for `mastery_round`.                                                                                         |
| `ad.Show(callbacks)`                                                        | Opens the hosted break in a WebView. `OnUserEarnedReward(item)` fires once for a server-confirmed passed graded break; dismiss and failure callbacks remain terminal.                                                                                                        |
| `callbacks.OnUserEarnedRewardItem`                                          | Optional alias for the same item (`RewardId`, `EarnedAt`), with no game-currency amount.                                                                                                                                                                                     |
| `ad.IsLoaded`                                                               | True when loaded and not yet shown/destroyed.                                                                                                                                                                                                                                |
| `ad.Destroy()`                                                              | Release the ad + tear down the WebView (no callbacks fired).                                                                                                                                                                                                                 |
| `ILevelMomentWebView` / `LevelMomentWebViewRegistry`                        | WebView provider seam for custom plugins.                                                                                                                                                                                                                                    |
| `ILevelMomentHeadlessWebView`                                               | Optional provider extension: `OpenHidden(url)` keeps the credential check off screen. The bundled gree adapter implements it; providers that do not still work, with the check briefly visible.                                                                              |
| `ILevelMomentRevealableWebView`                                             | Optional provider extension: `Reveal()` shows a surface opened with `OpenHidden(url)`. The SDK opens visible surfaces hidden and reveals them on `ready`, so its loading layer isn't covered by a blank native view. The bundled gree adapter and macOS view implement it.   |
| `ILevelMomentScriptableWebView`                                             | Optional provider extension: `EvaluateJS(js)` delivers a credential to the hosted page when the page asks for one. Required only if you pass an explicit `studentToken` — a paired device with no explicit token needs nothing here. The bundled gree adapter implements it. |

Every `studentToken?` parameter is deprecated compatibility surface. It accepts
only an `eply_sbx_` credential when `UnsafeTesting` is configured; production
credentials stay in the hosted bridge and are never supplied in game config.

**Callback semantics** (identical to the other LevelMoment SDKs):

- `OnUserEarnedReward(item)` — fires once for a server-confirmed passed graded break. `item.RewardId` equals the break ID; dedupe game grants by it. The game owns the item or currency amount.
- `OnUserEarnedRewardItem(item)` — optional alias with the same terminal reward item and earned time.
- `OnAdDismissed` — fires **exactly once** when the break ends. Resume the game here, unless `RequireInternet` is on and `LevelMomentAds.IsOnline` is false (see [Require an internet connection](#require-an-internet-connection)).
- `OnAdFailedToShow(error)` — fires instead of `OnAdDismissed` when the break can't be shown (not loaded, no connection, no WebView provider, page error, or the ad was already shown — see below).
- `format` — a floor for the break, not a choice: `quick_question`, `practice_set`, `mastery_round`, or `intro_lesson`. Declare the slot and Level Moment picks the format inside it. Pass `practice_set` on every rewarded slot, so a short slot is never served as one question; `RewardedAd.Load` defaults to `quick_question` only so older call sites keep compiling. Pass no `format` to `InterstitialAd.Load`: a rewarded format there has no effect, and an interstitial slot serves a round of questions either way.
- One `Show()` per handle: once a `RewardedAd`/`InterstitialAd` has been shown, `IsLoaded` is false and a second `Show()` reports `OnAdFailedToShow` with `not_loaded` rather than reopening it — get a fresh handle from `Load()` for each break.

A **15-second pre-`ready` watchdog** guarantees a crashed or unreachable page
can't cover the game forever — it resolves as a clean `OnAdDismissed`. After
`ready` there is no timeout (a player thinking through a question is never
force-closed).

`Show()` catches WebView-provider startup failures. If the provider factory or `Open()` throws, the SDK reports `webview_error` through `OnAdFailedToShow` and tears down the ad. Exceptions thrown by your own callbacks propagate to you unchanged.

**Startup gate semantics** (shared with the other LevelMoment SDKs and defined
by the Unity SDK's `EnsureSignedInResult`):

- `Ready` — enable learning breaks. Normal gameplay can start independently.
- `Canceled` — the player closed the gate or declined to link the device. Show your own "learning breaks are off" state or a retry action; do not retry automatically.
- `TechnicalFailure` — the gate could not run (no `Initialize()`, a bad config string, no connection, no WebView provider, or the surface never came up). Retry later.
- `IsSignedIn` is authoritative, not a cached flag — it re-validates the credential against the server every call, because a credential can be revoked between launches. An error means _unknown_, never _signed out_: treat it as "try again", and leave the current state alone.
- Both methods allow the hosted page 15 seconds to load. `EnsureSignedIn` then waits without a deadline for approval, so linking takes as long as the player takes. `IsSignedIn` has no approval step and is bounded end to end: it calls `onError` if the page does not load in 15 seconds, if the page's credential check runs longer than 10 seconds (the page reports `check_timeout`), or if 30 seconds pass with no answer at all.
- `EnsureAccess` and `CheckAccess` use the same outcome and deadline rules on `/access`; use them when access enablement is a separate publisher flow from identity sign-in.

---

## InterstitialAd

`InterstitialAd` is the same hosted `/break` WebView shell as `RewardedAd`,
with the same `Load()`/`Show()` lifecycle, watchdog, and dismiss and failure
callbacks. Use it for a moment that grants nothing, such as the pause between
levels where the game showed an interstitial ad. `InterstitialAdShowCallbacks`
has no reward callbacks.

```csharp
// A moment that grants nothing: 18 seconds, about 3 questions.
var slot = new LevelMomentAdSlot("interstitial", 18);
InterstitialAd.Load("YOUR_PLACEMENT_ID", new InterstitialAdLoadCallbacks
{
    OnAdLoaded = ad => ad.Show(new InterstitialAdShowCallbacks
    {
        OnAdDismissed = ResumeGame,
        OnAdFailedToShow = _ => ResumeGame(),
    }),
    OnAdFailedToLoad = _ => ResumeGame(),
}, slot: slot);
```

Always pass the slot, at 18 seconds or more. A `Load()` with no slot and no
`format` declares an 18-second interstitial slot and asks for `mastery_round`.
In 0.4.1 and earlier, that call served a single rewarded question. An
interstitial break serves a round of 2 to 10 questions and asks each missed
question again, with hints and then the worked answer.

The game gets no result from an interstitial break: every ending reaches
`OnAdDismissed` or `OnAdFailedToShow`, and both mean "carry on". Map either one
to the result your old interstitial's close callback produced. Don't grant
anything for it.

`OnAdDismissed` also fires when the page doesn't load within 15 seconds and
when the player closes the loading screen. To tell a break the player saw from
one that ended before it appeared, set `OnAdShowedFullScreenContent`, which
fires once when the break is on screen:

```csharp
var shown = false;
ad.Show(new InterstitialAdShowCallbacks
{
    OnAdShowedFullScreenContent = () => shown = true,
    OnAdDismissed = () => { if (!shown) LogBreakNotShown(); ResumeGame(); },
    OnAdFailedToShow = _ => ResumeGame(),
});
```

`RewardedAdShowCallbacks` has the same callback.

## Platform and storefront reporting

`LevelMomentAds.Initialize()` reads the device's platform and, on iOS, its App
Store storefront once and caches the result. The credential bridge sends both
to the hosted page next to every credential reply, so the server can decide
store policy for the placement (link-only checkout, sign-in only, or hidden,
depending on where the game is distributed).

- Platform is `ios` or `android`, from the running OS. macOS, the Unity
  Editor, and every other standalone target send no platform.
- Storefront is the iOS StoreKit storefront's ISO 3166-1 alpha-3 country
  code, such as `USA` (iOS 13 and later), or null when no Apple account is signed in or the device predates
  iOS 13. Android always sends no storefront — the SDK adds no Play Billing
  dependency, so it cannot read one, and a studio's own billing plugin never
  conflicts with it.

Reading StoreKit on the main thread inside `needCredential` would risk
missing the page's 500ms reply window, so the read happens once, at
`Initialize()`, on a device iOS player only. `Runtime/Plugins/iOS/LevelMomentStorefront.m`
holds the native call; nothing here needs App Store Connect access or a
sandbox tester account to build.

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

- **`OnAdReceivedRewardEvent` fires at most once per `ShowRewardedAd`**, when
  the server confirms that the whole graded break passed. Its label is the
  break reward ID; no per-answer callback authorizes a grant.
- **The slot's duration decides how much the break holds.** Level Moment fits
  questions to the seconds you declare: a fifteen-second slot holds several
  addition questions or one long-division question. The count stays fixed for
  the slot; how long a break actually runs varies with the learner. Leave the
  duration at 0 to take the format's default (15 seconds for a quick question,
  30 for a practice set, 60 for a mastery round or intro lesson).
- **`Reward.Amount` is the amount you declared for the ad unit.** MAX reads
  that from its dashboard; there is no dashboard here, so the mapping states
  it. An ad unit mapped without one reports 1 for a passed break. Declare the
  game-owned amount to keep it accurate.
- **`Reward.Label` is an opaque break reward id**, not MAX's dashboard-configured
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

## Test real pairing against a local stack

`UnsafeTesting.BreakUrl` on its own runs the sandbox: the page serves sandbox
questions and never pairs. To exercise pairing, a learner session, and the
learner's own topics against a hosted page on your machine, set
`RealPairing`:

```csharp
#if UNITY_EDITOR || DEVELOPMENT_BUILD
LevelMomentAds.Initialize(new LevelMomentConfig {
    UnsafeTesting = new UnsafeTesting {
        RealPairing = true,
        BreakUrl = "http://localhost:3000/break",
    },
});
#endif
```

Prerequisites:

- The web app running on port 3000, with the API's `APP_BASE_URL` set to
  `http://localhost:3000` so the pairing code and the browser approval link
  point at the local `/link` page.
- On Android, `adb reverse tcp:3000 tcp:3000` and `adb reverse tcp:8080 tcp:8080`,
  plus a debug-only network security config that allows cleartext to
  `localhost` and `127.0.0.1`.
- On iOS, the simulator. A physical iPhone has no route to your machine's
  `localhost`.

The SDK checks the configuration at `Initialize` and throws
`ArgumentException` when any of these is true:

- The build is not the editor or a development build. Real pairing compiles
  only under `UNITY_EDITOR` or `DEVELOPMENT_BUILD`.
- `BreakUrl` is not `http://localhost:<port>/…` or `http://127.0.0.1:<port>/…`
  with an explicit port. A query, fragment, userinfo, backslash, whitespace,
  `@`, or non-ASCII character is refused, and so is any other host, including
  `10.0.2.2`, `[::1]`, LAN addresses, and the production origin.
- `Token`, `ApiUrl`, `Mock`, or the deprecated top-level `ApiUrl` or
  `BreakUrl` is set. The local page pairs the device and uses its own API.

A per-call `studentToken` passed to `RewardedAd.Load`, `InterstitialAd.Load`,
or a gate method fails the call. Under real pairing the break URL carries no
`sandbox` and no `apiUrl` parameter, and the SDK answers the page's credential
request only on the local origin. `Initialize` logs one warning naming that
origin. Remove the setting before you build a release.

---

## Release verification

Automated C# checks do not replace validation in a real Unity editor, an
il2cpp build, and each target device. Before release:

1. **Open the package in Unity 2021.3 LTS** (or newer). Add both the SDK and
   `net.gree.unity-webview` per the Requirements section, with no scripting
   define.
   - Confirm the console is clean and that the bundled gree adapter compiles.
   - Confirm the package also compiles without the optional bundled provider when
     gree is absent.
   - Run the EditMode suites in the editor so bridge parsing meets the real
     `JsonUtility`.
2. **Accessibility check**: open a break in a built player and confirm that
   each answer is a named control inside the game window. On macOS, use
   Accessibility Inspector or VoiceOver. On iOS, use VoiceOver. On Android,
   use TalkBack.
3. **On-device smoke** (real WebView): build to iOS/Android with the canonical
   hosted service, trigger a break, and confirm a server-confirmed passed graded
   break fires `OnUserEarnedReward` with `RewardId`, while closing it fires `OnAdDismissed`
   exactly once.
4. **Offline**: turn on airplane mode and confirm a break fails at once and the
   game resumes. With `RequireInternet`, confirm the message appears within 2
   seconds, blocks the game, and goes away when the connection returns.

---

## Related

- [Integration documentation](https://levelmoment.com/docs/porting)
- React Native SDK (reference WebView shell): [`sdk/react-native/`](../react-native/README.md)
- Migrating from AdMob / Unity Ads: [MIGRATION.md](./MIGRATION.md)

`Samples~/PortalExample.cs` contains the portal setup example and is included in the sample compilation checks.
