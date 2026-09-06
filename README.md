# Level Moment SDK

Client SDKs for [Level Moment](https://levelmoment.com). Add a break to your
game, connect it to a placement, and show a short educational activity when
play reaches a natural pause.

`v0.2.0-preview.1` is an immutable preview release. It is not
production-certified. The release assets and this repository can be read and
downloaded without signing in.

## Packages

| Platform                     | Release artifacts                                                                                                                                                                                                                                             |
| ---------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Web / HTML5                  | [`sdk-core`](https://github.com/levelmomentorg/sdk/releases/download/v0.2.0-preview.1/levelmoment-sdk-core-0.2.0.tgz) + [`sdk-web`](https://github.com/levelmomentorg/sdk/releases/download/v0.2.0-preview.1/levelmoment-sdk-web-0.2.0.tgz)                   |
| React Native (iOS + Android) | [`sdk-core`](https://github.com/levelmomentorg/sdk/releases/download/v0.2.0-preview.1/levelmoment-sdk-core-0.2.0.tgz) + [`sdk-react-native`](https://github.com/levelmomentorg/sdk/releases/download/v0.2.0-preview.1/levelmoment-sdk-react-native-0.2.0.tgz) |
| Unity                        | `com.levelmoment.sdk` via the immutable Git tag below                                                                                                                                                                                                         |
| Flutter                      | `levelmoment_ads` via the immutable Git tag below                                                                                                                                                                                                             |

### Web

Install both artifacts from the preview release:

```sh
npm install \
  https://github.com/levelmomentorg/sdk/releases/download/v0.2.0-preview.1/levelmoment-sdk-core-0.2.0.tgz \
  https://github.com/levelmomentorg/sdk/releases/download/v0.2.0-preview.1/levelmoment-sdk-web-0.2.0.tgz
```

### React Native

Install both artifacts and the WebView dependency:

```sh
npm install \
  https://github.com/levelmomentorg/sdk/releases/download/v0.2.0-preview.1/levelmoment-sdk-core-0.2.0.tgz \
  https://github.com/levelmomentorg/sdk/releases/download/v0.2.0-preview.1/levelmoment-sdk-react-native-0.2.0.tgz \
  react-native-webview
```

### Unity

In the Unity Package Manager, choose **Add package from git URL** and enter:

```
https://github.com/levelmomentorg/sdk.git?path=sdk/unity#v0.2.0-preview.1
```

### Flutter

```yaml
dependencies:
  levelmoment_ads:
    git:
      url: https://github.com/levelmomentorg/sdk.git
      ref: v0.2.0-preview.1
      path: sdk/flutter
```

## Integration packet

Start with [tools/claude-skill-levelmoment](./tools/claude-skill-levelmoment/).
It contains `AGENT-INSTRUCTIONS.md`, the handoff template, platform guides,
and integration templates. Give an agent that complete directory when it ports
an existing game.

Bots can begin at [AGENTS.md](./AGENTS.md) or [llms.txt](./llms.txt).

## Getting started

Follow the packet's local guides to integrate and test the SDK. Register a
game and obtain its placement ID through the Level Moment developer portal.

Before release, certify each target device in preview. Test sign-in, the
fullscreen break, answer and reward callbacks, dismissal, and offline failure
handling on every supported OS and screen size. Preview traffic does not count
as production activity.

The web and React Native packages bundle the same skill under `claude/` in
their published tarballs.

## License

[MIT](./LICENSE) © Level Moment
