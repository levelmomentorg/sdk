# @levelmoment/sdk-web

**Status: 🟢 Verified end to end in the browser — real question served through the iframe loader, answered, reward bridged.**

JavaScript/TypeScript SDK for web and HTML5 games. Drop-in replacement for Google AdSense / Ad Manager interstitials in browser-based games.

Per [ADR-001](../../docs/ADR-001-webview-rendering.md), this SDK is a **thin
loader**: `show()` opens the hosted `/break` page (`platform/web/app/break`) in
a fullscreen iframe and bridges its `postMessage` terminal events. It ships
**no DOM renderer** — all question rendering and the impression queue live in
the hosted page. Structurally it mirrors `@levelmoment/sdk-react-native` (a WebView
shell).

See [`MIGRATION.md`](MIGRATION.md) for a step-by-step swap guide from AdSense.

---

## What's Done

- **`LevelMomentWebAd`** — thin loader ad class; `load()` marks the break ready, `show()` mounts a fullscreen iframe pointed at the hosted `/break` page and bridges its `ready/earnedReward/dismissed/error` postMessage protocol (origin-validated)
- **`LevelMomentWebClient`** — one-time initialisation + `loadAd()` convenience; resolves the break URL (same-origin `/break` by default, overridable via `breakUrl`). `start()`/`stop()` are kept-for-compat no-ops (the impression flush loop moved into the hosted page)
- **`WebClientConfig`** — `LevelMomentConfig & { breakUrl?: string; mock?: boolean; breakLoadTimeoutMs?: number }`. `LevelMomentConfig.customData` (SSV-parity) rides onto the `/break` URL and is stamped on every impression the hosted page records
- **Load-timeout watchdog** — if the hosted `/break` page never posts `ready` within `breakLoadTimeoutMs` (default 15000ms; `0`/negative disables) of `show()`, the iframe + listener + timer are torn down and `onAdDismissed` fires so the game resumes (handles host crash/navigation/network-drop). Only guards the pre-`ready` phase — once `ready` arrives the hosted page owns the lifecycle and is never force-closed. `ad.dispose()` is the explicit escape hatch for game-side abort/pause (tears down iframe + listener + timer)
- **No DOM renderer shipped** — all 8 question types, session/lesson flow, and the impression queue are rendered/owned by the hosted page (`platform/web/app/break/_renderer/`)
- **`LevelMomentAd`** (re-exported from `@levelmoment/sdk-core`) — low-level handle for advanced use cases
- **`MIGRATION.md`** — line-by-line swap guide from AdSense/Ad Manager
- **Unit tests** — URL building (mock/live, breakUrl default/override, `?`/`&`, SSV `customData`), the postMessage bridge (reward mapping, dismiss/error teardown, origin mismatch ignored), `start()/stop()` no-ops, iframe mounting/teardown, the load-timeout watchdog (fires on host silence, cleared by `ready`/`dispose()`/`dismissed`, custom + `0`-disabled timeout)

## What's Not Done

- [ ] No CDN/UMD build for non-module game frameworks — add a `umd` tsup output for games that use `<script src="...">` tags
- [ ] Games not served same-origin as the hosted Next app must pass `breakUrl` explicitly (documented in `MIGRATION.md`)

---

## Setup

```bash
npm install   # from repo root

# Run unit tests (Vitest)
cd sdk/web && npx vitest run

# Run type-check
npx tsc --noEmit

# Build (ESM + CJS dual bundle)
npm run build
```

---

## Usage

```ts
import { LevelMomentWebClient, LevelMomentWebAd } from "@levelmoment/sdk-web";

// 1. Initialise once at game boot
const client = LevelMomentWebClient.initialize({
  apiUrl: import.meta.env.VITE_API_URL,
  placementId: import.meta.env.VITE_GAME_ID,
  studentToken: new URLSearchParams(location.search).get("token") ?? "",
});
client.start();

// 2. Preload while the game runs
let pendingAd: LevelMomentWebAd | null = null;
client.loadAd({
  onAdLoaded: (ad) => {
    pendingAd = ad;
  },
  onAdFailedToLoad: () => {},
});

// 3. Show at the natural pause point — SDK renders everything.
//    earnedReward fires once per graded answer — accumulate, apply on dismiss.
let reward = 0;
pendingAd?.show({
  onUserEarnedReward: (r) => (reward = Math.max(reward, r.amount)),
  onAdDismissed: () => {
    if (reward === 1) grantBonus();
    resumeGame();
  },
  onAdFailedToShow: () => resumeGame(), // optional; absent → falls back to onAdDismissed
});
```

That's it. `show()` opens the hosted `/break` page, which renders the question
UI, handles all user interactions, manages session flow (multiple questions,
summary screen, lesson for deep_dive), owns the impression queue, and posts
back terminal events that drive your callbacks.

> **Same-origin default.** `loadAd()` defaults the break URL to
> `window.location.origin + "/break"`. If your game is **not** served
> same-origin as the hosted Next app, pass `breakUrl` in
> `initialize({ ..., breakUrl })`. See `MIGRATION.md`.

---

## Key Files

| File                      | Purpose                                                                                 |
| ------------------------- | --------------------------------------------------------------------------------------- |
| `src/LevelMomentWebAd.ts` | Thin loader ad class — builds the `/break` URL, mounts the iframe, bridges postMessage  |
| `src/client.ts`           | `LevelMomentWebClient` — init, breakUrl resolution, `loadAd()`; `start()/stop()` no-ops |
| `src/index.ts`            | Public exports                                                                          |
| `MIGRATION.md`            | Swap guide from AdSense / Ad Manager                                                    |
