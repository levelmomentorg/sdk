# `/levelmoment doctor` — diagnose an existing LevelMoment integration

Use this when an integration isn't working and the user wants a triage
checklist run automatically.

## Step 1 — detect platform

Same as `port.md` Step 1.

## Step 2 — run the checks

For each check, print a status line (`✓` pass / `✗` fail / `?` warn) and
keep going. Don't stop at the first failure — produce a full report.

### Universal checks

- **SDK installed** — manifest (`package.json` / `pubspec.yaml` /
  `manifest.json`) lists the LevelMoment SDK at `^0.1.0` or newer.
- **Placement ID configured** — search for `pl_PLACEHOLDER` in source.
  If found: `✗ Placement ID is still a placeholder. Run `/levelmoment
  register <name>` or paste your real placement ID.`
- **Student token wired** — find the SDK init call site. Check that
  `studentToken` is sourced from a URL param, deep link, or function
  call — not a hardcoded empty string `""`.
- **Reward callback non-empty** — `onUserEarnedReward` callback body
  must do something (call `grantBonus`, increment score, etc.) — not
  just empty `{}` or only a console log.
- **Dismiss callback non-empty** — `onAdDismissed` / `closed` /
  `onAdDismissedFullScreenContent` must do something (resume game).

### Web-specific

- **Init runs once** — `LevelMomentWebClient.initialize(...)` appears
  exactly once in source.
- **`client.start()` called** — required for impression flush.
- **No leftover `googletag`** — no remaining references to googletag in
  active code paths.

### React Native-specific

- **`<LevelMomentAdModal />` rendered** — must appear once in the app root.
- **`react-native-webview` peer dep present** — in `package.json`.
- **iOS pods up to date** — recommend `cd ios && pod install` if iOS
  build is detected.

### Flutter-specific

- **`LevelMomentAds.instance.initialize()` called** — once, before `runApp`.
- **`context:` passed to `.show()`** — required.

### Unity-specific

- **`LevelMomentAds.Initialize(...)` in `Awake`** — once.
- **No leftover `IUnityAdsInitializationListener`** — must be removed
  from the class declarations.
- **WebView plugin installed** — required peer dependency for native
  builds. If missing, warn.
- **`INTERNET` permission in `AndroidManifest.xml`** for Android builds.

## Step 3 — print the report

Use this exact format:

```
LevelMoment Integration Doctor
==========================

Platform: <web | react-native | flutter | unity>
SDK version: <e.g. ^0.1.0>

Universal:
  ✓ SDK installed
  ✗ Placement ID is still a placeholder (src/Boot.ts:42)
  ✓ Student token wired
  ? Reward callback only logs — preserve original game logic?
  ✓ Dismiss callback resumes game

Platform-specific (web):
  ✓ Init runs once
  ✗ client.start() not called — add after initialize()
  ✓ No leftover googletag references

Found 2 issues, 1 warning.
Suggested fixes:
  1. Replace `pl_PLACEHOLDER` at src/Boot.ts:42 (run `/levelmoment register`)
  2. Add `client.start()` at src/Boot.ts:48
```

## Step 4 — wrap up

If everything passes:

```
✓ Integration looks healthy. Test it: https://app.levelmoment.com/sandbox
```

If issues found, list the fixes and offer:

> "Want me to fix these automatically? I can re-run `/levelmoment port` to
> address them."
