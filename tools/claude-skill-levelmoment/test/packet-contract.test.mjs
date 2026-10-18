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
  typecheckTypeScriptTemplate("templates/web-promise-seam.ts.tmpl");
});

test("the promise template passes its own unit-test template", () => {
  const temp = fs.mkdtempSync(path.join(packetRoot, "test", ".vitest-"));
  try {
    fs.copyFileSync(
      path.join(packetRoot, "templates/web-promise-seam.ts.tmpl"),
      path.join(temp, "levelmoment-breaks.ts"),
    );
    fs.copyFileSync(
      path.join(packetRoot, "templates/web-promise-seam.test.ts.tmpl"),
      path.join(temp, "levelmoment-breaks.test.ts"),
    );
    const vitest = path.join(
      path.dirname(require.resolve("vitest/package.json")),
      "vitest.mjs",
    );
    try {
      execFileSync(process.execPath, [vitest, "run", "--root", temp], {
        cwd: packetRoot,
        stdio: "pipe",
      });
    } catch (error) {
      throw new Error(`${error.stdout}${error.stderr}`);
    }
  } finally {
    fs.rmSync(temp, { recursive: true, force: true });
  }
});

test("self-serve links every setup step to the public docs", () => {
  const selfServe = read("reference/self-serve.md");
  for (const page of [
    "getting-started",
    "testing",
    "own-break-interface",
    "web-views",
    "errors#troubleshooting",
    "going-live",
  ]) {
    assert.match(
      selfServe,
      new RegExp(`https://levelmoment\\.com/docs/${page}`),
    );
  }
});

test("upgrade links each step to the public docs and is routed", () => {
  const upgrade = read("reference/upgrade.md");
  for (const page of [
    "porting#upgrade",
    "getting-started#slot-size",
    "getting-started#integrate",
    "testing",
  ]) {
    assert.match(upgrade, new RegExp(`https://levelmoment\\.com/docs/${page}`));
  }
  assert.match(read("commands/levelmoment.md"), /reference\/upgrade\.md/);
  assert.match(read("SKILL.md"), /reference\/upgrade\.md/);
});

test("deploy links every going-live step and hands human steps back", () => {
  const deploy = read("reference/deploy.md");
  for (const page of [
    "going-live",
    "going-live#token",
    "going-live#disclosures",
    "going-live#testing-track",
    "going-live#device-test",
    "going-live#review",
    "going-live#verify",
    "testing#verify",
    "testing#production-build",
    "data-safety",
    "web-views",
  ]) {
    assert.match(deploy, new RegExp(`https://levelmoment\\.com/docs/${page}`));
  }
  const stops = deploy.slice(deploy.indexOf("## Stop conditions"));
  for (const step of [
    /sending the review email/,
    /uploading a test build to TestFlight/,
    /submitting for store review or publishing/,
    /accepting any terms/,
    /signing in/,
    /Level Moment account with a plan or a physical device/,
  ]) {
    assert.match(stops, step);
  }
  assert.match(read("commands/levelmoment.md"), /reference\/deploy\.md/);
  assert.match(read("SKILL.md"), /reference\/deploy\.md/);
});

test("native templates pass the terminal reward ID to game-owned grants", () => {
  const flutter = read("templates/flutter-ad-break.dart.tmpl");
  const unity = read("templates/unity-AdsManager.cs.tmpl");
  assert.match(flutter, /hooks\.grantChosenBonus\(reward\.rewardId\)/);
  assert.match(unity, /_grantChosenBonus\(reward\.RewardId\)/);
  assert.doesNotMatch(flutter, /reward\.amount/);
  assert.doesNotMatch(unity, /amount\s*==\s*1/);
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
    grantChosenBonus: (rewardId) => calls.push(`grant:${rewardId}`),
  });

  assert.equal(slot.show(), true);
  handles[0].callbacks.onUserEarnedReward({ rewardId: "break-1" });
  handles[0].callbacks.onUserEarnedReward({ rewardId: "break-1" });
  handles[0].callbacks.onAdFailedToShow();
  handles[0].callbacks.onUserEarnedReward({ rewardId: "break-1" });
  handles[0].callbacks.onAdDismissed();

  assert.deepEqual(calls, ["pause", "grant:break-1", "resume"]);
  assert.equal(handles.length, 2);
  assert.notEqual(handles[0], handles[1]);
  assert.equal(slot.show(), true);
  handles[1].callbacks.onAdDismissed();
  assert.deepEqual(calls.slice(-2), ["pause", "resume"]);
});

test("promise template opens access, resolves once, and guards a second tap", async () => {
  const handles = [];
  let access = "ready";
  const client = {
    loadAd(callbacks) {
      const handle = {
        callbacks: undefined,
        show(callbacks) {
          this.callbacks = callbacks;
        },
      };
      handles.push(handle);
      queueMicrotask(() => callbacks.onAdLoaded(handle));
    },
    ensureAccess: async () => access,
  };
  const { createLevelMomentBreaks } = loadTemplate(
    "templates/web-promise-seam.ts.tmpl",
    { LevelMomentWebClient: { initialize: () => client } },
  );
  const breaks = createLevelMomentBreaks("partner-placement");
  const slot = { id: "continue", targetDurationSeconds: 30, rewardAmount: 1 };
  const settle = () => new Promise((resolve) => setTimeout(resolve, 0));

  // The first break opens even without prepare().
  const first = breaks.showBreak(slot);
  assert.equal(await breaks.showBreak(slot), "none");
  await settle();
  handles[0].callbacks.onUserEarnedReward({ rewardId: "break-1" });
  handles[0].callbacks.onAdDismissed();
  handles[0].callbacks.onAdFailedToShow();
  assert.equal(await first, "finished");

  const second = breaks.showBreak(slot);
  await settle();
  assert.notEqual(handles[1], handles[0]);
  handles[1].callbacks.onAdDismissed();
  assert.equal(await second, "ended");

  access = "canceled";
  assert.equal(await breaks.showBreak(slot), "none");
  assert.equal(handles[2].callbacks, undefined);

  // The guard clears after "none", so the next tap opens the waiting handle.
  access = "ready";
  const third = breaks.showBreak(slot);
  await settle();
  handles[2].callbacks.onAdDismissed();
  assert.equal(await third, "ended");
});

test("promise template resolves none on a load failure, then loads afresh", async () => {
  let fail = true;
  const handles = [];
  const client = {
    loadAd(callbacks) {
      if (fail) {
        queueMicrotask(() => callbacks.onAdFailedToLoad({ code: "unknown" }));
        return;
      }
      const handle = {
        callbacks: undefined,
        show(callbacks) {
          this.callbacks = callbacks;
        },
      };
      handles.push(handle);
      queueMicrotask(() => callbacks.onAdLoaded(handle));
    },
    ensureAccess: async () => "ready",
  };
  const { createLevelMomentBreaks } = loadTemplate(
    "templates/web-promise-seam.ts.tmpl",
    { LevelMomentWebClient: { initialize: () => client } },
  );
  const breaks = createLevelMomentBreaks("partner-placement");
  const slot = { id: "continue", targetDurationSeconds: 20, rewardAmount: 1 };
  breaks.prepare(slot);
  fail = false;
  assert.equal(await breaks.showBreak(slot), "none");
  const next = breaks.showBreak(slot);
  await new Promise((resolve) => setTimeout(resolve, 0));
  assert.equal(handles.length, 1);
  handles[0].callbacks.onAdDismissed();
  assert.equal(await next, "ended");
});

test("promise template keys one client per moment and declares its reward", () => {
  const configs = [];
  const { createLevelMomentBreaks } = loadTemplate(
    "templates/web-promise-seam.ts.tmpl",
    {
      LevelMomentWebClient: {
        initialize: (config) => {
          configs.push(config);
          return { loadAd() {}, ensureAccess: async () => "ready" };
        },
      },
    },
  );
  const breaks = createLevelMomentBreaks("partner-placement");
  const cont = { id: "continue", targetDurationSeconds: 30, rewardAmount: 1 };
  const coins = { id: "coins", targetDurationSeconds: 30, rewardAmount: 100 };
  const between = { id: "between", targetDurationSeconds: 60 };
  const bonus = { id: "bonus", targetDurationSeconds: 20, rewardAmount: 0 };
  breaks.prepare(cont);
  breaks.prepare(coins);
  breaks.prepare(between);
  breaks.prepare(bonus);
  breaks.prepare(cont);

  // Two moments with the same duration still get a client each.
  assert.equal(configs.length, 4);
  assert.deepEqual(
    // The template runs in its own realm, so compare plain copies.
    JSON.parse(JSON.stringify(configs.map((config) => config.slot))),
    [
      { adType: "rewarded", targetDurationSeconds: 30, rewardAmount: 1 },
      { adType: "rewarded", targetDurationSeconds: 30, rewardAmount: 100 },
      { adType: "interstitial", targetDurationSeconds: 60 },
      // A reward of 0 grants nothing, so it is an interstitial too.
      { adType: "interstitial", targetDurationSeconds: 20 },
    ],
  );
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
    grantChosenBonus: (rewardId) => calls.push(`grant:${rewardId}`),
    prepareNextSlot: () => calls.push("prepare"),
  };

  showLevelMomentRewardedSlot("partner-placement", hooks);
  ads[0].emit("earnedReward", { rewardId: "break-2" });
  ads[0].emit("error", { code: "network_error" });
  ads[0].emit("closed");
  ads[0].emit("earnedReward", { rewardId: "break-2" });

  assert.deepEqual(calls, ["pause", "grant:break-2", "resume", "prepare"]);
  assert.equal(ads[0].disposed, true);
  assert.equal(Object.keys(ads[0].options).length, 0);

  showLevelMomentRewardedSlot("partner-placement", hooks);
  ads[1].emit("error", { code: "load_failed" });
  assert.deepEqual(calls, [
    "pause",
    "grant:break-2",
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

  assert.equal(handoff.sdk.version, "0.3.1");
  assert.equal(handoff.sdk.platformArtifact.artifactPath, "");
  assert.equal(handoff.sdk.platformArtifact.sha256, "");
  assert.equal(handoff.placement.placementId, "");
  assert.equal(handoff.sdk.nativeSource.immutableRef, "");
  assert.equal(handoff.reward.grantOn, "terminalReward");
  assert.equal(handoff.reward.dedupeByRewardId, true);
  assert.equal("firstCorrectOnly" in handoff.reward, false);
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
    assert.equal(references.length, 8);
    for (const [, reference] of references) {
      assert.ok(fs.existsSync(path.join(project, reference)), reference);
    }
  } finally {
    fs.rmSync(project, { recursive: true, force: true });
  }
});
