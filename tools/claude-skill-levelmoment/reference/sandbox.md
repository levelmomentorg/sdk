# Test in sandbox

**With a handoff:** read [the partner handoff](partner-handoff.md) and run only
the preview, test or build commands it supplies.

**Without one:** follow
[Testing and sandbox](https://levelmoment.com/docs/testing): one `eply_sbx_`
token per developer, kept in a gitignored env file and passed only as
`unsafeTesting: { token }` in sandbox builds; check every slot; confirm a
production build contains no `eply_sbx_`. The agent-only notes in
[self-serve.md](self-serve.md) cover moving the token without reading it.

Either way, check each slot: one reward for a passed graded break, no reward for
a failed or abandoned break, cancellation, and a show failure. Record the actual
environment and device used. A local check doesn't prove a native device
integration.
