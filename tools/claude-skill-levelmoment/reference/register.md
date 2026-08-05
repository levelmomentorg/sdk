# `/levelmoment register` — create a new game on the user's account

Use this when:

- The user invoked `/levelmoment register <name>` directly
- The port flow chose "auto-register" and called you

Result: a new game row created on the LevelMoment developer portal and the
generated placement ID in hand.

## Step 1 — collect the game name

If invoked as `/levelmoment register My Cool Game`, use the argument string
(everything after `register`).

Otherwise ask via `AskUserQuestion`:

> "What's the name of your game?"

Validate: non-empty, ≤ 80 characters, no leading/trailing whitespace.

## Step 2 — guide the developer through portal registration

Game registration requires a logged-in developer portal session. Direct them to
create the game via the portal UI:

> "I'll need a LevelMoment placement ID for **<game name>**. Please do the
> following:
>
> 1. Open **https://app.levelmoment.com/games** in your browser (sign up or
>    sign in if prompted — it's free).
> 2. Click **New game**.
> 3. Enter the name: **<game name>** and save.
> 4. Copy the placement ID shown on the game's page (it looks like
>    `pl_xxxxxxxxxxxxxxxxxxxxxx`).
> 5. Paste it here."

Use `AskUserQuestion` with one option:

- "I've copied the placement ID — paste in the next message"

Or accept it via free-text reply.

## Step 3 — validate the placement ID

Validate the pasted value before using it. It must match the canonical regex
`^pl_[A-Za-z0-9_-]{22}$`.

If the format is wrong, ask the user to re-copy it from the portal. Do **not**
proceed with a malformed placement ID and do **not** invent one.

Save the validated `placementId` for the caller (the port flow consumes
it via this sub-flow's return value).

## Step 4 — confirm

Print to the user:

```
✓ Got placement ID for "<game name>"
  Placement ID: pl_xxxxxxxxxxxxxxxxxxxxxx
  Manage at: https://app.levelmoment.com/games
```

If invoked from `/levelmoment port`, return the placement ID to the caller
and stop. Don't print anything else.

If invoked directly by the user, print the next steps:

```
Next steps:
1. Run /levelmoment port to wire this placement ID into your game
2. Or copy the placement ID above and paste it into your code manually
3. Test with the sandbox: https://app.levelmoment.com/sandbox?pl=pl_xxxx...
```
