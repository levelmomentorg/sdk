# Port a Unity slot

Read [`../AGENT-INSTRUCTIONS.md`](../AGENT-INSTRUCTIONS.md) and the supplied
handoff first. Use [`../templates/unity-AdsManager.cs.tmpl`](../templates/unity-AdsManager.cs.tmpl)
for the target slot.

Initialize with `new LevelMomentConfig()` and load one `RewardedAd` per show.
Call `Show()` only for the supplied slot. Do not configure a hosted URL, API
URL, or credential.
