# Greenfield — Web

User has no existing ad SDK. Scaffold a fresh LevelMoment integration.

## Step 1 — install the SDK

```bash
npm install @levelmoment/sdk-web
```

Ask first.

## Step 2 — find the entry file

In order of preference:

1. `src/main.ts` / `src/main.js`
2. `src/index.ts` / `src/index.js`
3. `src/scenes/BootScene.ts` (Phaser)
4. The script tag in `index.html`

If multiple candidates exist (Phaser game in a monorepo), ask the user
which file to wire into.

## Step 3 — insert the init template

Read `.claude/skills/levelmoment/templates/web-init.ts.tmpl`. Insert at the
top of the entry file (after any existing imports). Substitute:

- `{{PLACEMENT_ID}}` → the placement ID from the port flow
- `{{API_URL}}` → `https://api.levelmoment.com`

## Step 4 — insert a load/show marker

If the user has a clear "level complete" or "lives lost" handler in
their code (look for function names like `onLevelComplete`,
`gameOver`, `endLevel`), insert the load/show snippet there with a
`// LEVELMOMENT_TODO:` comment.

Otherwise add a top-level comment block at the end of the entry file:

```ts
// === LEVELMOMENT: place an ad break ===
// LEVELMOMENT_TODO: call showAdBreak() at your natural pause points
// (e.g. level complete, lives lost). The function is defined above
// from the init template.
// ===================================
```

## Step 5 — summary

```
✓ Scaffolded LevelMoment integration in <entry-file>

Next steps:
1. Place `showAdBreak()` calls at your game's natural pause points
2. Replace the URL token lookup if your token comes from somewhere else
3. Test at https://app.levelmoment.com/sandbox
```
