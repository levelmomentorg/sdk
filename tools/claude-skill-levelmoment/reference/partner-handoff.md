# Use the partner handoff

Read [`../AGENT-INSTRUCTIONS.md`](../AGENT-INSTRUCTIONS.md) first. It defines
the shared contract for every platform.

Read the supplied handoff JSON against
[`../handoff.example.json`](../handoff.example.json). Do not proceed until its
platform, placement ID, target slot, reward action, and verification commands
are present. Web and React Native also need every artifact path and SHA-256.
Flutter and Unity need an immutable source ref and exact versions for every
listed peer.

The handoff identifies one slot. Preserve other ad code and do not register a
game or call a Level Moment API. Use only the supplied immutable artifact; the
public `0.1.2` release is not a replacement for the `0.2.0` preview.
