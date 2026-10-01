# Changelog

## Unreleased

- `unsafeTesting.realPairing` runs the real pairing path against a local hosted page in a debug build. Its credential is kept in a keychain entry separate from the production one, so testing never reads, writes, or clears a production credential.
- The keychain store's `get`, `set`, and `clear` take the hosted origin as their first argument.

- The credential reply carries `platform` (`ios` or `android`, from `Platform.OS`) and `storefront: null`, so Level Moment can apply its store rules. Reading the App Store storefront comes in a later release.
- An `error` event's code is always one of the five public codes; a store-policy refusal arrives as `no_fill`.

## 0.2.0

- Added the hosted break loader for iOS and Android games.
- `LevelMomentAd.createForAdRequest(placementId, {})` uses the standard production service by default.
- Mount `LevelMomentAdModal` once at the application root.
- `load` prepares a handle and `show` opens the hosted activity.
- Reward callbacks report `amount: 1` for a correct answer and `amount: 0` otherwise; optional `rewardId` values are opaque correlation data.
- Sign-in is available as an optional connected-learning flow.

## Release guidance

Use a published package release in production. Test local examples against the
exact release selected for the game.
