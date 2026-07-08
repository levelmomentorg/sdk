#!/usr/bin/env node
/*
 * install-skill — copies the LevelMoment Claude Code skill into a user's repo.
 *
 * Run as:
 *   npx @levelmoment/sdk-web install-skill
 *   npx @levelmoment/sdk-react-native install-skill
 *
 * Or directly:
 *   node node_modules/@levelmoment/sdk-web/bin/install-skill.js
 *
 * The skill source ships inside the SDK package under `claude/`.
 * This script copies it to `.claude/skills/levelmoment/` and the slash
 * command to `.claude/commands/levelmoment.md` in the current working dir.
 */

"use strict";

const fs = require("node:fs");
const path = require("node:path");

const args = process.argv.slice(2);
const force = args.includes("--force") || args.includes("-f");
const dryRun = args.includes("--dry-run");
const quiet = args.includes("--quiet") || args.includes("-q");

const log = (...m) => !quiet && console.log(...m);
const warn = (...m) => console.warn(...m);
const die = (msg, code = 1) => {
  console.error(`error: ${msg}`);
  process.exit(code);
};

// Source: the skill directory containing this script. In both dev
// (tools/claude-skill-levelmoment/) and published SDK layout (<pkg>/claude/),
// SKILL.md lives one directory up from bin/.
function findSource() {
  const source = path.resolve(__dirname, "..");
  if (!fs.existsSync(path.join(source, "SKILL.md"))) {
    die(`could not locate the skill source. Expected SKILL.md at ${source}.`);
  }
  return source;
}

const source = findSource();
const cwd = process.cwd();
const targetSkill = path.join(cwd, ".claude", "skills", "levelmoment");
const targetCommand = path.join(cwd, ".claude", "commands", "levelmoment.md");

function copyRecursive(src, dst) {
  const stat = fs.statSync(src);
  if (stat.isDirectory()) {
    if (!dryRun) fs.mkdirSync(dst, { recursive: true });
    for (const entry of fs.readdirSync(src)) {
      copyRecursive(path.join(src, entry), path.join(dst, entry));
    }
  } else {
    if (fs.existsSync(dst) && !force) {
      warn(`  skip (exists): ${path.relative(cwd, dst)}`);
      return;
    }
    if (!dryRun) {
      fs.mkdirSync(path.dirname(dst), { recursive: true });
      fs.copyFileSync(src, dst);
    }
    log(`  ${dryRun ? "would write" : "wrote"}: ${path.relative(cwd, dst)}`);
  }
}

log("Installing LevelMoment Claude Code skill...");
log(`  source: ${source}`);
log(
  `  target: ${path.relative(cwd, targetSkill) || ".claude/skills/levelmoment"}`,
);
log("");

// 1. Copy the skill body (everything under source except commands/ which
//    we route to .claude/commands/ separately, and bin/ which the user
//    doesn't need at runtime).
for (const entry of fs.readdirSync(source)) {
  if (entry === "commands" || entry === "bin" || entry === "fixtures") continue;
  copyRecursive(path.join(source, entry), path.join(targetSkill, entry));
}

// 2. Copy the slash command.
copyRecursive(path.join(source, "commands", "levelmoment.md"), targetCommand);

log("");
log("Done. In your next Claude Code session, you can use:");
log("  /levelmoment              # port or scaffold");
log("  /levelmoment register     # create a game");
log("  /levelmoment install      # install SDK only");
log("  /levelmoment doctor       # diagnose");
log("  /levelmoment sandbox      # open preview");
log("");
log(
  "If you don't see the command, restart Claude Code so it picks up the new files.",
);
