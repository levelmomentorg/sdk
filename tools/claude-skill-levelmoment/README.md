# `/levelmoment` — Claude Code skill for LevelMoment integration

A Claude Code skill that ports your game from an existing ad SDK (AdMob,
AppLovin, Unity Ads, google_mobile_ads, AdSense / Ad Manager) onto LevelMoment,
or scaffolds a fresh integration if you don't have ads yet.

## What it does

In a single `/levelmoment port` invocation:

1. **Detects** which ad SDK your project uses (web, React Native, Flutter,
   Unity).
2. **Inventories** every load / show / reward / dismiss call site.
3. **Asks** for your LevelMoment placement ID — or fetches one automatically by
   calling the LevelMoment API on your behalf.
4. **Installs** the LevelMoment SDK in your manifest (`package.json`,
   `pubspec.yaml`, `Packages/manifest.json`).
5. **Edits** your source: swaps imports, replaces `RewardedAd.load()` /
   `RewardedAd.show()` calls with the LevelMoment equivalents, wires the
   modal/overlay component (RN), keeps your reward / dismiss handlers
   intact.
6. **Summarises** the diff and prints next steps.

Optional follow-up commands:

- `/levelmoment register <name>` — register a new game and get a placement ID
- `/levelmoment install [platform]` — install the SDK without changing code
- `/levelmoment doctor` — diagnose an existing integration
- `/levelmoment sandbox` — open the LevelMoment sandbox in your browser

## Install

### From the LevelMoment SDK packages (recommended)

```bash
# Web
npx @levelmoment/sdk-web install-skill

# React Native
npx @levelmoment/sdk-react-native install-skill
```

This copies the skill files into `.claude/skills/levelmoment/` and the slash
command into `.claude/commands/levelmoment.md` in your repo.

### Manual install (Flutter, Unity, or no Node)

```bash
mkdir -p .claude/skills/levelmoment .claude/commands
curl -fsSL https://levelmoment.com/claude-skill.tar.gz | \
  tar -xzC .claude/skills/levelmoment
cp .claude/skills/levelmoment/commands/levelmoment.md .claude/commands/levelmoment.md
```

## Usage

In any Claude Code session inside your game repo:

```
/levelmoment
```

Claude will auto-detect what's needed and run the appropriate sub-command.
Or be explicit:

```
/levelmoment port
/levelmoment register My Cool Game
/levelmoment install
/levelmoment doctor
```

## Supported platforms

| Platform     | Ports from                                     | Status |
| ------------ | ---------------------------------------------- | ------ |
| Web / HTML5  | AdSense, Google Ad Manager (`googletag`)       | ✅ v1  |
| React Native | `react-native-google-mobile-ads`, AppLovin Max | ✅ v1  |
| Flutter      | `google_mobile_ads`, `applovin_max`            | ✅ v1  |
| Unity        | Unity Ads SDK, AdMob Unity plugin              | ✅ v1  |

## Privacy

The skill runs entirely locally and never contacts LevelMoment's API on your
behalf. `/levelmoment register` walks you through creating the game yourself in
the developer portal (`https://app.levelmoment.com/games`) and pastes the
placement ID you copy back into your project. The skill stores no credentials
or tokens on disk.

## Reporting issues

The skill is exercised against fixture projects in
`fixtures/`. If the skill misbehaves on your codebase, open an issue at
https://github.com/levelmomentorg/sdk/issues with the before-and-after diff.
