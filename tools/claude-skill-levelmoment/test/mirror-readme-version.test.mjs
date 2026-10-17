import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";

// The front-page files sit in deploy/sdk-mirror here and at the repository
// root where the packet is published alongside them.
const repoRoot = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  "..",
  "..",
  "..",
);
const stagedDir = path.join(repoRoot, "deploy", "sdk-mirror");
const mirrorDir = fs.existsSync(stagedDir) ? stagedDir : repoRoot;

const read = (p) => fs.readFileSync(p, "utf8");
const pkgVersion = (name) =>
  JSON.parse(read(path.join(repoRoot, "sdk", name, "package.json"))).version;

test("published SDK packages share one version", () => {
  const v = pkgVersion("core");
  for (const name of ["web", "react-native", "unity"]) {
    assert.equal(pkgVersion(name), v, `sdk/${name} version`);
  }
  const pubspec = read(path.join(repoRoot, "sdk", "flutter", "pubspec.yaml"));
  assert.equal(pubspec.match(/^version:\s*(\S+)\s*$/m)?.[1], v, "pubspec");
});

test("mirror front-page files name the current SDK version", () => {
  const v = pkgVersion("core");
  assert.ok(fs.existsSync(path.join(mirrorDir, "llms.txt")), "mirror root");
  for (const file of ["README.md", "AGENTS.md", "llms.txt"]) {
    const text = read(path.join(mirrorDir, file));
    assert.doesNotMatch(text, /preview/i, `${file} mentions a preview`);
    const named = new Set(
      [...text.matchAll(/\bv?(\d+\.\d+\.\d+(?:-[\w.]+)?)\b/g)].map((m) => m[1]),
    );
    assert.ok(named.has(v), `${file} does not name ${v}`);
    assert.deepEqual(
      [...named].filter((n) => n !== v),
      [],
      `${file} names another version`,
    );
  }
  const readme = read(path.join(mirrorDir, "README.md"));
  assert.ok(readme.includes(`ref: sdk-v${v}`));
  assert.ok(readme.includes(`#sdk-v${v}"`));
  assert.ok(readme.includes(`@levelmoment/sdk-web@${v}`));
  assert.ok(readme.includes(`@levelmoment/sdk-react-native@${v}`));
});
