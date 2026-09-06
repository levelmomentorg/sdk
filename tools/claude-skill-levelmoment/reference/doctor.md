# Check a supplied integration

Read [the partner handoff](partner-handoff.md), then inspect only its target
slot. Report each result as pass, fail, or unknown.

- The installed artifact matches the handoff SHA-256.
- Production code supplies the handoff placement ID and no endpoint, token,
  password, or preview secret.
- The target slot creates a fresh handle for every show.
- A load failure restores regular gameplay once.
- The first correct answer grants the handoff reward once.
- Wrong and later correct answers grant nothing.
- Dismissal and failure each resume once.
- A failure after a first correct answer does not undo the reward.
- `ensureAccess` only runs from a deliberate player action. `checkAccess`, if
  present, treats technical failure as unknown. Access cancellation does not
  reopen the flow or pause the game.
- Other ad slots remain unchanged.

Run the handoff's verification commands. Mark real-game and target-device
evidence unknown until it has been collected; do not claim it from static code.
