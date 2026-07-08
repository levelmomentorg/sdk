# Greenfield — Unity

User has no existing ad SDK. Scaffold a fresh LevelMoment integration.

## Step 1 — add the UPM package

Edit `Packages/manifest.json` to add:

```json
"com.levelmoment.sdk": "https://github.com/levelmomentorg/sdk.git?path=sdk/unity#v0.1.0"
```

## Step 2 — create the AdsManager

Write `Assets/Scripts/AdsManager.cs` from
`.claude/skills/levelmoment/templates/unity-AdsManager.cs.tmpl`. Substitute
`{{PLACEMENT_ID}}` and `{{API_URL}}`.

If `Assets/Scripts/` doesn't exist, ask the user where to put it.

## Step 3 — guidance on attaching the script

Print:

```
Next steps in the Unity Editor:
1. Create an empty GameObject in your first scene named "LevelMoment"
2. Drag AdsManager.cs onto it
3. Call AdsManager.ShowAd() from your game's pause-point handlers
4. Implement RenderQuestion(Question q) in AdsManager — this is your UI
```

## Step 4 — WebView plugin reminder

LevelMoment's Unity SDK uses a WebView plugin to render questions. The user
must install one of:

- 3D WebView for Android, iOS, WebGL (paid, recommended)
- UniWebView (paid)
- gree/unity-webview (free, less polished)

Print this and let the user choose.

## Step 5 — summary

```
✓ Scaffolded LevelMoment integration:
  - Packages/manifest.json — added com.levelmoment.sdk
  - Assets/Scripts/AdsManager.cs — created

Next steps:
1. Refresh Unity Package Manager
2. Install a WebView plugin (see options above)
3. Attach AdsManager to a GameObject and wire ShowAd()
4. Implement RenderQuestion() with your UI
5. Test at https://app.levelmoment.com/sandbox
```
