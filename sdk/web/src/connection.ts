// Require-internet mode: block play with a full-screen message while the
// device has no network connection, and give it back when the connection
// returns.
//
// "Offline" means the browser reports no connection (`navigator.onLine` and
// the `online` / `offline` events). It never means Level Moment's servers
// failed: an outage on our side ends the break and the game keeps playing, as
// it always has. Our outage must never lock a game.

import { mountNoConnection, type MountedChrome } from "./chrome.js";
import { browserReportsOffline } from "./environment.js";

export interface ConnectionWatchOptions {
  /** Named in the message: "{gameName} needs an internet connection." */
  gameName?: string;
  /**
   * Called with `false` when the message appears and `true` when it goes
   * away. Pause the game loop on `false` and resume on `true`.
   */
  onConnectionChange?: (online: boolean) => void;
}

export class ConnectionWatcher {
  private _online = true;
  private _message: MountedChrome | null = null;
  private _disposed = false;
  private readonly _onOnline = (): void => this._set(true);
  private readonly _onOffline = (): void => this._set(false);

  constructor(private readonly _options: ConnectionWatchOptions = {}) {
    if (typeof window === "undefined") return;
    window.addEventListener("online", this._onOnline);
    window.addEventListener("offline", this._onOffline);
    if (browserReportsOffline()) {
      this._online = false;
      this._show();
      // Deferred so a game that reads its client inside the callback has the
      // client by then: this runs during initialize().
      queueMicrotask(() => {
        if (!this._disposed && !this._online) {
          this._options.onConnectionChange?.(false);
        }
      });
    }
  }

  /** Whether the message is down: the browser reports a connection. */
  get online(): boolean {
    return this._online;
  }

  /** Read the browser's connection state again, as Try again does. */
  recheck(): void {
    this._set(!browserReportsOffline());
  }

  /** Remove the message and stop listening. Idempotent. */
  dispose(): void {
    if (this._disposed) return;
    this._disposed = true;
    if (typeof window !== "undefined") {
      window.removeEventListener("online", this._onOnline);
      window.removeEventListener("offline", this._onOffline);
    }
    this._message?.remove();
    this._message = null;
  }

  private _set(online: boolean): void {
    if (this._disposed || online === this._online) return;
    this._online = online;
    if (online) {
      this._message?.remove();
      this._message = null;
    } else {
      this._show();
    }
    this._options.onConnectionChange?.(online);
  }

  private _show(): void {
    if (this._message || typeof document === "undefined") return;
    this._message = mountNoConnection(this._options.gameName, () =>
      this.recheck(),
    );
  }
}
