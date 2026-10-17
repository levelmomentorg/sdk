# Upgrade an existing Level Moment integration

Use this path when the game already shows Level Moment breaks through an SDK
older than 0.3.0, such as one pinned to an old commit or still carrying a mock
mode. The public docs are the guide; open each page as page text through a
browser (see SKILL.md). Without web access, the summary beside each link says
what the step is.

1. Ask only the questions in `self-serve.md`, step 1, that the existing code
   and the developer's brief don't already answer. An integrated game usually
   answers the slots, the reward authority and the game name already.
2. [Port from a rewarded ad SDK, "Upgrade from an earlier Level Moment SDK"](https://levelmoment.com/docs/porting#upgrade):
   read the changelog and the migration guide for the game's SDK, install
   0.3.1 exactly (the `sdk-v0.3.1` tag for Flutter and Unity), then remove the
   mock or placeholder break and its fallback, `apiUrl`/`breakUrl`/hardcoded
   tokens, old formats (`flashcard`, `quiz`, `deep_dive`) and per-answer reward
   code.
3. [Quickstart, step 3](https://levelmoment.com/docs/getting-started#slot-size):
   declare a slot per moment (`adType`, `targetDurationSeconds`,
   `rewardAmount`, which replace the old format), sized by what it buys (30
   seconds for a reward, 18 for resuming play, nothing under 18), with
   `format: "practice_set"` on rewarded slots.
4. [Quickstart, step 4](https://levelmoment.com/docs/getting-started#integrate):
   call `ensureAccess()` from a tap before each break; for a break that opens
   on its own, from the tap that leads to it or a Connect button.
5. [Testing and sandbox](https://levelmoment.com/docs/testing): set up the
   sandbox token for the platform (Flutter reads it behind `kReleaseMode`),
   check each slot's pass, fail and load failure, and search a release build
   for `eply_sbx_` with a positive control.

Apply the lifecycle and checks in AGENT-INSTRUCTIONS.md and the "Agent-only
notes" section of `self-serve.md`. Report as `self-serve.md`, step 3,
describes, and add the SDK version you upgraded from.
