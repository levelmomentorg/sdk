# Changelog

## 0.4.0 — 2026-10-25

- A break, `ensureAccess()`, `checkAccess()`, `ensureSignedIn()` or `isSignedIn()` called while the browser reports no network connection fails at once without mounting the hosted frame. A break calls `onAdFailedToShow` with `network_error` (or `onAdDismissed` when there is no `onAdFailedToShow`); the gates resolve `technicalFailure`; the checks reject.
- While a hosted frame loads, the SDK covers it with a spinner and a close button. Closing it ends a break with `onAdDismissed` and makes `ensureAccess()` and `ensureSignedIn()` resolve `canceled`; a page that hasn't loaded after 15 seconds still ends a break with `onAdDismissed` and the gates with `technicalFailure`.
- `requireInternet: true` in the client config shows a full-screen "No internet connection" message, with Try again, whenever the browser reports no connection. `gameName` names the game in it and `onConnectionChange(online)` lets the game pause. Off by default.
- `client.isOnline()` and `client.dispose()`. Call `dispose()` when the game tears down.

## 0.3.2 — 2026-10-20

- No changes. Released with the Unity package so every SDK shares one version.

## 0.3.1 — 2026-10-17

- The break URL carries `leave=page`, so a mid-break pairing card offers **Not now**, which ends the break with no reward.

## 0.3.0 — 2026-10-16

- BREAKING: `onUserEarnedReward` fires at most once per break, when a passed break ends, with `{ type, rewardId, earnedAt }`. `amount` is gone. Only `quick_question` and `practice_set` report one; `mastery_round` and `intro_lesson` never do. Grant once per `rewardId` when it fires, and resume the game on dismiss or error.
- BREAKING: break formats are `quick_question`, `practice_set`, `mastery_round`, and `intro_lesson`. `flashcard`, `quiz`, and `deep_dive` no longer compile; the default is `quick_question`.
- `slot` in the client config declares the ad slot a break fills: `adType`, `targetDurationSeconds`, an optional `rewardAmount`, and the reporting fields `slotType` and `dimensions`.
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
