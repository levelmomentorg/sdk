# @levelmoment/mcp

A stdio MCP server that exposes read-only tools against the Level Moment public
API for AI coding assistants and Claude Desktop.

## v1 scope

Version 1 exposes only the **unauthenticated public API surface**. Tools that
require an API key (earnings data, payout status, game configuration writes) are
planned for v2. If you need those tools, open an issue or email
support@levelmoment.com.

## Installation

```bash
npm install @levelmoment/mcp
```

Or run directly without installing:

```bash
npx @levelmoment/mcp
```

## Configuration

Set `LEVELMOMENT_API_URL` if you're targeting a non-production environment:

```bash
LEVELMOMENT_API_URL=https://staging.levelmoment.com/api npx @levelmoment/mcp
```

Default: `https://levelmoment.com/api`

## Tools

### `validate_placement`

Check whether a Level Moment placement ID (game key) is valid.

**Input:**

- `placement_id` (string) — the placement ID to validate (`X-LevelMoment-Game-Key` header value, typically `pl_*`)

**Returns:** `{ valid: true, gameId: "gm_..." }` on success, error on 401.

**Use case:** Run this before starting an integration to catch mis-configured
placement IDs before they cause opaque errors at runtime.

---

### `get_theme`

Retrieve the visual theme configured for a Level Moment game placement.

**Input:**

- `placement_id` (string) — the placement ID to look up

**Returns:** `{ themeId: "default" }` — the preset id used by the hosted `/break` page.

---

### `docs_links`

Return a JSON map of documentation section names → canonical URLs on
levelmoment.com. No input required.

**Use case:** Let an AI assistant link users to the right reference docs without
guessing or hallucinating URLs.

---

## Claude Desktop configuration

Add to your `claude_desktop_config.json`:

```json
{
  "mcpServers": {
    "levelmoment": {
      "command": "npx",
      "args": ["@levelmoment/mcp"],
      "env": {
        "LEVELMOMENT_API_URL": "https://levelmoment.com/api"
      }
    }
  }
}
```

## Claude Code configuration

Add to your `.claude/mcp_servers` config (or `~/.claude/mcp_servers.json`):

```json
{
  "levelmoment": {
    "command": "npx",
    "args": ["@levelmoment/mcp"],
    "env": {
      "LEVELMOMENT_API_URL": "https://levelmoment.com/api"
    }
  }
}
```

## Development

```bash
# From monorepo root
npm install
npm run build --workspace=sdk/mcp
npm run test --workspace=sdk/mcp
```

## License

MIT
