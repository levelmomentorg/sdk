---
description: Set up Level Moment in a game (self-serve or from a supplied handoff), then move it to production. Subcommands: setup, deploy, port, install, doctor, sandbox.
argument-hint: "[setup|deploy|port|install|doctor|sandbox] [handoff path]"
---

# /levelmoment

Read `.claude/skills/levelmoment/AGENT-INSTRUCTIONS.md` from the game repository root before
doing anything. (Reading the packet in place, without the installer, use the same
paths relative to the packet folder.) Follow that file for the supplied handoff, permitted edits,
reward lifecycle, verification, and report.

Interpret the first token of `$ARGUMENTS` as one of these actions:

| Action                              | Read next                                            |
| ----------------------------------- | ---------------------------------------------------- |
| `setup` or no action, no handoff    | `.claude/skills/levelmoment/reference/self-serve.md` |
| `deploy`                            | `.claude/skills/levelmoment/reference/deploy.md`     |
| `port`, or no action with a handoff | `.claude/skills/levelmoment/reference/port.md`       |
| `install`                           | `.claude/skills/levelmoment/reference/install.md`    |
| `doctor`                            | `.claude/skills/levelmoment/reference/doctor.md`     |
| `sandbox`                           | `.claude/skills/levelmoment/reference/sandbox.md`    |
| `help`                              | Print this table and stop.                           |

`register` is part of `setup`: see `reference/self-serve.md`.
