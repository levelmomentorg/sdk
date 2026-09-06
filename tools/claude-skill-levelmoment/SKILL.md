---
name: levelmoment
description: Integrate a supplied Level Moment SDK preview into a game's existing rewarded slot. Use when a partner provides a Level Moment handoff manifest, asks to port a rewarded placement, or asks to diagnose that integration.
---

# Level Moment partner integration

Read `AGENT-INSTRUCTIONS.md` in this packet before inspecting or editing the
game. It is vendor-neutral and is the canonical integration contract.

This wrapper supports the `/levelmoment` command in tools that provide it. The
same packet also works when an agent reads `AGENT-INSTRUCTIONS.md` directly;
do not require an installer or a platform-specific command.

Use a supplied handoff manifest. Do not register a game, request private
platform access, guess an SDK artifact, or replace another ad slot.
