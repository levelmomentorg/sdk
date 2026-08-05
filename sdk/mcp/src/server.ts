// @levelmoment/mcp — stdio MCP server entry point.
//
// Run directly: npx @levelmoment/mcp
// Or via Claude Desktop / Claude Code config (see README).
//
// Config via environment:
//   LEVELMOMENT_API_URL  — defaults to https://levelmoment.com/api

import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { z } from "zod";
import {
  handleValidatePlacement,
  handleGetTheme,
  handleDocsLinks,
  type ToolContext,
} from "./tools.js";

const API_URL =
  process.env["LEVELMOMENT_API_URL"] ?? "https://levelmoment.com/api";

const ctx: ToolContext = { apiUrl: API_URL };

const server = new McpServer({
  name: "levelmoment",
  version: "0.1.0",
});

// ---------------------------------------------------------------------------
// Tool: validate_placement
// ---------------------------------------------------------------------------
server.tool(
  "validate_placement",
  "Check whether a Level Moment placement ID (game key) is valid. Returns { valid: true, gameId } on success. Use this before running an integration to catch mis-configured game keys early.",
  {
    placement_id: z
      .string()
      .describe(
        "The Level Moment placement ID to validate (the X-LevelMoment-Game-Key value, typically starting with pl_).",
      ),
  },
  async ({ placement_id }) => handleValidatePlacement({ placement_id }, ctx),
);

// ---------------------------------------------------------------------------
// Tool: get_theme
// ---------------------------------------------------------------------------
server.tool(
  "get_theme",
  "Retrieve the visual theme configured for a Level Moment game placement. Returns { themeId } — a preset id (e.g. 'default', 'ocean', 'forest') used by the hosted /break page.",
  {
    placement_id: z
      .string()
      .describe("The Level Moment placement ID to look up."),
  },
  async ({ placement_id }) => handleGetTheme({ placement_id }, ctx),
);

// ---------------------------------------------------------------------------
// Tool: docs_links
// ---------------------------------------------------------------------------
server.tool(
  "docs_links",
  "Return a JSON object mapping documentation section names to their canonical URLs on levelmoment.com. Use this to link users to the right reference without guessing URLs.",
  {},
  async () => handleDocsLinks(),
);

// ---------------------------------------------------------------------------
// Start
// ---------------------------------------------------------------------------
async function main() {
  const transport = new StdioServerTransport();
  await server.connect(transport);
  // The server now reads from stdin and writes to stdout.
  // Logging goes to stderr so it doesn't pollute the MCP protocol stream.
  process.stderr.write(
    `[levelmoment-mcp] stdio server started (API: ${API_URL})\n`,
  );
}

main().catch((err) => {
  process.stderr.write(`[levelmoment-mcp] fatal: ${err}\n`);
  process.exit(1);
});
