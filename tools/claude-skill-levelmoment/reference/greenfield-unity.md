# Greenfield — Unity

User has no existing ad SDK. Scaffold a fresh LevelMoment integration.

The SDK is a **thin WebView shell** (ADR-001): it renders nothing. `Show()` opens
the hosted `/break` page in a WebView and calls back on reward / dismiss. There
is no question UI to build — do not scaffold a `RenderQuestion` method or a
`Question` type; the hosted page handles all of it.

## Step 1 — add the UPM packages

Edit `Packages/manifest.json` to add the SDK **and** a WebView provider
(`gree/unity-webview`) — Unity has no built-in WebView and the break renders in
one:

```json
"com.levelmoment.sdk": "https://github.com/levelmomentorg/sdk.git?path=sdk/unity#v0.1.0",
"net.gree.unity-webview": "https://github.com/gree/unity-webview.git?path=/dist/package"
```

## Step 2 — add the scripting define

**Project Settings → Player → Other Settings → Scripting Define Symbols**, add:

```
LEVELMOMENT_GREE_WEBVIEW
```

This compiles the bundled gree adapter, which self-registers as the WebView
provider at startup. Without it, `Show()` fails fast via `OnAdFailedToShow`
(never a broken screen). Using a different plugin (Vuplex, 3D WebView)? Implement
`ILevelMomentWebView` and call `LevelMomentWebViewRegistry.Register(...)` at
startup instead.

## Step 3 — create the AdsManager

Write `Assets/Scripts/AdsManager.cs` from
`.claude/skills/levelmoment/templates/unity-AdsManager.cs.tmpl`. Substitute
`{{API_URL}}`, `{{BREAK_URL}}`, and `{{PLACEMENT_ID}}`.

If `Assets/Scripts/` doesn't exist, ask the user where to put it.

## Step 4 — guidance on attaching the script

Print:

```
Next steps in the Unity Editor:
1. Create an empty GameObject in your first scene named "LevelMoment"
2. Drag AdsManager.cs onto it
3. Call AdsManager.Instance.ShowAd() from your game's pause-point handlers
   (level complete, lives lost, etc.)

The SDK renders the break itself — you do NOT build any question UI.
Grant your in-game bonus in the OnUserEarnedReward callback (amount == 1),
and always resume the game in OnAdDismissed.
```

## Step 5 — summary

```
✓ Scaffolded LevelMoment integration:
  - Packages/manifest.json — added com.levelmoment.sdk + net.gree.unity-webview
  - Scripting define — LEVELMOMENT_GREE_WEBVIEW
  - Assets/Scripts/AdsManager.cs — created

Next steps:
1. Refresh Unity Package Manager
2. Confirm the LEVELMOMENT_GREE_WEBVIEW define is set (Player Settings)
3. Attach AdsManager to a GameObject and wire ShowAd()
4. Test at https://app.levelmoment.com/sandbox
```
