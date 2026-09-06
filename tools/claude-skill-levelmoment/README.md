# Level Moment partner packet

Use this packet with a partner-supplied SDK artifact and handoff manifest to
replace one existing rewarded slot with a Level Moment break.

Read [AGENT-INSTRUCTIONS.md](AGENT-INSTRUCTIONS.md) directly in any coding
agent. It uses only paths inside this packet and does not require an installer.
The optional `install-skill` executable copies the same packet into a Claude
Code project and adds `/levelmoment`.

Use [handoff.example.json](handoff.example.json) as the contract for the
partner's handoff. The example intentionally has no usable artifact, checksum,
placement ID, target slot, reward action, or command. The partner supplies
those integration inputs separately. It never carries an endpoint, token, or
preview secret.

The packet supports web, React Native, Flutter, and Unity previews at the
repository's `0.2.0` SDK surface. Do not install the public `0.1.2` release in
place of a supplied preview artifact.

## Packet layout

- `AGENT-INSTRUCTIONS.md` contains the shared integration flow.
- `reference/` contains platform mapping, installation, diagnosis, and preview
  guidance.
- `templates/` contains examples with a fresh handle per break, one bonus for
  the first correct answer, and one game resume per terminal result.
- `handoff.example.json` describes the required partner inputs.

## Verify the packet

Run the packet tests from the SDK repository root:

```bash
node --test tools/claude-skill-levelmoment/test/*.test.mjs
```

The test suite executes the web and React Native templates with mocked SDK
exports, checks their TypeScript types against the installed SDK packages, and
checks the bundled packet's entry points. Run `flutter analyze` and `flutter
test` from `sdk/flutter`, then `tools/unity-compile-check/run.sh`, before
shipping Flutter or Unity template changes. Those checks compile the native SDK
surfaces; a Unity editor build and real device run remain separate evidence.
