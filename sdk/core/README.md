# @levelmoment/sdk-core

Public TypeScript contracts shared by the Level Moment platform SDKs.

**Preview:** The repository version is `0.2.0`, while the public npm channel
currently provides `0.1.2`. Use the immutable preview artifact or reference
supplied for your partner integration.

The package exposes the configuration, break-format, sign-in result, reward,
and public error types used by web and React Native integrations. It contains
no learning content or game runtime. Install a platform package to show a
break; the platform package brings this contract package along.

`placementId` is the stable public key for one game, not a key per in-game
location. One game can declare several slots. The optional slot declaration
combines serving settings (`adType`, `targetDurationSeconds`, and an optional
game-owned `rewardAmount`) with pre-registered reporting fields (`slotType`
and `dimensions`). The studio portal registers category codes and integer
ranges before the game sends them. `rewardId` is the server-confirmed break ID
for one earned reward; no game-currency value is supplied by Level Moment.

Give a coding agent the placement ID, immutable SDK source, target slot, reward
action, and verification commands. Require it to load the current
[porting documentation](https://levelmoment.com/docs/porting) and the migration
guide for the game platform before it edits the game.

See the [developer documentation](https://levelmoment.com/docs) for setup and
the [SDK repository](https://github.com/levelmomentorg/sdk) for release notes.
