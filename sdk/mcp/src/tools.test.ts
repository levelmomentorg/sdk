// Unit tests for MCP tool handlers — all HTTP calls are mocked via the
// fetchFn ToolContext injection so these run without a real API.

import { describe, it, expect, vi } from "vitest";
import {
  handleValidatePlacement,
  handleGetTheme,
  handleDocsLinks,
  type ToolContext,
} from "./tools.js";

const BASE_URL = "https://levelmoment.com/api";

function makeCtx(mockFetch: typeof fetch): ToolContext {
  return { apiUrl: BASE_URL, fetchFn: mockFetch };
}

function makeResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

// ---------------------------------------------------------------------------
// validate_placement
// ---------------------------------------------------------------------------

describe("handleValidatePlacement", () => {
  it("returns valid result on 200 response", async () => {
    const mockFetch = vi
      .fn()
      .mockResolvedValue(makeResponse({ valid: true, gameId: "gm_abc123" }));
    const result = await handleValidatePlacement(
      { placement_id: "pl_test" },
      makeCtx(mockFetch),
    );
    expect(result.isError).toBeFalsy();
    expect(result.content).toHaveLength(1);
    const text = JSON.parse((result.content[0] as { text: string }).text);
    expect(text.valid).toBe(true);
    expect(text.gameId).toBe("gm_abc123");
    expect(mockFetch).toHaveBeenCalledWith(
      `${BASE_URL}/sdk/v1/validate`,
      expect.objectContaining({
        headers: expect.objectContaining({
          "X-LevelMoment-Game-Key": "pl_test",
        }),
      }),
    );
  });

  it("returns isError=true on 401 response", async () => {
    const mockFetch = vi
      .fn()
      .mockResolvedValue(
        makeResponse(
          { error: { code: "invalid_game_key", message: "Unknown key" } },
          401,
        ),
      );
    const result = await handleValidatePlacement(
      { placement_id: "pl_bad" },
      makeCtx(mockFetch),
    );
    expect(result.isError).toBe(true);
    expect((result.content[0] as { text: string }).text).toContain("401");
  });

  it("returns isError=true on network failure", async () => {
    const mockFetch = vi.fn().mockRejectedValue(new Error("ECONNREFUSED"));
    const result = await handleValidatePlacement(
      { placement_id: "pl_test" },
      makeCtx(mockFetch),
    );
    expect(result.isError).toBe(true);
    expect((result.content[0] as { text: string }).text).toContain(
      "ECONNREFUSED",
    );
  });
});

// ---------------------------------------------------------------------------
// get_theme
// ---------------------------------------------------------------------------

describe("handleGetTheme", () => {
  it("returns themeId on 200 response", async () => {
    const mockFetch = vi
      .fn()
      .mockResolvedValue(makeResponse({ themeId: "default" }));
    const result = await handleGetTheme(
      { placement_id: "pl_test" },
      makeCtx(mockFetch),
    );
    expect(result.isError).toBeFalsy();
    const body = JSON.parse((result.content[0] as { text: string }).text);
    expect(body.themeId).toBe("default");

    // Verify the URL included the placementId query param
    const calledUrl = (mockFetch.mock.calls[0] as unknown[])[0] as string;
    expect(calledUrl).toContain("placementId=pl_test");
  });

  it("returns isError=true on 400 response", async () => {
    const mockFetch = vi
      .fn()
      .mockResolvedValue(
        makeResponse({ error: { code: "missing_param" } }, 400),
      );
    const result = await handleGetTheme(
      { placement_id: "" },
      makeCtx(mockFetch),
    );
    expect(result.isError).toBe(true);
  });

  it("returns isError=true on network failure", async () => {
    const mockFetch = vi
      .fn()
      .mockRejectedValue(new TypeError("Failed to fetch"));
    const result = await handleGetTheme(
      { placement_id: "pl_test" },
      makeCtx(mockFetch),
    );
    expect(result.isError).toBe(true);
    expect((result.content[0] as { text: string }).text).toContain(
      "Failed to fetch",
    );
  });
});

// ---------------------------------------------------------------------------
// docs_links
// ---------------------------------------------------------------------------

describe("handleDocsLinks", () => {
  it("returns a JSON object with expected keys", async () => {
    const result = await handleDocsLinks();
    expect(result.isError).toBeFalsy();
    const links = JSON.parse((result.content[0] as { text: string }).text);
    expect(links).toHaveProperty("quickstart");
    expect(links).toHaveProperty("api_reference");
    expect(links).toHaveProperty("llms_txt");
    expect(links).toHaveProperty("openapi_json");
    // All values should be valid https URLs
    for (const url of Object.values(links)) {
      expect(typeof url).toBe("string");
      expect((url as string).startsWith("https://")).toBe(true);
    }
  });
});
