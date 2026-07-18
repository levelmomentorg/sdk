import {
  describe,
  it,
  expect,
  vi,
  beforeEach,
  afterEach,
  type Mock,
} from "vitest";
import { MemoryQueueStorage, ImpressionQueue } from "./queue.js";
import type { ImpressionEvent } from "./types.js";

const makeEvent = (questionId = "q-1"): ImpressionEvent => ({
  questionId,
  placementId: "placement-abc",
  shownAt: "2026-04-10T12:00:00.000Z",
  durationMs: 1000,
  idempotencyKey: `idem-${questionId}`,
});

// ---------------------------------------------------------------------------
// MemoryQueueStorage
// ---------------------------------------------------------------------------

describe("MemoryQueueStorage", () => {
  it("starts empty", () => {
    const storage = new MemoryQueueStorage();
    expect(storage.load()).toEqual([]);
  });

  it("saves and loads events", () => {
    const storage = new MemoryQueueStorage();
    const event = makeEvent();
    storage.save([event]);
    expect(storage.load()).toEqual([event]);
  });

  it("overwrites previous state on save", () => {
    const storage = new MemoryQueueStorage();
    storage.save([makeEvent("q-1")]);
    storage.save([makeEvent("q-2")]);
    expect(storage.load()).toHaveLength(1);
    expect(storage.load()[0]!.questionId).toBe("q-2");
  });

  it("load returns a copy — mutations do not affect stored data", () => {
    const storage = new MemoryQueueStorage();
    storage.save([makeEvent()]);
    const first = storage.load();
    first.push(makeEvent("q-extra"));
    expect(storage.load()).toHaveLength(1);
  });

  it("save with empty array clears stored events", () => {
    const storage = new MemoryQueueStorage();
    storage.save([makeEvent()]);
    storage.save([]);
    expect(storage.load()).toEqual([]);
  });
});

// ---------------------------------------------------------------------------
// ImpressionQueue
// ---------------------------------------------------------------------------

describe("ImpressionQueue", () => {
  let storage: MemoryQueueStorage;
  // Typed precisely so it satisfies ImpressionQueue's flush parameter.
  let flush: Mock<[ImpressionEvent[]], Promise<void>>;
  let queue: ImpressionQueue;

  beforeEach(() => {
    storage = new MemoryQueueStorage();
    flush = vi
      .fn<[ImpressionEvent[]], Promise<void>>()
      .mockResolvedValue(undefined);
    // Short interval so fake-timer tests don't need huge advances.
    queue = new ImpressionQueue(storage, flush, 100);
  });

  afterEach(() => {
    queue.stop();
    vi.restoreAllMocks();
  });

  // ---- enqueue ----

  it("enqueue adds an event to storage", () => {
    const event = makeEvent();
    queue.enqueue(event);
    expect(storage.load()).toEqual([event]);
  });

  it("enqueue accumulates multiple events", () => {
    queue.enqueue(makeEvent("q-1"));
    queue.enqueue(makeEvent("q-2"));
    expect(storage.load()).toHaveLength(2);
    expect(storage.load().map((e) => e.questionId)).toEqual(["q-1", "q-2"]);
  });

  // ---- tryFlush ----

  it("tryFlush calls flush with all queued events", async () => {
    const e1 = makeEvent("q-1");
    const e2 = makeEvent("q-2");
    queue.enqueue(e1);
    queue.enqueue(e2);
    await queue.tryFlush();
    expect(flush).toHaveBeenCalledOnce();
    expect(flush).toHaveBeenCalledWith([e1, e2]);
  });

  it("tryFlush clears storage on successful flush", async () => {
    queue.enqueue(makeEvent());
    await queue.tryFlush();
    expect(storage.load()).toEqual([]);
  });

  it("tryFlush is a no-op when the queue is empty", async () => {
    await queue.tryFlush();
    expect(flush).not.toHaveBeenCalled();
  });

  it("tryFlush leaves events in storage when flush throws (retry on next interval)", async () => {
    flush.mockRejectedValueOnce(new Error("Network error"));
    queue.enqueue(makeEvent());
    await queue.tryFlush();
    // Events must still be queued for the next retry.
    expect(storage.load()).toHaveLength(1);
  });

  it("tryFlush retries successfully after a prior failure", async () => {
    flush.mockRejectedValueOnce(new Error("transient"));
    queue.enqueue(makeEvent());

    await queue.tryFlush(); // fails — events remain
    expect(storage.load()).toHaveLength(1);

    await queue.tryFlush(); // succeeds on second attempt
    expect(flush).toHaveBeenCalledTimes(2);
    expect(storage.load()).toEqual([]);
  });

  // ---- start / stop ----

  it("start triggers flush after the configured interval", async () => {
    vi.useFakeTimers();
    queue.start();
    queue.enqueue(makeEvent());
    // No flush before the interval elapses.
    expect(flush).not.toHaveBeenCalled();
    // Advance past one interval and drain the microtask queue.
    await vi.advanceTimersByTimeAsync(100);
    expect(flush).toHaveBeenCalledOnce();
    vi.useRealTimers();
  });

  it("start is idempotent — calling twice does not register a second interval", async () => {
    vi.useFakeTimers();
    queue.start();
    queue.start(); // second call should be a no-op
    queue.enqueue(makeEvent());
    await vi.advanceTimersByTimeAsync(100);
    // Flush is called exactly once, not twice.
    expect(flush).toHaveBeenCalledOnce();
    vi.useRealTimers();
  });

  it("stop prevents further scheduled flushes", async () => {
    vi.useFakeTimers();
    queue.start();
    queue.stop();
    queue.enqueue(makeEvent());
    await vi.advanceTimersByTimeAsync(500);
    expect(flush).not.toHaveBeenCalled();
    vi.useRealTimers();
  });

  it("stop is safe to call when not started", () => {
    expect(() => queue.stop()).not.toThrow();
  });

  // ---- exponential back-off (timer-driven path via start()) ----
  // Back-off applies only to the automatic timer ticks, not to direct
  // tryFlush() calls (which platform SDKs use on app background/close).
  // Ladder: min(intervalMs × 2^(failures − 1), 5 min) — the first retry
  // waits one normal interval, then each further failure doubles.

  it("retries at the very next scheduled tick after a single failure (1× back-off)", async () => {
    vi.useFakeTimers();

    flush.mockRejectedValueOnce(new Error("transient"));
    queue.enqueue(makeEvent());
    queue.start();

    // First tick at t=100ms fires and fails.
    // Back-off delay = intervalMs * 2^0 = 100ms → nextAttemptAt = 200ms.
    await vi.advanceTimersByTimeAsync(100);
    expect(flush).toHaveBeenCalledTimes(1);

    // Second tick at t=200ms: Date.now()=200 ≥ nextAttemptAt=200 → fires (a
    // single failure adds no extra skip) and this time succeeds.
    await vi.advanceTimersByTimeAsync(100);
    expect(flush).toHaveBeenCalledTimes(2);
    expect(storage.load()).toEqual([]);

    vi.useRealTimers();
  });

  it("skips a tick only after the second consecutive failure (2× back-off, then 4×)", async () => {
    vi.useFakeTimers();

    flush.mockRejectedValue(new Error("persistent"));
    queue.enqueue(makeEvent());
    queue.start();

    // Failure 1 at t=100ms → delay=100ms (2^0) → nextAttemptAt=200ms.
    await vi.advanceTimersByTimeAsync(100);
    expect(flush).toHaveBeenCalledTimes(1);

    // t=200ms: tick fires (200 ≥ 200) → failure 2 → delay=200ms (2^1) →
    // nextAttemptAt=400ms.
    await vi.advanceTimersByTimeAsync(100);
    expect(flush).toHaveBeenCalledTimes(2);

    // t=300ms: tick skipped (300 < 400).
    await vi.advanceTimersByTimeAsync(100);
    expect(flush).toHaveBeenCalledTimes(2);

    // t=400ms: tick fires (400 ≥ 400) → failure 3 → delay=400ms (2^2) →
    // nextAttemptAt=800ms.
    await vi.advanceTimersByTimeAsync(100);
    expect(flush).toHaveBeenCalledTimes(3);

    // t=500–700ms: three ticks each skipped (< 800ms).
    await vi.advanceTimersByTimeAsync(300);
    expect(flush).toHaveBeenCalledTimes(3);

    // t=800ms: tick fires (800 ≥ 800) → failure 4.
    await vi.advanceTimersByTimeAsync(100);
    expect(flush).toHaveBeenCalledTimes(4);

    vi.useRealTimers();
  });

  it("resets back-off state after a successful scheduled flush", async () => {
    vi.useFakeTimers();

    flush.mockRejectedValueOnce(new Error("once")).mockResolvedValue(undefined);
    queue.enqueue(makeEvent());
    queue.start();

    // Failure 1 at t=100ms → nextAttemptAt=200ms (1× back-off).
    await vi.advanceTimersByTimeAsync(100);
    expect(flush).toHaveBeenCalledTimes(1);
    expect(storage.load()).toHaveLength(1); // still queued

    // t=200ms: tick fires → success → back-off cleared.
    await vi.advanceTimersByTimeAsync(100);
    expect(flush).toHaveBeenCalledTimes(2);
    expect(storage.load()).toEqual([]); // cleared on success

    // After reset, the very next tick (t=300ms) fires normally.
    queue.enqueue(makeEvent("q-fresh"));
    await vi.advanceTimersByTimeAsync(100);
    expect(flush).toHaveBeenCalledTimes(3);

    vi.useRealTimers();
  });

  it("caps back-off at 5 minutes: 20 failures clear within 85 min of fake time", async () => {
    vi.useFakeTimers();

    // Use the production 10s interval so this only drives a few hundred fake
    // timer ticks (a 100ms interval over ~85 min would be ~50k ticks and can
    // starve under parallel CI load).
    const q = new ImpressionQueue(storage, flush, 10_000);

    // Reject 20 times, then succeed on the 21st attempt. With the 5-min cap the
    // 20 retries drain at ~80 min of fake time; without it, the uncapped delay
    // after 20 failures would be 10s × 2^19 ≈ 60 days.
    for (let i = 0; i < 20; i++) {
      flush.mockRejectedValueOnce(new Error("fail"));
    }
    flush.mockResolvedValue(undefined);
    q.enqueue(makeEvent());
    q.start();

    // Advance 85 min of fake time — the queue must be empty by then.
    await vi.advanceTimersByTimeAsync(85 * 60 * 1000);
    expect(storage.load()).toEqual([]);

    q.stop();
    vi.useRealTimers();
  });

  it("a failed direct tryFlush does not create a back-off window (scheduled ticks unaffected)", async () => {
    vi.useFakeTimers();

    // Platform SDKs call tryFlush() directly on app background/close. A failure
    // there must NOT set a back-off window, so the next scheduled tick still
    // fires on its normal cadence.
    flush
      .mockRejectedValueOnce(new Error("direct fail"))
      .mockResolvedValue(undefined);
    queue.enqueue(makeEvent());

    await queue.tryFlush(); // direct call fails
    expect(flush).toHaveBeenCalledTimes(1);
    expect(storage.load()).toHaveLength(1); // still queued

    queue.start();
    // The very first scheduled tick (t=100ms) fires — no back-off window was
    // created by the direct failure — and succeeds.
    await vi.advanceTimersByTimeAsync(100);
    expect(flush).toHaveBeenCalledTimes(2);
    expect(storage.load()).toEqual([]);

    vi.useRealTimers();
  });
});
