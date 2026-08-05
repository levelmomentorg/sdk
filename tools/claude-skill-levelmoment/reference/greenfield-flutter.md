# Greenfield — Flutter

User has no existing ad SDK. Scaffold a fresh LevelMoment integration.

## Step 1 — add `levelmoment_ads` to `pubspec.yaml`

```yaml
dependencies:
  flutter:
    sdk: flutter
  levelmoment_ads:
    git:
      url: https://github.com/levelmomentorg/sdk.git
      path: sdk/flutter
      ref: v0.1.0
```

Don't run `flutter pub get` — let the user do it.

## Step 2 — initialize at startup

Read `.claude/skills/levelmoment/templates/flutter-init.dart.tmpl`. Find
`main()` in `lib/main.dart`. Insert the init lines:

```dart
WidgetsFlutterBinding.ensureInitialized();
await LevelMomentAds.instance.initialize(
  apiUrl: '{{API_URL}}',
  breakUrl: '{{BREAK_URL}}', // REQUIRED — where the hosted /break page lives
);
```

…immediately before `runApp(...)`. Substitute `{{API_URL}}` →
`https://api.levelmoment.com` and `{{BREAK_URL}}` → `https://app.levelmoment.com/break`.
`breakUrl` is a **required** named arg (the WebView shell has no other way to
find the hosted `/break` page); omitting it is a compile error.

If `main()` is not `async`, make it async and add the `await`.

## Step 3 — create an ad-break helper

Write `lib/levelmoment/ad_break.dart` from
`.claude/skills/levelmoment/templates/flutter-ad-break.dart.tmpl`.
Substitute `{{PLACEMENT_ID}}` (the only placeholder in this template).

## Step 4 — leave a TODO

Search the user's `lib/` for game-end handlers (`onLevelComplete`,
`gameOver`, `endRound`). If found, insert near them:

```dart
// LEVELMOMENT_TODO: await showAdBreak(context); from ad_break.dart
```

## Step 5 — summary

```
✓ Scaffolded LevelMoment integration:
  - pubspec.yaml — added levelmoment_ads
  - lib/main.dart — added LevelMomentAds.initialize
  - lib/levelmoment/ad_break.dart — created

Next steps:
1. Run `flutter pub get` (I didn't run it for you)
2. Call `showAdBreak(context)` at your game's pause points
3. Pass a real student token (from deep link or backend)
4. Test at https://app.levelmoment.com/sandbox
```
