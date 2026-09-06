# Level Moment partner handoff

Use this packet to place a supplied Level Moment SDK preview in one existing
rewarded slot. The game keeps its own identity, codebase, and non-target ad
placements. Level Moment owns the hosted break and access pairing behind the
SDK.

## Start with the handoff

1. Read the partner's handoff JSON. Use
   [`handoff.example.json`](handoff.example.json) as its field contract.
2. Require the platform, placement ID, target slot, reward action, install
   command, and verification commands before editing. For Web and React
   Native, require a path and SHA-256 for the platform artifact and every
   required artifact; ignore the native-source fields. For Flutter and Unity,
   require a source repository, immutable ref, and an explicit peer dependency
   list; ignore the artifact fields. Each listed peer needs an exact version.
3. Verify each applicable artifact's SHA-256 or immutable source ref before
   installation. Use the exact source and install command from the handoff. Do not substitute
   npm's public `0.1.2`, a floating Git ref, a guessed URL, or a version pin.
4. Find the supplied target slot and every load, show, reward, terminal, and
   pause/resume path that belongs to it. Preserve every other ad placement.

Treat the supplied placement ID as opaque. Use it exactly as supplied; do not
infer a client-side format or promise that a local pattern check proves it is
valid.

The partner has authorized edits necessary to complete this supplied handoff:
source, dependency manifest, generated lockfile, and the focused test or build
commands listed in the handoff. Do not ask again for those edits or checks.
Ask only when the handoff lacks a required value or the target slot cannot be
identified safely.

## Integrate one rewarded slot

Install only the immutable preview artifact the partner supplied. Production
SDK configuration uses the supplied `placementId` alone. Do not add
`apiUrl`, `breakUrl`, `studentToken`, a production credential, or a preview
password to game code, a manifest, or a URL. The SDK manages access pairing.

Create a fresh rewarded-ad handle for each call to the target slot's `show`.
Do not reuse a handle after it reaches a terminal callback.

When the target slot opens:

1. Pause the game once.
2. Show the new handle.
3. On the first `amount == 1` reward callback, run the handoff's chosen bonus
   action once. Ignore later correct and incorrect reward callbacks.
4. On either terminal callback, resume once and prepare a fresh handle for the
   next slot. A reward already granted stays granted if a later failure arrives.

If the new handle fails to load, restore regular gameplay through the same
terminal-once resume path. Do not leave a paused game waiting for a handle that
never became ready.

Preserve the game's reward authority. These callback examples grant a local
game bonus. If the existing slot grants rewards through a game server, keep
that server path and require the partner's verified-webhook instructions before
changing it. A client callback must not authorize a server-held balance. Use
the opaque `rewardId` to reconcile verified events and deduplicate delivery;
keep webhook signing secrets on the game server.

Use `ensureAccess` only from a player action that deliberately enables
learning. Treat `ready` as permission to continue that action, and keep normal
gameplay available for `canceled` or `technicalFailure`. Do not automatically
reopen access after cancellation. When one player action first enables access
and then opens a break, wait for `ready` before pausing and showing the break.
`checkAccess` is an optional noninteractive check; a technical failure is
unknown, not a false access result.

Use these platform entry points:

| Platform     | Production setup                                             | Rewarded handle                                                                 | Access methods                                                  |
| ------------ | ------------------------------------------------------------ | ------------------------------------------------------------------------------- | --------------------------------------------------------------- |
| Web          | `LevelMomentWebClient.initialize({ placementId })`           | `client.loadAd()` then `ad.show()`                                              | `client.ensureAccess()`, `client.checkAccess()`                 |
| React Native | `<LevelMomentAdModal />` once at app root                    | `LevelMomentAd.createForAdRequest(placementId, {})`, then `load()` and `show()` | `ensureAccess({ placementId })`, `checkAccess({ placementId })` |
| Flutter      | Initialize once; pass the supplied placement ID to each load | `LevelMomentRewardedAd.load()` then `ad.show(context:)`                         | `LevelMomentAds.instance.ensureAccess(...)`, `checkAccess(...)` |
| Unity        | Initialize once; pass the supplied placement ID to each load | `RewardedAd.Load()` then `ad.Show()`                                            | `LevelMomentAds.EnsureAccess(...)`, `CheckAccess(...)`          |

Read the platform guide in `reference/` before editing. The templates in
`templates/` demonstrate the same lifecycle; replace only their marked game
hooks with the supplied slot and reward action.

## Check and report

Run the handoff's focused install, test, and build commands. Confirm the
target slot handles these cases:

- A first correct answer grants one bonus.
- A second correct answer grants no second bonus.
- A wrong answer grants no bonus.
- Closing the break resumes once.
- A show failure resumes once and does not revoke an earlier bonus.

Report the artifact checksum you verified, files changed, commands run, and
their results. Separate automated evidence from evidence that remains to be
collected in a real game build and on each target device.
