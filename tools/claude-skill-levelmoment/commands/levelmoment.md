---
description: Integrate or check a supplied Level Moment partner handoff. Subcommands: port, install, doctor, sandbox.
argument-hint: "[port|install|doctor|sandbox] [handoff path]"
---

# /levelmoment

Read `.claude/skills/levelmoment/AGENT-INSTRUCTIONS.md` from the game repository root before
doing anything. Follow that file for the supplied handoff, permitted edits,
reward lifecycle, verification, and report.

Interpret the first token of `$ARGUMENTS` as one of these actions:

| Action              | Read next                                         |
| ------------------- | ------------------------------------------------- |
| `port` or no action | `.claude/skills/levelmoment/reference/port.md`    |
| `install`           | `.claude/skills/levelmoment/reference/install.md` |
| `doctor`            | `.claude/skills/levelmoment/reference/doctor.md`  |
| `sandbox`           | `.claude/skills/levelmoment/reference/sandbox.md` |
| `help`              | Print this table and stop.                        |

`register` is unavailable. A Level Moment placement must be supplied in the
handoff manifest.
