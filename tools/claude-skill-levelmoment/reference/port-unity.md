# Port a Unity slot

Read [`../AGENT-INSTRUCTIONS.md`](../AGENT-INSTRUCTIONS.md) and the supplied
handoff first. Use [`../templates/unity-AdsManager.cs.tmpl`](../templates/unity-AdsManager.cs.tmpl)
for the target slot.

1. Add the SDK and gree lines to `Packages/manifest.json`, as
   [Quickstart, step 2](https://levelmoment.com/docs/getting-started#install)
   and the package README's Requirements show. The SDK brings the Input System
   and web request modules, turns on its gree adapter and keeps its
   assemblies from being stripped, so add no scripting define and no
   `Assets/link.xml`.
2. Add `LevelMomentSDK.Runtime` to the `references` of every assembly
   definition that uses SDK types, test assemblies included.
3. Initialize once with `new LevelMomentConfig()` and load one `RewardedAd`
   per show, passing a `LevelMomentAdSlot` and `format: "practice_set"`. Call
   `Show()` only for the supplied slot. Do not configure a hosted URL, API
   URL, or credential.
4. For sandbox, put the token in `UserSettings/LevelMomentSandboxToken.txt`.
   The SDK reads it in the Editor and development builds and keeps it out of
   release builds; write no token code or build guard.
