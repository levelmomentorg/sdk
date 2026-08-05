---
name: levelmoment
description: Use when the user wants to integrate LevelMoment's question-ads SDK, port a game from an existing ad SDK (AdMob, AppLovin, Unity Ads, google_mobile_ads, AdSense, Ad Manager) onto LevelMoment, register a game on their LevelMoment developer account, install the LevelMoment SDK, or diagnose an LevelMoment integration. Also use when the user mentions replacing in-game ads with educational questions, or asks how to migrate a rewarded-ad SDK call to LevelMoment.
---

# LevelMoment integration skill

This skill ports games onto LevelMoment's question-ads SDK and scaffolds
fresh integrations. It backs the `/levelmoment` slash command but also
auto-triggers when the user describes the task in their own words.

## When to invoke

Trigger this skill when the user's request matches any of:

- "Port my game from AdMob / AppLovin / Unity Ads / google_mobile_ads /
  AdSense / Ad Manager to LevelMoment"
- "Replace [ad SDK] with LevelMoment"
- "Add LevelMoment to my game"
- "Set up LevelMoment on this project"
- "Register this game on LevelMoment" / "Get an LevelMoment placement ID"
- "Diagnose my LevelMoment integration" / "LevelMoment isn't working"
- "Show me what an LevelMoment ad break looks like" (→ sandbox)

## How to invoke

Tell the user you're using the LevelMoment skill, then read
`.claude/commands/levelmoment.md` and follow its instructions. Pass the
user's natural-language request as the effective sub-command:

- Mentions of porting / migrating / replacing → `port`
- "Register" / "get a placement ID" / "create a game" → `register`
- "Install the SDK" / "add levelmoment to package.json" → `install`
- "Diagnose" / "doctor" / "isn't working" → `doctor`
- "Sandbox" / "preview" / "show me a question break" → `sandbox`

If the request is ambiguous, default to `port` — it handles greenfield
projects (no existing ad SDK) by asking the user interactively.

## Boundaries

- **Never** invent placement IDs. They look like `pl_<22 base64url chars>`
  and only come from the LevelMoment API.
- **Never** commit or push the user's changes. They review the diff.
- **Never** edit files outside the user's source tree without asking
  (e.g. `package.json`, `pubspec.yaml`, `Packages/manifest.json`).
- If detection finds no existing ad SDK, ask the user interactively
  before scaffolding — don't silently add code to a project that didn't
  request it.

## Reference docs

All step-by-step instructions live in
`.claude/skills/levelmoment/reference/*.md`:

- `port.md` — main port flow + detection
- `port-web.md`, `port-react-native.md`, `port-flutter.md`,
  `port-unity.md` — per-platform port instructions
- `greenfield-{web,react-native,flutter,unity}.md` — scaffold from
  scratch
- `register.md` — create a game via API
- `install.md` — install SDK only
- `doctor.md` — diagnose integration
- `sandbox.md` — open sandbox in browser

Templates (the code the skill writes) live in
`.claude/skills/levelmoment/templates/`.
