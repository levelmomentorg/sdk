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
});
