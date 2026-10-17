---
name: levelmoment
description: Set up Level Moment learning breaks in a game, from registering it to verified sandbox breaks, or integrate a supplied handoff manifest. Use when a developer asks to add, set up, port or diagnose Level Moment in a game.
---

# Level Moment partner integration

Every setup step lives in the public docs at https://levelmoment.com/docs; this
packet links to them, and its summaries change only after the docs do.

Read `AGENT-INSTRUCTIONS.md` in this packet before inspecting or editing the
game. It is vendor-neutral and holds the handoff contract and the break
lifecycle every path follows.

This wrapper supports the `/levelmoment` command in tools that provide it. The
same packet also works when an agent reads `AGENT-INSTRUCTIONS.md` directly;
do not require an installer or a platform-specific command.

Choose the path first:

- **A handoff manifest was supplied:** follow AGENT-INSTRUCTIONS.md as written.
- **No handoff:** the developer is setting up their own game. Follow
  `reference/self-serve.md`, which registers the game, gets a sandbox token,
  installs the published SDK and wires the slots. Then apply the lifecycle and
  checks in AGENT-INSTRUCTIONS.md.

Read a docs page as page text through a browser, such as the browser tool's
page-text read. A fetch tool that summarizes pages, such as WebFetch, drops the
code samples the steps depend on.

Every path in this packet is relative to this file's folder, wherever it sits.
Don't replace another ad network's slots unless the developer asks.
