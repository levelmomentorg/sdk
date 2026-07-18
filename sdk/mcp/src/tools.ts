// MCP tool handlers — read-only tools against the Level Moment public API.
//
// v1 scope: no authenticated (API-key-scoped) endpoints. All tools use the
// public, unauthenticated surface. API-key-scoped tools (earnings, payout
// status) are a v2 item noted in the README.

import type { CallToolResult } from "@modelcontextprotocol/sdk/types.js";

export interface ToolContext {
  apiUrl: string;
  fetchFn?: typeof fetch;
}

// ---------------------------------------------------------------------------
// mcpGet — shared GET scaffold
//
// Both read-only tools do the same thing: fire a GET, turn a thrown network
// error into an `isError` CallToolResult, and otherwise hand back the Response
// + its text body for the caller to format. Extracted so each handler is just
// its URL/headers + its success/failure rendering.
// ---------------------------------------------------------------------------

type McpGetResult =
  | { ok: true; res: Response; body: string }
  | { ok: false; error: CallToolResult };

async function mcpGet(
  ctx: ToolContext,
  url: string,
  headers: Record<string, string>,
): Promise<McpGetResult> {
  const fn = ctx.fetchFn ?? fetch;
  let res: Response;
  try {
    res = await fn(url, { method: "GET", headers });
  } catch (err) {
    return {
      ok: false,
      error: {
        isError: true,
        content: [
          {
            type: "text",
            text: `Network error reaching Level Moment API: ${err instanceof Error ? err.message : String(err)}`,
          },
        ],
      },
    };
  }
  return { ok: true, res, body: await res.text() };
}

// ---------------------------------------------------------------------------
// validate_placement — GET /sdk/v1/validate
// ---------------------------------------------------------------------------

export interface ValidatePlacementArgs {
  placement_id: string;
}

export async function handleValidatePlacement(
  args: ValidatePlacementArgs,
  ctx: ToolContext,
): Promise<CallToolResult> {
  const got = await mcpGet(ctx, `${ctx.apiUrl}/sdk/v1/validate`, {
    "X-LevelMoment-Game-Key": args.placement_id,
    Accept: "application/json",
  });
  if (!got.ok) return got.error;
  const { res, body } = got;

  if (res.ok) {
    let parsed: unknown;
    try {
      parsed = JSON.parse(body);
    } catch {
      parsed = body;
    }
    return {
      content: [
        {
          type: "text",
          text: JSON.stringify(
            {
              valid: true,
              ...(typeof parsed === "object" && parsed !== null ? parsed : {}),
            },
            null,
            2,
          ),
        },
      ],
    };
  }

  return {
    isError: true,
    content: [
      {
        type: "text",
        text: `validate_placement failed (HTTP ${res.status}): ${body}`,
      },
    ],
  };
}

// ---------------------------------------------------------------------------
// get_theme — GET /sdk/v1/theme?placementId=...
// ---------------------------------------------------------------------------

export interface GetThemeArgs {
  placement_id: string;
}

export async function handleGetTheme(
  args: GetThemeArgs,
  ctx: ToolContext,
): Promise<CallToolResult> {
  const url = new URL(`${ctx.apiUrl}/sdk/v1/theme`);
  url.searchParams.set("placementId", args.placement_id);

  const got = await mcpGet(ctx, url.toString(), {
    Accept: "application/json",
  });
  if (!got.ok) return got.error;
  const { res, body } = got;

  if (res.ok) {
    return {
      content: [{ type: "text", text: body }],
    };
  }

  return {
    isError: true,
    content: [
      {
        type: "text",
        text: `get_theme failed (HTTP ${res.status}): ${body}`,
      },
    ],
  };
}

// ---------------------------------------------------------------------------
// docs_links — static reference to key documentation URLs
// ---------------------------------------------------------------------------

const DOCS_LINKS = {
  quickstart: "https://levelmoment.com/docs/getting-started",
  how_it_works: "https://levelmoment.com/docs/how-it-works",
  webhooks: "https://levelmoment.com/docs/webhooks",
  errors: "https://levelmoment.com/docs/errors",
  standards: "https://levelmoment.com/docs/standards",
  api_reference: "https://levelmoment.com/docs/api-reference",
  porting: "https://levelmoment.com/docs/porting",
  porting_claude_code: "https://levelmoment.com/docs/porting/using-claude-code",
  openapi_json: "https://levelmoment.com/api/openapi.json",
  llms_txt: "https://levelmoment.com/llms.txt",
  llms_full_txt: "https://levelmoment.com/llms-full.txt",
  status: "https://levelmoment.com/status",
};

export async function handleDocsLinks(): Promise<CallToolResult> {
  return {
    content: [
      {
        type: "text",
        text: JSON.stringify(DOCS_LINKS, null, 2),
      },
    ],
  };
}
