# Greenfield — React Native

User has no existing ad SDK. Scaffold a fresh LevelMoment integration.

## Step 1 — install

```bash
npm install @levelmoment/sdk-react-native react-native-webview
```

For Expo:

```bash
npx expo install react-native-webview
npm install @levelmoment/sdk-react-native
```

Ask before running.

## Step 2 — wire `<LevelMomentAdModal />` at app root

Find `App.tsx` / `App.jsx` (or whatever file `AppRegistry.registerComponent`
references).

Read `.claude/skills/levelmoment/templates/rn-app-root.tsx.tmpl` and merge
its `<LevelMomentAdModal />` import + render into the existing component
tree. Render as a sibling of `<NavigationContainer>` if present.

## Step 3 — create an ad hook

Read `.claude/skills/levelmoment/templates/rn-use-ad-break.ts.tmpl` and write
it to `src/hooks/useLevelMomentAdBreak.ts` (or `src/levelmoment/useAdBreak.ts`
if no `hooks/` directory exists — ask the user).

Substitute `{{PLACEMENT_ID}}`, `{{API_URL}}`, and `{{BREAK_URL}}`
(`https://app.levelmoment.com/break` — the required hosted `/break` page URL).

## Step 4 — leave a TODO at a likely pause point

Search for: `onGameOver`, `levelComplete`, `endLevel`, `useEffect`
blocks gated on a "game ended" state. If found, leave a comment near it:

```ts
// LEVELMOMENT_TODO: invoke showAdBreak() from useLevelMomentAdBreak() here
```

If nothing is found, just leave a top-level TODO at the bottom of
`App.tsx`.

## Step 5 — summary

```
✓ Scaffolded LevelMoment integration:
  - <App.tsx> — added <LevelMomentAdModal />
  - <src/hooks/useLevelMomentAdBreak.ts> — created
  - <Game.tsx:142> — added a TODO marker

Next steps:
1. Call `showAdBreak()` from the hook at your pause points
2. Pass a real student token (from deep link or backend)
3. Test at https://app.levelmoment.com/sandbox
```
