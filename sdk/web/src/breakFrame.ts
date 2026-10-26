import {
  BRIDGE_PROTOCOL_VERSION,
  SDK_VERSION,
  isBridgeMessage,
  type HostStoreClaims,
} from "@levelmoment/sdk-core";

import { mountLoadingLayer, type MountedChrome } from "./chrome.js";
import { browserReportsOffline } from "./environment.js";

/** A browser host has no store account: always `web`, never a storefront. */
const WEB_STORE_CLAIMS: HostStoreClaims = { platform: "web", storefront: null };
// BreakFrame — the iframe + postMessage plumbing every hosted surface shares.
//
// Two callers open the hosted page for different reasons: LevelMomentWebAd
// shows a break, and the startup gate (client.ensureSignedIn / isSignedIn)
// opens ?mode=gate / ?mode=check. They need the same three guarantees —
// messages only from the hosted origin, a pre-`ready` watchdog so a page that
// never loads cannot leave a fullscreen iframe over the game forever, and a
// teardown that runs exactly once — so those live here rather than in two
// places that could drift.

/**
 * The terminal-event protocol posted by the hosted /break page. Kept in sync
 * with the HostMessage union documented at the top of
 * platform/web/app/break/page.tsx (and mirrors sdk/react-native's HostMessage).
 */
export type HostMessage =
  | { type: "ready" }
  | { type: "earnedReward"; payload: { rewardId: string; earnedAt: string } }
  /** Sign-in gate only: this device holds a valid credential for the game. */
  | { type: "signedIn" }
  | { type: "dismissed" }
  | { type: "error"; payload: { code: string; message: string } }
  /**
   * The page is asking for the credential this host holds, instead of reading
   * one off the URL. Answered with {@link BreakFrame.deliverCredential} — with
   * an empty token when there is nothing to hand over, so the page can stop
   * waiting and get on with its own stored credential.
   */
  | { type: "needCredential" }
  /**
   * Pairing minted a credential. Only ever posted to a host that asked for
   * custody, which the web adapter never does: the credential belongs in the
   * hosted origin's own storage, where it already is.
   */
  | { type: "credentialIssued"; payload: { token: string } }
  /** The page threw its stored credential away. */
  | { type: "credentialInvalid" };

/** What a host answers `needCredential` with. */
export interface CredentialReply {
  /** The credential the host holds, or "" when it holds none. */
  token: string;
  /**
   * Ask to be told about credentials the page mints or discards
   * (`credentialIssued` / `credentialInvalid`). Only a host with somewhere
   * better to keep them than the hosted origin's own storage — a native
   * keychain — should set it.
   */
  custody: boolean;
  customData?: string;
  slotType?: string;
  dimensions?: Record<string, string | number>;
}

/**
 * Why a hosted frame ended before its page posted `ready`:
 * - `timeout` — the page did not post `ready` within the load watchdog.
 * - `offline` — the browser reported no connection, so no frame was mounted.
 * - `closed` — the player closed the loading layer.
 */
export type LoadFailure = "timeout" | "offline" | "closed";

/** Default pre-`ready` load-timeout for the hosted /break page (ms). */
export const DEFAULT_LOAD_TIMEOUT_MS = 15000;

const VISIBLE_STYLE =
  "position:fixed;inset:0;width:100%;height:100%;border:0;z-index:2147483647";
// The headless credential check has no UI. Keep the frame in the document
// (it must actually load) but out of layout, out of the a11y tree, and
// unreachable by tab.
const HIDDEN_STYLE =
  "position:fixed;top:0;left:0;width:0;height:0;border:0;opacity:0;pointer-events:none";

export interface BreakFrameOptions {
  /** Fully-built hosted-page URL. Its origin is the only accepted sender. */
  url: string;
  /** Accessible title for the iframe. */
  title: string;
  /** Open a 0x0 invisible frame instead of a fullscreen one. */
  hidden?: boolean;
  /** Pre-`ready` watchdog in ms. 0 or negative disables it. */
  loadTimeoutMs?: number;
  /**
   * Every validated message from the hosted page, `ready` included. The
   * watchdog is already cancelled by the time a `ready` reaches here.
   * Call `frame.close()` from this handler on a terminal message.
   */
  onMessage: (msg: HostMessage) => void;
  /**
   * The frame ended before the hosted page posted `ready`: the watchdog fired,
   * the browser was offline when the frame was asked for, or the player closed
   * the loading layer. The frame is already closed when this runs. An offline
   * frame is never mounted, and this runs on a microtask after `open()`
   * returns.
   */
  onLoadTimeout: (reason: LoadFailure) => void;
}

/**
 * Resolve the origin the hosted page will post from. `breakUrl` is resolved
 * against the current location so a relative same-origin "/break" yields this
 * page's origin, matching how the hosted page posts to window.parent.
 */
export function expectedOrigin(breakUrl: string): string {
  const base = typeof window !== "undefined" ? window.location.href : undefined;
  return new URL(breakUrl, base).origin;
}

export class BreakFrame {
  private _iframe: HTMLIFrameElement | null = null;
  private _listener: ((event: MessageEvent) => void) | null = null;
  private _timer: ReturnType<typeof setTimeout> | null = null;
  private _loading: MountedChrome | null = null;
  private _closed = false;
  /** The hosted origin: the only accepted sender, and the only target. */
  private _origin = "";

  private constructor() {}

  /**
   * Mount the frame, start listening, and arm the watchdog.
   *
   * When the browser reports no connection, nothing is mounted: the page
   * could not load, and a fullscreen frame over the game would only show the
   * browser's error page until the watchdog fired. The frame comes back
   * already closed and `onLoadTimeout("offline")` runs on a microtask.
   */
  static open(options: BreakFrameOptions): BreakFrame {
    const frame = new BreakFrame();
    if (browserReportsOffline()) {
      frame._closed = true;
      queueMicrotask(() => options.onLoadTimeout("offline"));
      return frame;
    }
    const origin = expectedOrigin(options.url);
    frame._origin = origin;

    const listener = (event: MessageEvent): void => {
      // Ignore anything from another frame or window. Origin alone is not
      // enough: a game may have several Level Moment frames alive at once — a
      // break and a hidden sign-in check, say — and they all post from the same
      // origin, so an origin-only check would let one frame's `dismissed`
      // resolve the other's promise. Identity of the sending window is what
      // actually distinguishes them. A frame that has been torn down, or has
      // not created its window yet, has no contentWindow and can match nothing.
      if (event.origin !== origin) return;
      const source = frame._iframe?.contentWindow;
      if (!source || event.source !== source) return;
      const msg = event.data as HostMessage;
      if (!isBridgeMessage(msg)) return;
      if (frame._closed) return;
      if (msg.type === "ready") {
        // The hosted page is up and now owns the lifecycle. There is
        // intentionally NO post-`ready` timeout: a student thinking through a
        // break, or a parent walking to another room to scan a code, must never
        // be force-closed.
        frame._clearTimer();
        frame._removeLoading();
      }
      options.onMessage(msg);
    };
    frame._listener = listener;
    window.addEventListener("message", listener);

    const iframe = document.createElement("iframe");
    iframe.src = options.url;
    iframe.setAttribute("referrerpolicy", "no-referrer");
    iframe.setAttribute(
      "sandbox",
      "allow-scripts allow-same-origin allow-forms allow-popups allow-popups-to-escape-sandbox",
    );
    iframe.setAttribute("title", options.title);
    if (options.hidden) iframe.setAttribute("aria-hidden", "true");
    iframe.style.cssText = options.hidden ? HIDDEN_STYLE : VISIBLE_STYLE;
    frame._iframe = iframe;
    document.body.appendChild(iframe);

    // Until `ready`, the player sees the SDK's own spinner and close button
    // instead of a blank frame or the browser's error page. Closing ends the
    // frame exactly as the watchdog would. The headless check has no UI.
    if (!options.hidden) {
      frame._loading = mountLoadingLayer(() => {
        if (frame._closed) return;
        frame.close();
        options.onLoadTimeout("closed");
      });
    }

    const timeoutMs = options.loadTimeoutMs ?? DEFAULT_LOAD_TIMEOUT_MS;
    if (timeoutMs > 0) {
      frame._timer = setTimeout(() => {
        if (frame._closed) return;
        frame.close();
        options.onLoadTimeout("timeout");
      }, timeoutMs);
    }

    return frame;
  }

  /** True once close() has run. Further messages are ignored. */
  get closed(): boolean {
    return this._closed;
  }

  /**
   * Answer the page's `needCredential`. Posted INTO the frame, addressed to
   * the hosted origin only — a credential must not be readable by whatever
   * else the game has embedded, and `targetOrigin` is the browser's guarantee
   * of that. No-op once the frame is closed.
   *
   * Always answer, even holding nothing: an empty token ends the page's wait
   * immediately instead of letting it run out the clock.
   */
  deliverCredential(reply: CredentialReply): void {
    if (this._closed) return;
    const target = this._iframe?.contentWindow;
    if (!target) return;
    target.postMessage(
      {
        type: "credential",
        payload: {
          ...reply,
          // Store claims (HostStoreClaims in sdk-core). This adapter always
          // runs in a browser, so it says so explicitly: the page reads a
          // reply with no platform as a missing claim, never as `web`.
          ...WEB_STORE_CLAIMS,
          protocolVersion: BRIDGE_PROTOCOL_VERSION,
          sdkVersion: SDK_VERSION,
        },
      },
      this._origin,
    );
  }

  /**
   * Remove the iframe and the loading layer, drop the listener, cancel the
   * watchdog. Idempotent.
   */
  close(): void {
    if (this._closed) return;
    this._closed = true;
    this._clearTimer();
    this._removeLoading();
    if (this._listener) {
      window.removeEventListener("message", this._listener);
      this._listener = null;
    }
    if (this._iframe?.parentNode) {
      this._iframe.parentNode.removeChild(this._iframe);
    }
    this._iframe = null;
  }

  private _removeLoading(): void {
    this._loading?.remove();
    this._loading = null;
  }

  private _clearTimer(): void {
    if (this._timer !== null) {
      clearTimeout(this._timer);
      this._timer = null;
    }
  }
}
