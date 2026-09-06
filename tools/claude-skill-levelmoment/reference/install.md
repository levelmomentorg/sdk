# Install the supplied preview

1. Read [the partner handoff](partner-handoff.md).
2. For web or React Native, compute SHA-256 for `sdk.platformArtifact` and
   every `sdk.requiredArtifacts` entry. Compare each value byte-for-byte with
   its handoff SHA-256. For Flutter or Unity, verify the supplied immutable
   source ref and each peer version instead.
3. Run only `sdk.installCommand` from the handoff. It must name the immutable
   artifact or source ref. Do not replace it with npm, a Git branch, or a
   guessed version.
4. Keep the manifest and generated lockfile that the install command changes.
   The handoff authorizes these manifest and lockfile changes.
