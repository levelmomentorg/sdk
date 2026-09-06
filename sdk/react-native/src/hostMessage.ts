import {
  BRIDGE_PROTOCOL_VERSION,
  SDK_VERSION,
  HOSTED_BREAK_URL,
  sameHostedOrigin,
} from "@levelmoment/sdk-core";
// The terminal-event protocol posted by the hosted /break page.
//
// Kept in a module of its own — with no react-native import — so the URL
// builder, the message mapping, and the startup gate can all be unit-tested as
// plain TypeScript. Mirrors the HostMessage union documented at the top of
// platform/web/app/break/page.tsx and sdk/web/src/breakFrame.ts.

export type HostMessage =
  | { type: "ready" }
  | { type: "earnedReward"; payload: { amount: 0 | 1; rewardId?: string } }
  /** Sign-in gate only: this device holds a valid credential for the game. */
  | { type: "signedIn" }
  | { type: "dismissed" }
  | { type: "error"; payload: { code: string; message: string } }
  /**
   * The page wants the credential this host holds, instead of reading one off
   * its URL. Answered by injecting a `credential` reply into the WebView.
   */
  | { type: "needCredential" }
  /**
   * Pairing minted a credential. Only reaches a host that asked for custody —
   * this adapter does when a working keychain is present, because it keeps a
   * copy if the WebView's site data is cleared.
   */
  | { type: "credentialIssued"; payload: { token: string } }
  /** The page threw its stored credential away; drop the keychain copy too. */
  | { type: "credentialInvalid" }
  /**
   * Open this URL in the device's browser, outside the WebView. The page asks
   * for this when a parent chooses to approve the game from a browser instead
   * of scanning the code. Parent sign-in happens outside the WebView because
   * the game can inspect that surface; never collect a Level Moment email code
   * or other parent credential inside it (RFC 8252). The break keeps polling,
   * so nothing needs to come back.
   */
  | { type: "openExternal"; payload: { url: string } };

/** What this host answers `needCredential` with. */
export interface CredentialReply {
  /** The credential this host holds for the placement, or "" if none. */
  token: string;
  /**
   * Ask to be told about credentials the page mints or discards. True for this
   * adapter: it keeps a keychain copy that has to stay in step with the page's.
   */
  custody: boolean;
  customData?: string;
}

/**
 * The JavaScript that hands a credential to the hosted page, for
 * `WebView.injectJavaScript`.
 *
 * The reply is JSON-encoded and the encoding is the whole safety argument: a
 * token is an opaque server-issued string, but it arrives from outside this
 * function, and interpolating it raw into a script would make any quote or
 * backslash in it executable code inside the page. `JSON.stringify` on the
 * whole reply, embedded as a single string literal the page parses, cannot
 * escape its literal. The trailing `true;` is required by iOS —
 * injectJavaScript warns on a non-primitive result.
 */
export function credentialInjection(
  reply: CredentialReply,
  hostedUrl = HOSTED_BREAK_URL,
): string {
  const json = JSON.stringify(
    JSON.stringify({
      ...reply,
      protocolVersion: BRIDGE_PROTOCOL_VERSION,
      sdkVersion: SDK_VERSION,
    }),
  );
  const origin = JSON.stringify(new URL(hostedUrl).origin);
  return `window.location.origin === ${origin} && window.__levelMomentDeliverCredential && window.__levelMomentDeliverCredential(${json}); true;`;
}

/**
 * What this shell tells the page it can do, on the URL it loads.
 *
 * The page has to decide whether to offer "Approve in your browser" before it
 * can know anything about the shell around it, and a shell built before
 * `openExternal` existed forwards the message to the game without opening
 * anything — a button that silently does nothing, next to a QR code that
 * works. So every break and gate URL this SDK builds names the capability, and
 * a page loaded by an older build sees no `caps` at all and offers the QR
 * alone. Comma-separated, so adding a second capability changes only this
 * constant.
 */
export const HOST_CAPABILITIES_PARAM = "caps";
export const HOST_CAPABILITIES = "openExternal";

/**
 * The URL an `openExternal` message asks the OS to open, or null for every
 * other message and for anything that does not belong to `hostedUrl`'s origin.
 *
 * The origin check is the guard, and it has to be the origin rather than just
 * `http(s)`. This bridge belongs to whatever the WebView is currently showing:
 * a redirect that took it somewhere else keeps posting messages, and
 * `Linking.openURL` will full-screen whatever it is handed — a page asking for
 * a Level Moment sign-in code in the device's own browser is precisely what a
 * phishing site wants. Pinned to the hosted page's origin, the worst a wrong
 * URL can do is nothing.
 *
 * Comparing origins also settles the scheme: http passes only when the game is
 * configured against an http `breakUrl`, which is local development.
 */
export function externalUrlToOpen(
  msg: HostMessage,
  hostedUrl: string,
): string | null {
  if (msg.type !== "openExternal") return null;
  const raw = msg.payload?.url;
  if (typeof raw !== "string") return null;
  try {
    return sameHostedOrigin(raw, hostedUrl) ? raw : null;
  } catch {
    return null;
  }
}

/**
 * Terminal messages end the hosted surface: the WebView is torn down and the
 * caller is notified exactly once. `ready` and `earnedReward` are not terminal
 * — `earnedReward` fires once per answer within a single break.
 */
export function isTerminal(msg: HostMessage): boolean {
  return (
    msg.type === "signedIn" || msg.type === "dismissed" || msg.type === "error"
  );
}
