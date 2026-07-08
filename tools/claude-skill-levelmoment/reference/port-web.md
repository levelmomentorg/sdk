# Port — Web (AdSense / Ad Manager → LevelMoment)

You are inside the main `/levelmoment port` flow, platform = web.

## What the LevelMoment web SDK looks like

The public API the user's game will call:

```ts
import { LevelMomentWebClient } from "@levelmoment/sdk-web";

const client = LevelMomentWebClient.initialize({
  apiUrl: "https://api.levelmoment.com",
  placementId: "pl_xxxxxxxxxxxxxxxxxxxxxx",
  studentToken: new URLSearchParams(location.search).get("token") ?? "",
});

client.start(); // start background impression flush

let pendingAd = null;

function preloadNextAd() {
  client.loadAd({
    onAdLoaded: (ad) => {
      pendingAd = ad;
    },
    onAdFailedToLoad: (err) => {
      console.warn("[LevelMoment] load failed", err);
    },
  });
}

preloadNextAd();

// At the natural pause point:
function showAdBreak() {
  if (!pendingAd?.isLoaded()) return;
  pendingAd.show({
    onUserEarnedReward: (reward) => {
      // reward.amount === 1 → correct answer
      grantBonus(reward);
    },
    onAdDismissed: () => {
      pendingAd = null;
      resumeGame();
      preloadNextAd();
    },
  });
}
```

Key points to preserve from their existing code:

- Their game-pause logic stays at the same call site as their old ad
  show.
- Their bonus / reward grant logic moves into `onUserEarnedReward`.
- Their game-resume logic moves into `onAdDismissed`.

## Step 1 — remove the AdSense / Ad Manager bootstrap

In HTML files, remove these tags:

```html
<script
  async
  src="https://pagead2.googlesyndication.com/pagead/js/adsbygoogle.js"
></script>
<script
  async
  src="https://securepubads.g.doubleclick.net/tag/js/gpt.js"
></script>
```

And any inline `googletag.cmd.push(...)` blocks that set up slots.

If the user has a `googletag` setup function (often called `initAds`,
`setupAdSlots`), remove or `// LEVELMOMENT_TODO:` comment it out.

## Step 2 — add the LevelMoment init

Pick the right place. The init must run **once** before the first ad
break. Look for these in order:

1. A `main.ts` / `main.js` / `index.ts` / `index.js` entry file
2. A `BootScene.ts` (Phaser projects) or `boot.ts` (Pixi)
3. The earliest `<script>` block in `index.html`

Use the template in `.claude/skills/levelmoment/templates/web-init.ts.tmpl`
and insert it at the entry point. Substitute:

- `{{PLACEMENT_ID}}` → the placement ID from the port flow
- `{{API_URL}}` → `https://api.levelmoment.com` (or, if the user's existing
  code mentions a staging URL, ask them)

If the project is TypeScript, write the init as `.ts`. If JS-only, drop
the type annotations.

## Step 3 — replace call sites

For each file inventoried in port step 4, find the old ad calls and swap
them. The patterns to swap:

### Pattern A — googletag display ad (AdSense / Ad Manager)

```js
// BEFORE
googletag.cmd.push(function () {
  googletag.display("div-gpt-ad-xxx");
});

// AFTER
if (pendingAd?.isLoaded()) {
  pendingAd.show({
    onUserEarnedReward: (reward) => {
      /* preserve user's reward code */
    },
    onAdDismissed: () => {
      pendingAd = null;
      resumeGame();
      preloadNextAd();
    },
  });
}
```

### Pattern B — Phaser scene-launched ad break

If the user has `this.scene.launch('AdBreakScene')` followed by ad-network
calls inside `AdBreakScene`, leave the scene launch in place and rewire
the AdBreakScene's `create()` to call `pendingAd.show(...)` instead.

### Pattern C — Custom interstitial overlay

If the user has a hand-rolled `<div id="ad-overlay">` they show/hide
manually, replace the show logic with `pendingAd.show()` — LevelMoment's
overlay renders itself. Remove their old overlay div if it's only for the
ad, **but** confirm with the user first (could be reused for other UI).

## Step 4 — wire reward and dismiss

The trickiest semantic mapping. AdSense/Ad Manager don't have rewards;
they just display. LevelMoment always fires reward + dismiss callbacks.

Rules:

- If the user's existing code grants in-game currency / lives / score
  bonus on ad completion, move that into `onUserEarnedReward` (and gate
  on `reward.amount === 1` so they only get the bonus on correct
  answers).
- If the user's existing code resumes the game on ad close, move that
  into `onAdDismissed`.
- If you can't find clear "reward" or "resume" code, leave both callback
  bodies as `// LEVELMOMENT_TODO: place your reward / resume logic here` and
  flag in the summary.

## Step 5 — preload the next ad

After `onAdDismissed`, always call `preloadNextAd()` (defined in the init
template). This ensures the next break has zero network latency.

## Step 6 — manifest

Add to `package.json` `dependencies`:

```json
"@levelmoment/sdk-web": "^0.1.0"
```

Remove old SDKs (ask first):

- `@types/google-publisher-tag`
- Any custom ad library wrappers the user wrote

Bump lockfile via `npm install` (ask first if `node_modules` is large or
they have lockfile hooks).

## Step 7 — things to flag for manual review

Add `// LEVELMOMENT_TODO:` markers for:

- **GDPR / UMP consent flows** — LevelMoment uses the parent-side consent
  model, so the user's AdMob UMP / TCF code is unnecessary, but don't
  silently delete (could break compliance for other ads in the same
  app). Flag and let them decide.
- **Mediation adapters** — AppLovin, Mintegral, Pangle adapters. LevelMoment
  has no mediation. Flag.
- **Banner / native ads** — LevelMoment only ports rewarded / interstitial
  ads. If the user has banner ads (`<ins class="adsbygoogle">`), flag
  them as unsupported.
- **A/B test or analytics calls tied to ad events** — preserve them but
  warn that the events fire at slightly different points.
