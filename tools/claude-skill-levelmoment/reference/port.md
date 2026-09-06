# Port the supplied slot

1. Read [the partner handoff](partner-handoff.md) and validate its inputs.
2. Verify the supplied artifact's SHA-256, then run its exact install command.
   Update the dependency manifest and generated lockfile when that command
   changes them.
3. Read the guide for the handoff's `platform`: [web](port-web.md),
   [React Native](port-react-native.md), [Flutter](port-flutter.md), or
   [Unity](port-unity.md).
4. Inspect the specified slot and replace only its rewarded lifecycle. Keep
   unrelated ad placements and the game's existing pause, resume, and bonus
   ownership.
5. Use a new ad handle for every show. Grant the supplied reward action on the
   first correct answer, then resume once on dismissal, show failure, or load
   failure.
6. Run every supplied verification command and report the artifact checksum,
   changed files, and remaining device checks.
