# Changelog

## Unreleased

- `unsafeTesting.realPairing` runs the real pairing path against a local hosted page, from a game served on `http://localhost:<port>` or `http://127.0.0.1:<port>` only. `unsafeTesting.breakUrl` without it still selects sandbox content.

- The credential reply states `platform: "web"` and `storefront: null`, so Level Moment can apply its store rules to browser games.
- An `error` whose code is not one of the five public codes reaches `onAdFailedToShow` as `unknown`; a store-policy refusal reaches it as `no_fill`.

## 0.2.0

- Added the hosted break loader for browser and HTML5 games.
- `LevelMomentWebClient.initialize({ placementId })` uses the standard production service by default.
- `loadAd` prepares a handle and `show` opens the hosted activity.
- Reward callbacks report `amount: 1` for a correct answer and `amount: 0` otherwise; optional `rewardId` values are opaque correlation data.
- Sign-in is available as an optional connected-learning flow.

## Release guidance

Use a published package release in production. Test local examples against the
exact release selected for the game.
