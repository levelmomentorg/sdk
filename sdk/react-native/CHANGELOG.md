# Changelog

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
