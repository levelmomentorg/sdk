# Level Moment SDK

Client SDKs for [Level Moment](https://levelmoment.com) — educational ad breaks
that turn a game's interstitial slots into short, curriculum-aligned questions.
Drop the SDK into your game, show a break, and earn from parent subscriptions
instead of per-impression CPM.

## Packages

| Platform                     | Package                                                                                        | Install                                                          |
| ---------------------------- | ---------------------------------------------------------------------------------------------- | ---------------------------------------------------------------- |
| Web / HTML5                  | [`@levelmoment/sdk-web`](https://www.npmjs.com/package/@levelmoment/sdk-web)                   | `npm install @levelmoment/sdk-web`                               |
| React Native (iOS + Android) | [`@levelmoment/sdk-react-native`](https://www.npmjs.com/package/@levelmoment/sdk-react-native) | `npm install @levelmoment/sdk-react-native react-native-webview` |
| Shared core (peer dep)       | [`@levelmoment/sdk-core`](https://www.npmjs.com/package/@levelmoment/sdk-core)                 | pulled in automatically                                          |
| Unity                        | `com.levelmoment.sdk`                                                                          | Package Manager → Add from git URL (below)                       |
| Flutter                      | `levelmoment_ads`                                                                              | git dependency (below)                                           |

### Unity

In the Unity Package Manager, choose **Add package from git URL** and enter:

```
https://github.com/levelmomentorg/sdk.git?path=sdk/unity
```

### Flutter

```yaml
dependencies:
  levelmoment_ads:
    git:
      url: https://github.com/levelmomentorg/sdk.git
      path: sdk/flutter
```

## Getting started

Full integration guides — registering a game, obtaining an API key, showing a
break, and testing in sandbox — live in the developer docs:

- **Docs:** https://app.levelmoment.com/docs
- **Developer portal:** https://app.levelmoment.com

The web and React Native packages also ship a Claude Code skill (bundled under
`claude/` in each published tarball) to help port an existing game — run
`npx @levelmoment/sdk-web install-skill`.

## License

[MIT](./LICENSE) © Level Moment
