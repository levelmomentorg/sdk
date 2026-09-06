# Port a React Native slot

Read [`../AGENT-INSTRUCTIONS.md`](../AGENT-INSTRUCTIONS.md) and the supplied
handoff first. Mount `LevelMomentAdModal` once at the app root using
[`../templates/rn-app-root.tsx.tmpl`](../templates/rn-app-root.tsx.tmpl), then
use [`../templates/rn-use-ad-break.ts.tmpl`](../templates/rn-use-ad-break.ts.tmpl)
for the target slot.

Create `LevelMomentAd` with the supplied placement ID for each show, call
`load()`, and call `show()` after its `loaded` event. Do not provide a hosted
URL, API URL, or credential.
