# Placement provisioning

With a handoff, use its placement ID exactly as supplied.

Without one, the developer registers the game in the portal while signed in to
their own account:
[Quickstart, step 1](https://levelmoment.com/docs/getting-started#register)
(choose "Register game" on the Games page and commit the placement ID in the
game's config). The agent-only notes in [self-serve.md](self-serve.md) cover
driving the browser. Never call private APIs, sign in on the developer's
behalf, or create a placeholder that resembles a placement ID.
