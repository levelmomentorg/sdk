# Port a Flutter slot

Read [`../AGENT-INSTRUCTIONS.md`](../AGENT-INSTRUCTIONS.md) and the supplied
handoff first. Initialize the SDK with no production overrides using
[`../templates/flutter-init.dart.tmpl`](../templates/flutter-init.dart.tmpl).
Use [`../templates/flutter-ad-break.dart.tmpl`](../templates/flutter-ad-break.dart.tmpl)
for the target slot.

Call `LevelMomentRewardedAd.load()` for each show and call `show(context:)` on
the returned handle. Do not provide a hosted URL, API URL, or credential.
