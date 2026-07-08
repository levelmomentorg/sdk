# `/levelmoment register` — create a new game on the user's account

Use this when:

- The user invoked `/levelmoment register <name>` directly
- The port flow chose "auto-register" and called you

Result: a new game row created on the LevelMoment developer account, and the
generated placement ID printed.

## Step 1 — read or acquire the CLI auth token

Check `~/.levelmoment/auth.json`. If it exists and contains a `token` field:

```bash
cat ~/.levelmoment/auth.json
```

If the file is missing or empty, walk the user through obtaining a token:

> "I need to authenticate to your LevelMoment account to register the game.
>
> 1. Open https://app.levelmoment.com/settings/cli-tokens in your browser.
> 2. Click 'Generate token' (or copy an existing one).
> 3. Paste it here."

Use `AskUserQuestion` with one option:

- "I've copied the token — paste in the next message"

Or accept it via free-text reply. Token format: `eply_cli_<40 chars>`.
Validate before saving.

Save to `~/.levelmoment/auth.json` with mode 0600:

```json
{
  "token": "eply_cli_...",
  "issued_at": "2026-05-10T19:30:00Z"
}
```

```bash
mkdir -p ~/.levelmoment
chmod 700 ~/.levelmoment
# write JSON via Write tool, then:
chmod 600 ~/.levelmoment/auth.json
# verify — these lines MUST run after the write. If either fails, delete
# the file (`rm -f ~/.levelmoment/auth.json`) and stop. Do not proceed with a
# world-readable credential on disk.
[ "$(stat -c '%a' ~/.levelmoment/auth.json 2>/dev/null || stat -f '%Lp' ~/.levelmoment/auth.json)" = "600" ]
[ "$(stat -c '%a' ~/.levelmoment 2>/dev/null || stat -f '%Lp' ~/.levelmoment)" = "700" ]
```

## Step 2 — collect the game name

If invoked as `/levelmoment register My Cool Game`, use the argument string
(everything after `register`).

Otherwise ask via `AskUserQuestion`:

> "What's the name of your game?"

Validate: non-empty, ≤ 80 characters, no leading/trailing whitespace.

## Step 3 — call the API

Resolve the API URL in this order, taking the first non-empty value:

1. `$LEVELMOMENT_API_URL` if set (lets devs target staging / preview deploys)
2. `https://api.levelmoment.com` (default)

Never proceed with a URL that doesn't match `^https://[a-zA-Z0-9.-]+/?$` —
this defeats accidental cross-environment writes from a typoed override.

```bash
API_URL="${LEVELMOMENT_API_URL:-https://api.levelmoment.com}"
curl -sS -X POST "$API_URL/games" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"name": "<game name>"}'
```

Expected response:

```json
{
  "id": "gm_...",
  "name": "My Cool Game",
  "placementId": "pl_xxxxxxxxxxxxxxxxxxxxxx",
  "createdAt": "..."
}
```

**Validate** the returned placement ID before passing it back to the port
flow or writing it to the user's source. It must match the canonical
regex `^pl_[A-Za-z0-9_-]{22}$`. If it doesn't, treat the response as
untrusted (DNS hijack, typoed `LEVELMOMENT_API_URL`, etc.), discard it, and
ask the user to verify their network and the `LEVELMOMENT_API_URL` value
before retrying. Do not write a malformed placement ID into the user's
code under any circumstance.

Save the validated `placementId` for the caller (the port flow consumes
it via this sub-flow's return value).

## Step 4 — handle errors

| HTTP status | Cause                                | Recovery                                               |
| ----------- | ------------------------------------ | ------------------------------------------------------ |
| 401         | Token invalid or expired             | Delete `~/.levelmoment/auth.json`, restart from Step 1 |
| 403         | Token doesn't have game-create scope | Ask user to regenerate with correct scope              |
| 409         | Game name already exists on account  | Ask for a new name and retry                           |
| 5xx         | Server error                         | Retry once with exponential backoff                    |

## Step 5 — confirm

Print to the user:

```
✓ Registered game "My Cool Game"
  Placement ID: pl_xxxxxxxxxxxxxxxxxxxxxx
  Game ID: gm_...
  Manage at: https://app.levelmoment.com/games/gm_...
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
