# Changelog

## 0.3.1 — 2026-10-17

- BREAKING: a release build (`__DEV__` false) refuses `unsafeTesting.token` or a sandbox `studentToken`. `createForAdRequest()` and `checkAccess()` throw; `ensureAccess()` and `ensureSignedIn()` resolve `technicalFailure`. A release build that kept the token would serve every player unpaid sandbox breaks with no pairing. Debug builds are unchanged.
- A break no longer shows the native close button (×), and the Android back button does nothing on it, once the break page confirms it ends the break itself. The break URL carries `leave=page`, and the page's `ready` answers with `leaveSupported`; until then, and on a page that never answers, the button and back stay. A player can't close a break partway: it ends in a pass, a fail, or a load failure, and a mid-break pairing card offers **Not now**. The sign-in gate keeps its close button, which is a parent's "not now".
- When the break page reloads itself after pairing, the load watchdog is re-armed until the new page is ready. If the web view's content process stops, the break ends with an `error` event.

## 0.3.0 — 2026-10-16

- BREAKING: the `earnedReward` event fires at most once per break, when a passed break ends, with `{ type, rewardId, earnedAt }`. `amount` is gone. Only `quick_question` and `practice_set` report one; `mastery_round` and `intro_lesson` never do. Grant once per `rewardId` when it fires, and resume the game on dismiss or error.
- BREAKING: break formats are `quick_question`, `practice_set`, `mastery_round`, and `intro_lesson`. `flashcard`, `quiz`, and `deep_dive` no longer compile; the default is `quick_question`.
- The `slot` request option declares the ad slot a break fills: `adType`, `targetDurationSeconds`, an optional `rewardAmount`, and the reporting fields `slotType` and `dimensions`.
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
