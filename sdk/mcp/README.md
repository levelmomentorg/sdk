# @levelmoment/mcp

A read-only MCP server for Level Moment partner SDK integrations.

**Preview:** Validate the server and its API responses in your integration
environment before relying on it in developer tooling.

## Install

Use the exact preview package archive supplied for your integration, then run
the installed binary. Keep the archive and its checksum with your build inputs;
the public registry package may not match this preview guide.

The default service is `https://levelmoment.com/api`. Set
`LEVELMOMENT_API_URL` only for an approved test environment; partner
integrations should use the canonical service.

## Tools

- `validate_placement` checks a placement ID before integration.
- `get_theme` returns the configured presentation theme for a placement.
- `docs_links` returns canonical Level Moment developer documentation links.

The server does not create games, change configuration, expose account records,
or return settlement information.

## MCP configuration

```json
{
  "mcpServers": {
    "levelmoment": {
      "command": "/ABSOLUTE_PROJECT_PATH/node_modules/.bin/levelmoment-mcp"
    }
  }
}
```

Use the signed-in developer documentation for installation and reward callback
semantics. Keep account credentials out of source control; placement IDs are public.

## License

MIT
