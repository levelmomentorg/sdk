# `/levelmoment port` — main port flow

Follow these steps in order. Do not skip ahead.

## Step 1 — detect the platform

Run these checks in parallel (single message, multiple Bash/Read calls):

### Web detection signals

- `package.json` exists and has any of: `phaser`, `pixi.js`, `cocos2d-js`,
  `playcanvas`, `babylonjs` in `dependencies`
- Any `.html` file in repo contains `googletag.cmd.push` or
  `pagead/show_ads.js` or `adsbygoogle`
- Any `.js` / `.ts` / `.tsx` file references `googletag.display(` or
  `googletag.pubads()` or `adsbygoogle`

### React Native detection signals

- `package.json` `dependencies` or `devDependencies` includes
  `react-native-google-mobile-ads`, `react-native-applovin-max`,
  `@react-native-firebase/admob`, or `react-native`
- Any `.ts` / `.tsx` / `.js` / `.jsx` file imports from any of those
  packages or references `RewardedAd.createForAdRequest(`

### Flutter detection signals

- `pubspec.yaml` exists and lists `flutter:` under `dependencies`
- `pubspec.yaml` includes `google_mobile_ads:` or `applovin_max:` or
  `unity_ads:`
- Any `.dart` file references `RewardedAd.load(` or `MobileAds.instance`

### Unity detection signals

- `Packages/manifest.json` exists (Unity project root marker)
- `Packages/manifest.json` includes `com.google.ads.mobile`,
  `com.unity.ads`, or `com.applovin.max`
- Any `.cs` file references `using GoogleMobileAds.Api;`,
  `using UnityEngine.Advertisements;`, or `RewardedAd.Load(`

## Step 2 — pick the platform

The detection rules are ordered: if multiple platforms match (rare — a
monorepo), ask the user which to port.

If **no platform is detected**:

1. Tell the user: "I didn't find an existing ad SDK in this project."
2. Ask via `AskUserQuestion` whether they want to:
   - "Scaffold a fresh LevelMoment integration anyway" (recommended)
   - "Cancel — nothing to port"
3. If they choose scaffold, ask which platform their game uses (web,
   React Native, Flutter, Unity), then read the corresponding
   `greenfield-<platform>.md` reference file and follow it.

## Step 3 — collect the placement ID

Ask the user via `AskUserQuestion`:

> "I need an LevelMoment placement ID to wire into your game. How would you
> like to provide it?"
>
> - **Auto-register** (recommended) — I'll create a game on your LevelMoment
>   account and use the new placement ID
> - **Paste an existing one** — I'll ask you to paste it
> - **Use a placeholder** — I'll insert `pl_PLACEHOLDER` and you fill it
>   in later

If they choose **auto-register**, follow `register.md` to fetch the ID,
then continue.

If they choose **paste**, prompt for the placement ID and validate format:
must match `^pl_[A-Za-z0-9_-]{22}$`. If invalid, ask again.

If they choose **placeholder**, use the literal string `pl_PLACEHOLDER`.

Store the chosen placement ID in a variable for the rest of this run.

## Step 4 — inventory the call sites

Use Grep (via Bash) to find every file that touches the existing ad SDK.
Save the list — you'll re-read these files in the port step.

Search patterns by platform (use the union of these in your grep):

- **Web:** `googletag\.|adsbygoogle|RewardedAd\.|interstitialAd|rewardedVideo`
- **React Native:** `react-native-google-mobile-ads|RewardedAd|InterstitialAd|RewardedAdEventType|AdEventType|requestNonPersonalizedAdsOnly`
- **Flutter:** `google_mobile_ads|MobileAds\.instance|RewardedAd|InterstitialAd|RewardedAdLoadCallback|FullScreenContentCallback`
- **Unity:** `UnityEngine\.Advertisements|GoogleMobileAds|UnityAds\.|RewardedAd\.Load|IUnityAdsLoadListener|IUnityAdsShowListener`

Read every file in the result list. Identify the **logical call sites**:

- Initialization (`MobileAds.instance.initialize()`, etc.)
- Load (`RewardedAd.load(...)`)
- Show (`rewardedAd.show()`, `.fullScreenContentCallback = ...`)
- Reward callback (`onUserEarnedReward`, `EARNED_REWARD` listener, etc.)
- Dismiss callback (`onAdDismissedFullScreenContent`, `CLOSED` listener)

Make a private mental map: file path → call sites → user's surrounding
context (the lines around each call). You'll need this to preserve their
game-pause / game-resume / reward-grant logic during the port.

## Step 5 — read the platform-specific port instructions

Read the appropriate file:

- Web → `.claude/skills/levelmoment/reference/port-web.md`
- React Native → `.claude/skills/levelmoment/reference/port-react-native.md`
- Flutter → `.claude/skills/levelmoment/reference/port-flutter.md`
- Unity → `.claude/skills/levelmoment/reference/port-unity.md`

Follow its steps exactly. Each platform file contains:

- The exact manifest changes (add / remove deps)
- The exact import swaps
- The exact load / show / event handler swaps
- A list of "things to preserve from the user's code" (game pause/resume,
  reward grants, etc.)
- A list of "things to flag for manual review" (consent UMP, mediation)

## Step 6 — update the manifest

After the per-platform port instructions, ensure the LevelMoment SDK is
listed in the manifest:

- **Web (npm):** `npm install @levelmoment/sdk-web` (run via Bash, ask the
  user first if you see existing `node_modules`)
- **React Native:** `npm install @levelmoment/sdk-react-native react-native-webview`
- **Flutter:** add `levelmoment_ads:` to `pubspec.yaml` `dependencies:` block
  (don't run `flutter pub get` — let the user do it)
- **Unity:** add `com.levelmoment.sdk` to `Packages/manifest.json`

Remove the old ad SDK if the user confirmed in Step 3a (ask if not yet
confirmed):

> "Want me to remove the old ad SDK (`react-native-google-mobile-ads`) at
> the same time, or keep it for now?"

## Step 7 — verify the edits compile

Read the final edited source files back to spot-check. Do **not** run the
user's test/build script automatically — they may have non-obvious side
effects. Instead, suggest the command in the summary:

> "Run `npm run typecheck && npm run build` (or your usual build) to
> verify."

## Step 8 — write the summary

End the run with this exact structure:

```markdown
## Port complete

**Platform:** <web | react-native | flutter | unity>
**Placement ID:** <the placement ID, or `pl_PLACEHOLDER`>
**Files edited:** <count> (`src/Foo.ts`, `src/Bar.tsx`, ...)
**Old SDK:** <removed | kept alongside LevelMoment>

### What I changed

- <one bullet per category of change>

### What I couldn't do automatically

- <list each `// LEVELMOMENT_TODO:` marker with file:line>

### Next steps

1. Run your build / typecheck: `<exact command for their platform>`
2. Test locally with a sandbox token (see https://app.levelmoment.com/sandbox)
3. Sign in to https://app.levelmoment.com/dashboard to verify the game appears
4. Deploy and integrate the parent-issued student token at runtime

Full integration docs: https://app.levelmoment.com/docs/getting-started
```
