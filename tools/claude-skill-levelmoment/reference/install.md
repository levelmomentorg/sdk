# `/levelmoment install` — install the SDK without modifying source

Use this when the user wants the SDK as a dependency but will wire it up
manually. Useful for: copy-pasting the snippets, exploratory work,
greenfield projects.

## Step 1 — detect platform

Same detection rules as `port.md` Step 1. If multiple match, ask.

If `$ARGUMENTS` after `install ` is `web`, `react-native`, `flutter`, or
`unity`, skip detection and use that.

## Step 2 — install

### Web

```bash
npm install @levelmoment/sdk-web
```

Ask the user before running `npm install` if `node_modules` doesn't exist
or if there's a `package-lock.json` (might have side effects).

### React Native

```bash
npm install @levelmoment/sdk-react-native react-native-webview
```

If the project is using Expo, also remind the user: `react-native-webview`
is already in the Expo Go runtime; for dev clients, run
`npx expo install react-native-webview`.

### Flutter

Edit `pubspec.yaml` to add:

```yaml
dependencies:
  levelmoment_ads:
    git:
      url: https://github.com/levelmomentorg/sdk.git
      path: sdk/flutter
      ref: v0.1.0
```

Don't run `flutter pub get` — let the user do it.

### Unity

Edit `Packages/manifest.json` to add:

```json
"com.levelmoment.sdk": "https://github.com/levelmomentorg/sdk.git?path=sdk/unity#v0.1.0"
```

Print a note: the user must refresh Unity Package Manager
(`Window → Package Manager`) for Unity to pick up the new dep.

## Step 3 — confirm

```
✓ Installed @levelmoment/sdk-<platform>

Next steps:
1. Read the integration docs: https://app.levelmoment.com/docs/getting-started
2. Or run `/levelmoment port` to auto-port your existing ad calls
3. Or `/levelmoment register <game name>` to get a placement ID first
```

Do not modify any source files. That's what `port` is for.
