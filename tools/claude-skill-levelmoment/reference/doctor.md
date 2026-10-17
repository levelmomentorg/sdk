# Check a supplied integration

Read [the partner handoff](partner-handoff.md), then inspect only its target
slot. Report each result as pass, fail, or unknown.

- The installed artifact matches the handoff SHA-256.
- Production code supplies the handoff placement ID and no endpoint, token,
  password, or preview secret.
- The target slot creates a fresh handle for every show.
- A load failure restores regular gameplay once.
- A completed-and-passed graded break grants the handoff reward once by
  `rewardId`, independent of its question count.
- Individual answers and failed or abandoned breaks grant nothing.
- Dismissal and failure each resume once.
- A failure after a confirmed terminal reward does not undo that reward.
- `ensureAccess` only runs from a deliberate player action. `checkAccess`, if
  present, treats technical failure as unknown. Access cancellation does not
  reopen the flow or pause the game.
- Other ad slots remain unchanged.

Run the handoff's verification commands. Mark real-game and target-device
evidence unknown until it has been collected; do not claim it from static code.

## Sandbox errors

For a failing sandbox break, follow
[Troubleshooting](https://levelmoment.com/docs/errors#troubleshooting):
no break and `technicalFailure` from `ensureAccess()` means the placement ID
isn't a registered game; a device-linking card means the token is missing,
wrong or revoked, or the game is on another account (check the break URL for
`sandbox=true`); `no_fill` means nothing to serve, so report the time and
placement ID to Level Moment.
