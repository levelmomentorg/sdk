// ---------------------------------------------------------------------------
// Impression Queue
// Buffers impression events locally and flushes them to the API in the
// background. Ensures no impressions (= developer CPM payments) are dropped
// on network interruption.
//
// This is an internal implementation detail — game developers interact with
// LevelMomentAd.load() / show(), not the queue directly.
// ---------------------------------------------------------------------------

import type { ImpressionEvent } from "./types.js";

export interface QueueStorage {
  load(): ImpressionEvent[];
  save(events: ImpressionEvent[]): void;
}

/** Fallback in-memory storage for environments without persistence. */
export class MemoryQueueStorage implements QueueStorage {
  private events: ImpressionEvent[] = [];

  load(): ImpressionEvent[] {
    return [...this.events];
  }

  save(events: ImpressionEvent[]): void {
    this.events = [...events];
  }
}

export class ImpressionQueue {
  private readonly storage: QueueStorage;
  private flushInterval: ReturnType<typeof setInterval> | null = null;

  constructor(
    storage: QueueStorage,
    private readonly flush: (events: ImpressionEvent[]) => Promise<void>,
    private readonly intervalMs = 10_000,
  ) {
    this.storage = storage;
  }

  enqueue(event: ImpressionEvent): void {
    const events = this.storage.load();
    events.push(event);
    this.storage.save(events);
  }

  start(): void {
    if (this.flushInterval !== null) return;
    this.flushInterval = setInterval(
      () => void this.tryFlush(),
      this.intervalMs,
    );
  }

  stop(): void {
    if (this.flushInterval === null) return;
    clearInterval(this.flushInterval);
    this.flushInterval = null;
  }

  async tryFlush(): Promise<void> {
    const events = this.storage.load();
    if (events.length === 0) return;

    try {
      await this.flush(events);
      this.storage.save([]);
    } catch {
      // Leave events in queue; will retry on next interval.
    }
  }
}
