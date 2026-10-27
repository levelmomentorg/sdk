---
name: levelmoment
description: Set up Level Moment learning breaks in a game, from registering it to verified sandbox breaks, integrate a supplied handoff manifest, or move a working sandbox integration to production. Use when a developer asks to add, set up, port, diagnose, ship or go live with Level Moment in a game.
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
- **No handoff:** the developer is setting up their own game. This is also
  the path for every Unity game without a handoff, with or without AppLovin
  MAX, and for a game with its own break function and no ad SDK. Follow
  `reference/self-serve.md`, which registers the game, gets a sandbox token,
  installs the published SDK and wires the slots. Then apply the lifecycle and
  checks in AGENT-INSTRUCTIONS.md.
- **The game already shows Level Moment breaks through an SDK older than
  0.3.0** (an old commit pin, a mock mode, a `format` choice instead of slot
  declarations): follow `reference/upgrade.md`, which moves it to 0.4.3 and
  through the same sandbox checks.
- **The integration already passes its sandbox checks and the developer wants
  to ship it:** follow `reference/deploy.md`, which removes the sandbox token
  from release builds, checks them, and prepares the review email. The
  developer sends that email, runs the device test and uploads the build.

Read each docs page with a browser tool that returns the page's full text. A
fetch tool that summarizes pages, such as WebFetch, drops the
code samples the steps depend on.

Every path in this packet is relative to this file's folder, wherever it sits.
Don't replace another ad network's slots unless the developer asks.
