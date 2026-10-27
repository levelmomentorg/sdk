# Level Moment SDK

Client SDKs for [Level Moment](https://levelmoment.com). Add a break to your
game, connect it to a placement, and show a short educational activity when
play reaches a natural pause. The current release is 0.4.2.

## Install

Pin the exact version and commit your lockfile so every build runs the one you tested.

### Web

```sh
npm install --save-exact @levelmoment/sdk-web@0.4.2
```

### React Native

```sh
npm install --save-exact @levelmoment/sdk-react-native@0.4.2 react-native-webview
```

### Flutter

Add the package to `pubspec.yaml`:

```yaml
dependencies:
  levelmoment_ads:
    git:
      url: https://github.com/levelmomentorg/sdk.git
      ref: sdk-v0.4.2
      path: sdk/flutter
```

### Unity

In `Packages/manifest.json`, add:

```json
"com.levelmoment.sdk": "https://github.com/levelmomentorg/sdk.git?path=/sdk/unity#sdk-v0.4.2"
```

Unity also needs a WebView provider. The
[Unity package README](./sdk/unity/README.md#requirements) lists the steps.

## Documentation

- [Getting started](https://levelmoment.com/docs/getting-started)
- [Testing](https://levelmoment.com/docs/testing)
- [Going live](https://levelmoment.com/docs/going-live)

Changelogs:
[web](./sdk/web/CHANGELOG.md),
[React Native](./sdk/react-native/CHANGELOG.md),
[core](./sdk/core/CHANGELOG.md),
[Flutter](./sdk/flutter/CHANGELOG.md),
[Unity](./sdk/unity/CHANGELOG.md).

## Integration packet

[tools/claude-skill-levelmoment](./tools/claude-skill-levelmoment/) holds the
agent instructions, handoff template, platform guides, and integration
templates. Give an agent that whole directory when it ports a game. Bots can
begin at [AGENTS.md](./AGENTS.md) or [llms.txt](./llms.txt).

## License

[MIT](./LICENSE) © Level Moment
