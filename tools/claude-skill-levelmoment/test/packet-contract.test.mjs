import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import { createRequire } from "node:module";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";
import vm from "node:vm";

const require = createRequire(import.meta.url);
const ts = require("typescript");
const packetRoot = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  "..",
);

function read(relativePath) {
  return fs.readFileSync(path.join(packetRoot, relativePath), "utf8");
}

function loadTemplate(relativePath, sdk) {
  const { outputText } = ts.transpileModule(read(relativePath), {
    compilerOptions: {
      module: ts.ModuleKind.CommonJS,
      target: ts.ScriptTarget.ES2022,
    },
  });
  const module = { exports: {} };
  vm.runInNewContext(outputText, {
    module,
    exports: module.exports,
    require: (specifier) => {
      if (
        specifier === "@levelmoment/sdk-web" ||
        specifier === "@levelmoment/sdk-react-native"
      )
        return sdk;
      throw new Error(`Unexpected template import: ${specifier}`);
    },
  });
  return module.exports;
}

function typecheckTypeScriptTemplate(relativePath) {
  const temp = fs.mkdtempSync(path.join(packetRoot, "test", ".compile-"));
  const file = path.join(temp, path.basename(relativePath, ".tmpl"));
  try {
    fs.copyFileSync(path.join(packetRoot, relativePath), file);
    try {
      execFileSync(
        process.execPath,
        [
          require.resolve("typescript/bin/tsc"),
          "--noEmit",
          "--module",
          "ESNext",
          "--moduleResolution",
          "Bundler",
          "--target",
          "ES2022",
          "--strict",
          "--jsx",
          "react-jsx",
          "--skipLibCheck",
          file,
        ],
        { cwd: packetRoot, stdio: "pipe" },
      );
    } catch (error) {
      throw new Error(error.stdout.toString());
    }
  } finally {
    fs.rmSync(temp, { recursive: true, force: true });
  }
}

test("TypeScript templates compile against the installed SDK exports", () => {
  typecheckTypeScriptTemplate("templates/web-init.ts.tmpl");
  typecheckTypeScriptTemplate("templates/rn-use-ad-break.ts.tmpl");
  typecheckTypeScriptTemplate("templates/rn-app-root.tsx.tmpl");
});

test("web template grants once, resumes once, and creates a fresh handle", () => {
  const handles = [];
  const client = {
    loadAd(callbacks) {
      const handle = {
        callbacks: undefined,
        show(callbacks) {
          this.callbacks = callbacks;
        },
      };
      handles.push(handle);
      callbacks.onAdLoaded(handle);
    },
    ensureAccess: async () => "ready",
    checkAccess: async () => true,
  };
  const { createLevelMomentRewardedSlot } = loadTemplate(
    "templates/web-init.ts.tmpl",
    { LevelMomentWebClient: { initialize: () => client } },
  );
  const calls = [];
  const slot = createLevelMomentRewardedSlot("partner-placement", {
    pauseGame: () => calls.push("pause"),
    resumeGame: () => calls.push("resume"),
    grantChosenBonus: () => calls.push("grant"),
  });

  assert.equal(slot.show(), true);
  handles[0].callbacks.onUserEarnedReward({ amount: 0 });
  handles[0].callbacks.onUserEarnedReward({ amount: 1 });
  handles[0].callbacks.onUserEarnedReward({ amount: 1 });
  handles[0].callbacks.onAdFailedToShow();
  handles[0].callbacks.onUserEarnedReward({ amount: 1 });
  handles[0].callbacks.onAdDismissed();

  assert.deepEqual(calls, ["pause", "grant", "resume"]);
  assert.equal(handles.length, 2);
  assert.notEqual(handles[0], handles[1]);
  assert.equal(slot.show(), true);
});

test("React Native template resumes once for load failure and dismissal", () => {
  const ads = [];
  const sdk = {
    LevelMomentAd: {
      createForAdRequest(placementId, options) {
        const listeners = new Map();
        const ad = {
          placementId,
          options,
          disposed: false,
          addAdEventListener(event, listener) {
            const callbacks = listeners.get(event) ?? new Set();
            callbacks.add(listener);
            listeners.set(event, callbacks);
            return () => callbacks.delete(listener);
          },
          emit(event, value) {
            for (const listener of listeners.get(event) ?? []) listener(value);
          },
          load() {
            this.emit("loaded");
          },
          show() {},
          dispose() {
            this.disposed = true;
          },
        };
        ads.push(ad);
        return ad;
      },
    },
    ensureAccess: async () => "ready",
    checkAccess: async () => true,
  };
  const { showLevelMomentRewardedSlot } = loadTemplate(
    "templates/rn-use-ad-break.ts.tmpl",
    sdk,
  );
  const calls = [];
  const hooks = {
    pauseGame: () => calls.push("pause"),
    resumeGame: () => calls.push("resume"),
    grantChosenBonus: () => calls.push("grant"),
    prepareNextSlot: () => calls.push("prepare"),
  };

  showLevelMomentRewardedSlot("partner-placement", hooks);
  ads[0].emit("earnedReward", { amount: 1 });
  ads[0].emit("error", { code: "network_error" });
  ads[0].emit("closed");
  ads[0].emit("earnedReward", { amount: 1 });

  assert.deepEqual(calls, ["pause", "grant", "resume", "prepare"]);
  assert.equal(ads[0].disposed, true);
  assert.equal(Object.keys(ads[0].options).length, 0);

  showLevelMomentRewardedSlot("partner-placement", hooks);
  ads[1].emit("error", { code: "load_failed" });
  assert.deepEqual(calls, [
    "pause",
    "grant",
    "resume",
    "prepare",
    "pause",
    "resume",
    "prepare",
  ]);
  assert.notEqual(ads[0], ads[1]);
});

test("handoff uses supplied immutable inputs without a fabricated source", () => {
  const handoff = JSON.parse(read("handoff.example.json"));

  assert.equal(handoff.sdk.version, "0.2.0");
  assert.equal(handoff.sdk.platformArtifact.artifactPath, "");
  assert.equal(handoff.sdk.platformArtifact.sha256, "");
  assert.equal(handoff.placement.placementId, "");
  assert.equal(handoff.sdk.nativeSource.immutableRef, "");
  assert.equal("$schema" in handoff, false);
});

test("the bundled packet retains its direct entry points and linked references", () => {
  const target = fs.mkdtempSync(path.join(os.tmpdir(), "levelmoment-packet-"));
  try {
    execFileSync(process.execPath, [
      path.join(packetRoot, "scripts", "bundle-into.mjs"),
      target,
    ]);
    const bundle = path.join(target, "claude");
    for (const relativePath of [
      "AGENT-INSTRUCTIONS.md",
      "handoff.example.json",
      "reference/partner-handoff.md",
      "templates/web-init.ts.tmpl",
      "templates/rn-use-ad-break.ts.tmpl",
    ]) {
      assert.equal(fs.existsSync(path.join(bundle, relativePath)), true);
    }
    assert.match(
      fs.readFileSync(path.join(bundle, "AGENT-INSTRUCTIONS.md"), "utf8"),
      /handoff\.example\.json/,
    );
  } finally {
    fs.rmSync(target, { recursive: true, force: true });
  }
});

test("the installed command resolves every instruction in the partner project", () => {
  const project = fs.mkdtempSync(
    path.join(os.tmpdir(), "levelmoment-install-"),
  );
  try {
    execFileSync(process.execPath, [
      path.join(packetRoot, "scripts", "bundle-into.mjs"),
      path.join(project, "sdk"),
    ]);
    execFileSync(
      process.execPath,
      [
        path.join(project, "sdk", "claude", "bin", "install-skill.cjs"),
        "--quiet",
      ],
      { cwd: project },
    );
    const command = fs.readFileSync(
      path.join(project, ".claude", "commands", "levelmoment.md"),
      "utf8",
    );
    const references = [
      ...command.matchAll(/`(\.claude\/skills\/levelmoment\/[^`]+)`/g),
    ];
    assert.equal(references.length, 5);
    for (const [, reference] of references) {
      assert.ok(fs.existsSync(path.join(project, reference)), reference);
    }
  } finally {
    fs.rmSync(project, { recursive: true, force: true });
  }
});
