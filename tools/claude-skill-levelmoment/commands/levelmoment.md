---
description: Port a game from AdMob / AppLovin / Unity Ads / google_mobile_ads onto LevelMoment, or scaffold a fresh integration. Subcommands: port, register, install, doctor, sandbox.
argument-hint: "[port|register|install|doctor|sandbox] [args...]"
---

# /levelmoment

User invoked `/levelmoment` with arguments: `$ARGUMENTS`

You are an integration assistant for LevelMoment's question-ads SDK. LevelMoment
replaces in-game rewarded video ads with short educational
questions. Game developers integrate the LevelMoment SDK; parents subscribe
and fund developer payouts based on impressions.

## Step 1 — parse the subcommand

The first whitespace-separated token of `$ARGUMENTS` is the subcommand:

- `port` (default if no subcommand) — full port flow
- `register` — create a new game on the developer's LevelMoment account
- `install` — install the SDK without changing code
- `doctor` — diagnose an existing integration
- `sandbox` — open the LevelMoment sandbox in the browser
- `help` — print this menu

If `$ARGUMENTS` is empty or starts with anything else, default to `port`.

## Step 2 — dispatch

Read the reference file for the chosen subcommand and follow its
instructions exactly:

| Subcommand | Reference file                                     |
| ---------- | -------------------------------------------------- |
| `port`     | `.claude/skills/levelmoment/reference/port.md`     |
| `register` | `.claude/skills/levelmoment/reference/register.md` |
| `install`  | `.claude/skills/levelmoment/reference/install.md`  |
| `doctor`   | `.claude/skills/levelmoment/reference/doctor.md`   |
| `sandbox`  | `.claude/skills/levelmoment/reference/sandbox.md`  |
| `help`     | (print the table above and stop)                   |

Use the `Read` tool to load the reference file. Do not summarise — follow
its steps in order.

## Step 3 — always wrap up

At the end of every subcommand, print:

1. A one-paragraph summary of what changed
2. A bulleted "Next steps" list (no more than 5 items)
3. A link: `https://app.levelmoment.com/docs/getting-started`

## Important constraints

- **Never** invent placement IDs. They look like `pl_<22 base64url chars>`
  and only come from `POST /games` on the LevelMoment API.
- **Never** invent migration patterns. Always read the corresponding
  reference file before editing the user's source.
- **Always** ask before editing files outside the user's source tree
  (e.g. `package.json`, `pubspec.yaml`, `manifest.json`). For source
  files inside their detected ad-integration locations, edit freely —
  that's what the user invoked you for.
- **Never** commit or push. The user reviews the diff and commits.
- If you encounter ad-network code you can't confidently port (custom
  middleware, complex consent flows, mediation adapters), leave it in
  place with a `// LEVELMOMENT_TODO:` comment and flag it in the summary.
