// ---------------------------------------------------------------------------
// Tests for LevelMomentAd (sdk/core). Focused on the two wire-protocol contracts:
//   1. show() generates an idempotencyKey, enqueues an ImpressionEvent
//      carrying it, AND issues a synchronous-style POST /impressions whose
//      response populates a per-question pendingImpressionIds promise.
//   2. notifyCorrectAnswer / notifyDismissed await that promise so the answer
//      POST sees a real impressionId rather than the empty-string sentinel.
//
// All network calls are driven through a vi.fn() fetch mock so no real API
// connection is required.
// ---------------------------------------------------------------------------

import { describe, it, expect, vi, beforeEach } from "vitest";
import { webcrypto } from "node:crypto";
import { LevelMomentAd } from "./ad.js";
import { ImpressionQueue, MemoryQueueStorage } from "./queue.js";
import type { ImpressionEvent } from "./types.js";

// The SDK's runtime baseline is Node ≥19 (where globalThis.crypto.randomUUID
// exists natively). This dev box may be older; shim it from node:crypto's
// webcrypto so tests exercise the real code path. Production runtimes
// (browsers + Node ≥19 + RN with the standard polyfill) get crypto.randomUUID
// without any shim.
if (typeof globalThis.crypto?.randomUUID !== "function") {
  Object.defineProperty(globalThis, "crypto", {
    value: webcrypto,
    configurable: true,
  });
}

const API_URL = "https://api.test";
const PLACEMENT_ID = "placement-abc";
const STUDENT_TOKEN = "test-token";

function jsonResponse(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

interface FetchCall {
  url: string;
  init?: RequestInit;
  body?: unknown;
}

interface FetchMock {
  fetch: typeof fetch;
  calls: FetchCall[];
}

/**
 * Build a fetch mock that routes by URL substring → response. Captures every
 * call (including the parsed JSON body) so tests can inspect the SDK's wire
 * format. Unknown URLs return 404.
 */
function makeFetch(
  routes: Array<{ match: string; respond: (body: unknown) => Response }>,
): FetchMock {
  const calls: FetchCall[] = [];
  const fetchFn = (async (input: unknown, init?: RequestInit) => {
    const url = typeof input === "string" ? input : (input as Request).url;
    const body =
      init?.body !== undefined ? JSON.parse(init.body as string) : undefined;
    calls.push({ url, init, body });
    const route = routes.find((r) => url.includes(r.match));
    if (!route) return jsonResponse(404, { error: "no route" });
    return route.respond(body);
  }) as unknown as typeof fetch;
  return { fetch: fetchFn, calls };
}

function makeQueue(): ImpressionQueue {
  return new ImpressionQueue(new MemoryQueueStorage(), async () => undefined);
}

/**
 * Drive LevelMomentAd.load → wait for onAdLoaded → return the handle. Uses a
 * minimal flashcard fetch route. Tests append additional routes after.
 */
async function loadFlashcardAd(
  fetchMock: FetchMock,
  queue: ImpressionQueue,
): Promise<LevelMomentAd> {
  return new Promise<LevelMomentAd>((resolve, reject) => {
    LevelMomentAd.load(
      {
        apiUrl: API_URL,
        placementId: PLACEMENT_ID,
        studentToken: STUDENT_TOKEN,
      },
      queue,
      {
        onAdLoaded: (ad) => resolve(ad as LevelMomentAd),
        onAdFailedToLoad: (err) => reject(new Error(err.message)),
      },
      { format: "flashcard" },
      fetchMock.fetch,
    );
  });
}

describe("LevelMomentAd.show — idempotencyKey + impressionId resolution", () => {
  beforeEach(() => {
    vi.useRealTimers();
  });

  it("enqueues an ImpressionEvent carrying a non-empty idempotencyKey", async () => {
    const fetchMock = makeFetch([
      {
        match: "/questions",
        respond: () =>
          jsonResponse(200, {
            question: { id: "q-1", meta: { type: "multiple_choice" } },
          }),
      },
      {
        match: "/impressions",
        respond: () =>
          jsonResponse(202, {
            accepted: true,
            results: [
              {
                idempotencyKey: "captured-by-server",
                impressionId: "imp-1",
              },
            ],
          }),
      },
    ]);

    const queue = makeQueue();
    const enqueueSpy = vi.spyOn(queue, "enqueue");

    const ad = await loadFlashcardAd(fetchMock, queue);
    ad.show({});

    expect(enqueueSpy).toHaveBeenCalledOnce();
    const event = enqueueSpy.mock.calls[0]![0] as ImpressionEvent;
    expect(event.questionId).toBe("q-1");
    expect(event.placementId).toBe(PLACEMENT_ID);
    expect(typeof event.idempotencyKey).toBe("string");
    expect(event.idempotencyKey.length).toBeGreaterThan(0);
  });

  it("Option A: builds an instanceId event (not questionId) when the served question carries instanceId", async () => {
    const fetchMock = makeFetch([
      {
        // The served flashcard is a template-engine instance: its id is an
        // instanceId and the payload carries an `instanceId` discriminator.
        match: "/questions",
        respond: () =>
          jsonResponse(200, {
            question: {
              id: "inst-77",
              meta: { type: "fill_in_blank", prompt: "1 + 1 = {blank}" },
              instanceId: "inst-77",
            },
          }),
      },
      {
        match: "/impressions",
        respond: () =>
          jsonResponse(202, {
            accepted: true,
            results: [
              { idempotencyKey: "captured-by-server", impressionId: "imp-9" },
            ],
          }),
      },
    ]);

    const queue = makeQueue();
    const enqueueSpy = vi.spyOn(queue, "enqueue");

    const ad = await loadFlashcardAd(fetchMock, queue);
    ad.show({});

    expect(enqueueSpy).toHaveBeenCalledOnce();
    const event = enqueueSpy.mock.calls[0]![0] as ImpressionEvent;
    // Exactly one of instanceId / questionId — instanceId set, questionId unset.
    expect(event.instanceId).toBe("inst-77");
    expect(event.questionId).toBeUndefined();
    expect(event.placementId).toBe(PLACEMENT_ID);

    // The synchronous-style POST /impressions also carries instanceId.
    const impCall = fetchMock.calls.find((c) => c.url.includes("/impressions"));
    const sentEvent = (impCall!.body as { events: ImpressionEvent[] })
      .events[0]!;
    expect(sentEvent.instanceId).toBe("inst-77");
    expect(sentEvent.questionId).toBeUndefined();
  });

  it("generates a unique idempotencyKey per show() call", async () => {
    const fetchMock = makeFetch([
      {
        match: "/questions",
        respond: () =>
          jsonResponse(200, {
            question: { id: "q-1", meta: { type: "multiple_choice" } },
          }),
      },
      {
        match: "/impressions",
        respond: (body) => {
          const evt = (body as { events: Array<{ idempotencyKey: string }> })
            .events[0]!;
          return jsonResponse(202, {
            accepted: true,
            results: [
              {
                idempotencyKey: evt.idempotencyKey,
                impressionId: "imp-x",
              },
            ],
          });
        },
      },
    ]);

    const queue1 = makeQueue();
    const enqueue1 = vi.spyOn(queue1, "enqueue");
    const ad1 = await loadFlashcardAd(fetchMock, queue1);
    ad1.show({});

    const queue2 = makeQueue();
    const enqueue2 = vi.spyOn(queue2, "enqueue");
    const ad2 = await loadFlashcardAd(fetchMock, queue2);
    ad2.show({});

    const key1 = (enqueue1.mock.calls[0]![0] as ImpressionEvent).idempotencyKey;
    const key2 = (enqueue2.mock.calls[0]![0] as ImpressionEvent).idempotencyKey;
    expect(key1).not.toBe(key2);
  });

  it("POSTs /impressions at show() time carrying events with idempotencyKey", async () => {
    const fetchMock = makeFetch([
      {
        match: "/questions",
        respond: () =>
          jsonResponse(200, {
            question: { id: "q-1", meta: { type: "multiple_choice" } },
          }),
      },
      {
        match: "/impressions",
        respond: () =>
          jsonResponse(202, {
            accepted: true,
            results: [{ idempotencyKey: "x", impressionId: "imp-1" }],
          }),
      },
    ]);

    const ad = await loadFlashcardAd(fetchMock, makeQueue());
    ad.show({});

    // give the microtask queue time to issue the synchronous POST
    await new Promise((r) => setTimeout(r, 0));

    const impressionsCalls = fetchMock.calls.filter((c) =>
      c.url.includes("/impressions"),
    );
    expect(impressionsCalls).toHaveLength(1);
    const body = impressionsCalls[0]!.body as {
      events: Array<{ idempotencyKey: string; questionId: string }>;
    };
    expect(body.events).toHaveLength(1);
    expect(body.events[0]!.questionId).toBe("q-1");
    expect(typeof body.events[0]!.idempotencyKey).toBe("string");
  });

  it("notifyCorrectAnswer submits an answer carrying the impressionId resolved from POST /impressions", async () => {
    const RESOLVED_ID = "imp-resolved-7";
    const fetchMock = makeFetch([
      {
        match: "/questions",
        respond: () =>
          jsonResponse(200, {
            question: { id: "q-1", meta: { type: "multiple_choice" } },
          }),
      },
      {
        match: "/impressions",
        respond: (body) => {
          const evt = (body as { events: Array<{ idempotencyKey: string }> })
            .events[0]!;
          return jsonResponse(202, {
            accepted: true,
            results: [
              {
                idempotencyKey: evt.idempotencyKey,
                impressionId: RESOLVED_ID,
              },
            ],
          });
        },
      },
      {
        match: "/answers",
        respond: () =>
          jsonResponse(201, {
            outcomes: [{ impressionId: RESOLVED_ID, outcome: "correct" }],
          }),
      },
    ]);

    const ad = await loadFlashcardAd(fetchMock, makeQueue());
    ad.show({});
    ad.notifyCorrectAnswer({});

    // Drain the fire-and-forget IIFE so /answers actually goes out.
    await new Promise((r) => setTimeout(r, 10));

    const answerCalls = fetchMock.calls.filter((c) =>
      c.url.includes("/answers"),
    );
    expect(answerCalls).toHaveLength(1);
    const body = answerCalls[0]!.body as {
      answers: Array<{ impressionId: string }>;
    };
    // The decisive assertion: the answer carries the SERVER-MINTED
    // impressionId resolved from the synchronous POST /impressions, NOT the
    // empty-string sentinel that the strict student ownership check rejects.
    expect(body.answers[0]!.impressionId).toBe(RESOLVED_ID);
  });

  it("notifyDismissed also awaits the resolved impressionId", async () => {
    const RESOLVED_ID = "imp-dismiss-id";
    const fetchMock = makeFetch([
      {
        match: "/questions",
        respond: () =>
          jsonResponse(200, {
            question: { id: "q-1", meta: { type: "multiple_choice" } },
          }),
      },
      {
        match: "/impressions",
        respond: (body) => {
          const evt = (body as { events: Array<{ idempotencyKey: string }> })
            .events[0]!;
          return jsonResponse(202, {
            accepted: true,
            results: [
              {
                idempotencyKey: evt.idempotencyKey,
                impressionId: RESOLVED_ID,
              },
            ],
          });
        },
      },
      {
        match: "/answers",
        respond: () =>
          jsonResponse(201, {
            outcomes: [{ impressionId: RESOLVED_ID, outcome: "incorrect" }],
          }),
      },
    ]);

    const ad = await loadFlashcardAd(fetchMock, makeQueue());
    ad.show({});
    ad.notifyDismissed({}, undefined, 1);
    await new Promise((r) => setTimeout(r, 10));

    const answerCalls = fetchMock.calls.filter((c) =>
      c.url.includes("/answers"),
    );
    expect(answerCalls).toHaveLength(1);
    const body = answerCalls[0]!.body as {
      answers: Array<{ impressionId: string; selectedIndex?: number }>;
    };
    expect(body.answers[0]!.impressionId).toBe(RESOLVED_ID);
    expect(body.answers[0]!.selectedIndex).toBe(1);
  });

  it("advanceSession submits an answer carrying the impressionId resolved from POST /impressions", async () => {
    const RESOLVED_ID = "imp-session-42";
    const fetchMock = makeFetch([
      {
        match: "/break-sessions",
        respond: () =>
          jsonResponse(200, {
            sessionId: "sess-1",
            format: "quiz",
            questions: [
              {
                id: "q-1",
                meta: { type: "multiple_choice" },
                conceptId: "c-1",
                unitId: "u-1",
              },
              {
                id: "q-2",
                meta: { type: "multiple_choice" },
                conceptId: "c-1",
                unitId: "u-1",
              },
            ],
            totalQuestions: 2,
            currentIndex: 0,
            passThreshold: 70,
            throttled: false,
          }),
      },
      {
        match: "/impressions",
        respond: (body) => {
          const evt = (body as { events: Array<{ idempotencyKey: string }> })
            .events[0]!;
          return jsonResponse(202, {
            accepted: true,
            results: [
              {
                idempotencyKey: evt.idempotencyKey,
                impressionId: RESOLVED_ID,
              },
            ],
          });
        },
      },
      {
        match: "/answers",
        respond: () =>
          jsonResponse(201, {
            outcomes: [{ impressionId: RESOLVED_ID, outcome: "correct" }],
          }),
      },
    ]);

    // Load a session-based (quiz) ad. We use the load API directly here
    // rather than loadFlashcardAd, since advanceSession only applies to
    // session-based formats.
    const ad = await new Promise<LevelMomentAd>((resolve, reject) => {
      LevelMomentAd.load(
        {
          apiUrl: API_URL,
          placementId: PLACEMENT_ID,
          studentToken: STUDENT_TOKEN,
        },
        makeQueue(),
        {
          onAdLoaded: (a) => resolve(a as LevelMomentAd),
          onAdFailedToLoad: (err) => reject(new Error(err.message)),
        },
        { format: "quiz" },
        fetchMock.fetch,
      );
    });

    // show() the first question so the impressionId resolver is populated.
    ad.show({});

    // advanceSession submits the answer — the decisive assertion is that
    // it picks up the RESOLVED impressionId from the prior show()'s
    // pendingImpressionIds, NOT the empty-string sentinel.
    const result = await ad.advanceSession({
      answeredAt: new Date().toISOString(),
      selectedIndex: 0,
    });

    // We advanced past q-1 → q-2 should now be current.
    expect(result.next?.id).toBe("q-2");

    const answerCalls = fetchMock.calls.filter((c) =>
      c.url.includes("/answers"),
    );
    expect(answerCalls).toHaveLength(1);
    const body = answerCalls[0]!.body as {
      answers: Array<{ impressionId: string }>;
    };
    expect(body.answers[0]!.impressionId).toBe(RESOLVED_ID);
  });

  it("advanceSession does NOT advance on a failed submission — returns a retriable result for the SAME question", async () => {
    const fetchMock = makeFetch([
      {
        match: "/break-sessions",
        respond: () =>
          jsonResponse(200, {
            sessionId: "sess-r",
            format: "quiz",
            questions: [
              {
                id: "q-1",
                meta: { type: "multiple_choice" },
                conceptId: "c-1",
                unitId: "u-1",
              },
              {
                id: "q-2",
                meta: { type: "multiple_choice" },
                conceptId: "c-1",
                unitId: "u-1",
              },
            ],
            totalQuestions: 2,
            currentIndex: 0,
            passThreshold: 70,
            throttled: false,
          }),
      },
      {
        match: "/impressions",
        respond: (body) =>
          jsonResponse(202, {
            accepted: true,
            results: [
              {
                idempotencyKey: (
                  body as { events: Array<{ idempotencyKey: string }> }
                ).events[0]!.idempotencyKey,
                impressionId: "imp-r",
              },
            ],
          }),
      },
      // The answer POST fails (network / 500) — advanceSession must NOT advance.
      { match: "/answers", respond: () => jsonResponse(500, {}) },
    ]);

    const ad = await new Promise<LevelMomentAd>((resolve, reject) => {
      LevelMomentAd.load(
        {
          apiUrl: API_URL,
          placementId: PLACEMENT_ID,
          studentToken: STUDENT_TOKEN,
        },
        makeQueue(),
        {
          onAdLoaded: (a) => resolve(a as LevelMomentAd),
          onAdFailedToLoad: (err) => reject(new Error(err.message)),
        },
        { format: "quiz" },
        fetchMock.fetch,
      );
    });

    ad.show({});
    const result = await ad.advanceSession({
      answeredAt: new Date().toISOString(),
      selectedIndex: 0,
    });

    // Retriable, still on q-1 — the student is NOT told they were wrong and the
    // session did not move on.
    expect(result.retriable).toBe(true);
    expect(result.outcome).toBe("skipped");
    expect(result.next?.id).toBe("q-1");
    expect(ad.question?.id).toBe("q-1");
    // Summary reflects zero answered (index did not advance).
    expect(ad.getSessionSummary()?.totalAnswered).toBe(0);
  });

  it("falls back to empty impressionId if POST /impressions fails (queue still retries)", async () => {
    const fetchMock = makeFetch([
      {
        match: "/questions",
        respond: () =>
          jsonResponse(200, {
            question: { id: "q-1", meta: { type: "multiple_choice" } },
          }),
      },
      // Synchronous POST /impressions fails — the queued event remains for
      // background retry; the answer below sees an empty impressionId.
      { match: "/impressions", respond: () => jsonResponse(500, {}) },
      {
        match: "/answers",
        respond: () =>
          jsonResponse(403, {
            error: { code: "forbidden", message: "..." },
          }),
      },
    ]);

    const queue = makeQueue();
    const enqueueSpy = vi.spyOn(queue, "enqueue");
    const ad = await loadFlashcardAd(fetchMock, queue);
    ad.show({});
    ad.notifyCorrectAnswer({});
    await new Promise((r) => setTimeout(r, 10));

    // The queued event is still in the queue (background retry path) — the
    // synchronous POST failure does NOT swallow the impression.
    expect(enqueueSpy).toHaveBeenCalledOnce();

    // The answer carries "" (no resolved id) — the server's strict student
    // ownership check is what surfaces the failure as 403.
    const answerCalls = fetchMock.calls.filter((c) =>
      c.url.includes("/answers"),
    );
    expect(answerCalls).toHaveLength(1);
    const body = answerCalls[0]!.body as {
      answers: Array<{ impressionId: string }>;
    };
    expect(body.answers[0]!.impressionId).toBe("");
  });
});
