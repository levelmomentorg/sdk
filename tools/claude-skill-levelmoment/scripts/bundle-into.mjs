#!/usr/bin/env node
/*
 * bundle-into.mjs — copies the LevelMoment Claude Code skill source into a
 * target SDK package's `claude/` directory.
 *
 * Used by each SDK package's `prepack` script so the published tarball
 * contains the skill files. Run with the target directory as the only
 * argument:
 *
 *   node bundle-into.mjs sdk/web
 *
 * The skill source is the directory containing this script's parent
 * (i.e. tools/claude-skill-levelmoment/).
 *
 * Excludes: fixtures/, scripts/ (these are dev-only).
 */

import fs from "node:fs";
import path from "node:path";
import url from "node:url";

const __dirname = path.dirname(url.fileURLToPath(import.meta.url));
const source = path.resolve(__dirname, "..");
const target = path.resolve(process.argv[2] ?? ".", "claude");

const EXCLUDE = new Set(["fixtures", "scripts", "node_modules", ".git"]);

function copy(src, dst) {
  const stat = fs.statSync(src);
  if (stat.isDirectory()) {
    fs.mkdirSync(dst, { recursive: true });
    for (const entry of fs.readdirSync(src)) {
      if (EXCLUDE.has(entry)) continue;
      copy(path.join(src, entry), path.join(dst, entry));
    }
  } else {
    fs.mkdirSync(path.dirname(dst), { recursive: true });
    fs.copyFileSync(src, dst);
  }
}

if (fs.existsSync(target)) fs.rmSync(target, { recursive: true, force: true });
copy(source, target);

const files = [];
function walk(dir, base = "") {
  for (const entry of fs.readdirSync(dir)) {
    const full = path.join(dir, entry);
    const rel = path.join(base, entry);
    if (fs.statSync(full).isDirectory()) walk(full, rel);
    else files.push(rel);
  }
}
walk(target);

console.log(
  `bundled ${files.length} skill files into ${path.relative(process.cwd(), target) || target}/`,
);
