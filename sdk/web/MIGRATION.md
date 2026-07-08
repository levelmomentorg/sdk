# Migrating from Google AdSense / Ad Manager (web) to LevelMoment

For web and HTML5 games that currently show display or interstitial ads.

## 1. Replace the script tag

```html
<!-- REMOVE: -->
<script
  async
  src="https://pagead2.googlesyndication.com/pagead/js/adsbygoogle.js"
></script>

<!-- ADD: -->
<script type="module">
  import {
    LevelMomentWebClient,
    LevelMomentAd,
  } from "https://cdn.levelmoment.com/sdk-web/latest/index.js";
  window.LevelMoment = { LevelMomentWebClient, LevelMomentAd };
</script>
```

Or via npm:

```bash
npm install @levelmoment/sdk-web
```

## 2. Initialize once at game start

```ts
import { LevelMomentWebClient, LevelMomentAd } from "@levelmoment/sdk-web";

const client = LevelMomentWebClient.initialize({
  apiUrl: "https://api.levelmoment.com",
  placementId: "your-game-id", // from LevelMoment developer portal
  studentToken: new URLSearchParams(location.search).get("token") ?? "",
  // breakUrl: "https://app.levelmoment.com/break", // only if NOT same-origin — see below
});

client.start(); // kept-for-compat no-op (the hosted page owns impressions)
```

### One behavioral migration: the break URL

LevelMoment no longer ships a DOM renderer — `show()` opens the hosted `/break`
page in a fullscreen iframe (see [ADR-001](../../docs/ADR-001-webview-rendering.md)).

- **Default:** the break URL is `window.location.origin + "/break"`. If your
  game is served from the **same origin** as the hosted LevelMoment Next app, you
  don't need to do anything.
- **Cross-origin:** if your game is served from a different origin, pass
  `breakUrl` explicitly in
  `initialize({ ..., breakUrl: "https://app.levelmoment.com/break" })`. The SDK
  validates that postMessage events come from this origin.

## 3. Preload while the game runs

```ts
let pendingAd = null;

function preloadNextAd() {
  client.loadAd({
    onAdLoaded: (ad) => {
      pendingAd = ad;
    },
    onAdFailedToLoad: () => {},
  });
}

preloadNextAd(); // call at game start
```

## 4. Show at the natural pause point

```ts
// At level complete, lives lost, etc.:
if (pendingAd?.isLoaded()) {
  pendingAd.show({
    onUserEarnedReward: (reward) => grantBonus(reward), // correct answer
    onAdDismissed: () => {
      resumeGame();
      preloadNextAd(); // get the next break ready
    },
  });
}
```

The hosted `/break` page renders all question UI and owns the impression
queue — there is no `onAdShowed` / `pendingAd.question` / `client.queue` to
handle.

## 5. Robustness: the load-timeout watchdog + dispose() escape hatch

`show()` mounts a fullscreen iframe and a `message` listener that are normally
torn down when the hosted page posts a terminal `dismissed`/`error`. If the
hosted page crashes, navigates away, or the network drops it _before it ever
comes up_, those resources would otherwise leak and the iframe would
permanently cover your game.

- **Load-timeout watchdog (automatic).** If the hosted `/break` page does not
  post `ready` within **15 seconds** of `show()`, the SDK tears down the
  iframe + listener and calls `onAdDismissed` so your game resumes cleanly —
  exactly as if the break had been dismissed normally. Override the window
  with `breakLoadTimeoutMs` in `initialize({...})`:

  ```ts
  LevelMomentWebClient.initialize({
    apiUrl: "https://api.levelmoment.com",
    placementId: "your-game-id",
    studentToken: token,
    breakLoadTimeoutMs: 10000, // default 15000; 0 (or negative) disables it
  });
  ```

  The watchdog **only** guards the pre-`ready` phase. Once the hosted page
  posts `ready` it owns the lifecycle and is never force-closed — a student
  legitimately thinking through a quiz must not be interrupted.

- **`dispose()` (explicit escape hatch).** Call `ad.dispose()` on your own
  abort/pause path (e.g. the player quits to menu mid-break). It synchronously
  tears down the iframe, removes the `message` listener, and clears the
  load-timeout timer. Always call it if you abandon a break without waiting
  for `onAdDismissed`.
