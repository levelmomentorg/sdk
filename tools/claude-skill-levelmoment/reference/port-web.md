# Port a web slot

Read [`../AGENT-INSTRUCTIONS.md`](../AGENT-INSTRUCTIONS.md) and the supplied
handoff first. Use [`../templates/web-init.ts.tmpl`](../templates/web-init.ts.tmpl)
as the lifecycle shape.

Initialize `LevelMomentWebClient` with `{ placementId }`. Use `client.loadAd()`
to make one handle, then call `ad.show()` in the supplied slot. Wire the first
correct reward and terminal-once resume to the game's supplied hooks. Do not
configure a hosted URL, API URL, or credential.
