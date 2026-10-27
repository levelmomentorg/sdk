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
3. Initialize once with `new LevelMomentConfig()`, from a bootstrap object or
   a `[RuntimeInitializeOnLoadMethod]` (the
   [Unity README](https://github.com/levelmomentorg/sdk/tree/sdk-v0.4.3/sdk/unity#quickstart)
   shows both). Call `EnsureAccess` from the player's tap, then load one
   `RewardedAd` per show, passing a `LevelMomentAdSlot` and
   `format: "practice_set"`. Call `Show()` only for the supplied slot. Do not
   configure a hosted URL, API URL, or credential.
4. For sandbox, put the token in `UserSettings/LevelMomentSandboxToken.txt`.
   The SDK reads it in the Editor and development builds and keeps it out of
   release builds; write no token code or build guard.
5. Map each ending to a game result as
   [Quickstart, step 3](https://levelmoment.com/docs/getting-started#results)
   shows: a break without a reward grants nothing, and a one-time offer such
   as a revive returns to its own screen. An interstitial slot is 18 seconds
   and takes no `format`.
6. With `RequireInternet`, resume only when online and no break is open, and
   turn off the game's own input, including the uGUI `EventSystem`, as
   [Handle a device with no connection](https://levelmoment.com/docs/getting-started#offline)
   shows. IMGUI can't block input.
7. Expect the SDK to change `ProjectSettings/ProjectSettings.asset` once, when
   it moves Active Input Handling from Old to Both on the next editor start;
   commit that change.
