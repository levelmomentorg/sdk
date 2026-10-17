# Test in sandbox

**With a handoff:** read [the partner handoff](partner-handoff.md) and run only
the preview, test or build commands it supplies.

**Without one:** follow
[Testing and sandbox](https://levelmoment.com/docs/testing): one `eply_sbx_`
token per developer, kept in a file only sandbox builds read and passed only
as `unsafeTesting: { token }`; a production build must contain no
`eply_sbx_`. The agent-only notes in [self-serve.md](self-serve.md) cover
moving the token without reading it and failing each break type.

Either way, check each slot: one reward for a passed graded break, no reward for
a failed break, and a load or show failure that resumes with no reward. A player can't
close a break partway, so these cover every way a break ends. Record the actual
environment and device used. A local check doesn't prove a native device
integration.
