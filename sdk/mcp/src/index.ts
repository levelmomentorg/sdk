// @levelmoment/mcp — public API surface
//
// Re-exports the tool handlers so consumers can unit-test them or embed them
// in custom MCP server configurations. The stdio server entry is src/server.ts.

export {
  handleValidatePlacement,
  handleGetTheme,
  handleDocsLinks,
  type ValidatePlacementArgs,
  type GetThemeArgs,
  type ToolContext,
} from "./tools.js";
