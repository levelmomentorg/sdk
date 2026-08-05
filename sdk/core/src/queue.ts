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

/** Maximum back-off delay between scheduled flush retries (5 minutes). */
const MAX_BACKOFF_MS = 5 * 60 * 1000;

export class ImpressionQueue {
  private readonly storage: QueueStorage;
  private flushInterval: ReturnType<typeof setInterval> | null = null;

  /** Count of consecutive flush failures since the last success. */
  private _consecutiveFailures = 0;
  /** Earliest timestamp (Date.now()) at which a scheduled flush may fire. */
  private _nextAttemptAt = 0;

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
      () => void this._scheduledFlush(),
      this.intervalMs,
    );
  }

  stop(): void {
    if (this.flushInterval === null) return;
    clearInterval(this.flushInterval);
    this.flushInterval = null;
  }

  /**
   * Immediately flush all queued events to the server. Called by platform SDKs
   * on app background / close. Always attempts the flush regardless of any
   * ongoing back-off window, and never *creates* a back-off window itself — a
   * failed direct flush must not suppress subsequent scheduled ticks. Back-off
   * is applied only to the automatic timer ticks driven by `start()`. A
   * successful flush via any path still clears any existing back-off window.
   */
  async tryFlush(): Promise<void> {
    await this._attemptFlush();
  }

  /**
   * Run a single flush attempt. Returns `true` when the batch was delivered
   * (and the queue drained), `false` when the flush failed. Resets the
   * back-off window on success; never sets one on failure — that bookkeeping
   * lives in `_scheduledFlush` so only automatic ticks back off.
   */
  private async _attemptFlush(): Promise<boolean> {
    const events = this.storage.load();
    if (events.length === 0) return true;

    try {
      await this.flush(events);
      this.storage.save([]);
      // Reset back-off state so the next scheduled tick fires normally.
      this._consecutiveFailures = 0;
      this._nextAttemptAt = 0;
      return true;
    } catch {
      // Leave events in queue; the scheduled path decides on back-off.
      return false;
    }
  }

  /**
   * Called on each timer tick. Respects the exponential back-off window set by
   * prior *scheduled* failures — skips the tick if we are still inside the
   * window. Failure bookkeeping lives here (not in `tryFlush`) so back-off
   * applies only to automatic ticks. The ladder is
   * `min(intervalMs × 2^(failures − 1), 5 min)`: the first retry waits one
   * normal interval, then each further consecutive failure doubles the delay.
   */
  private async _scheduledFlush(): Promise<void> {
    if (Date.now() < this._nextAttemptAt) return;
    const ok = await this._attemptFlush();
    if (!ok) {
      this._consecutiveFailures++;
      this._nextAttemptAt =
        Date.now() +
        Math.min(
          this.intervalMs * Math.pow(2, this._consecutiveFailures - 1),
          MAX_BACKOFF_MS,
        );
    }
  }
}
