// The module-level handoff between a caller that wants a hosted surface and the
// <LevelMomentAdModal /> mounted at the app root — the same pattern AdMob's RN
// SDK uses.
//
// It lives apart from LevelMomentAdModal.tsx so that callers (LevelMomentAd and
// the startup gate) reach the host without importing react-native, which keeps
// their logic unit-testable.

import type { CredentialReply, HostMessage } from "./hostMessage.js";

export interface ModalShowParams {
  /** Fully-built hosted-page URL. */
  url: string;
  /** Every message from the page, `ready` included. */
  onMessage: (msg: HostMessage) => void;
  /**
   * Render the WebView off-screen with no chrome. Used by the headless
   * credential check, which has no UI to show.
   */
  hidden?: boolean;
  /** Pre-`ready` watchdog in ms. Defaults to 15000. */
  loadTimeoutMs?: number;
  /**
   * The page never posted `ready` within the watchdog window. When omitted the
   * host synthesizes a `dismissed` instead, which is what a break wants: the
   * game resumes as if the break had ended normally. The gate supplies this so
   * a page that never came up reads as a technical failure, not as a person
   * saying no.
   */
  onLoadTimeout?: () => void;
  /**
   * A newer request took this surface's slot before it reached a terminal
   * message. The host owns one visible slot and one hidden slot, so the
   * displaced caller would otherwise wait forever on a WebView that is no
   * longer in the tree. Its watchdog is already cancelled when this runs.
   */
  onDisplaced?: () => void;
  /**
   * The page asked for a credential. Resolve with the one this caller holds —
   * or an empty token, promptly, when it holds none: the page waits only
   * briefly before falling back to its own storage, and a caller that never
   * answers spends that whole window for nothing.
   *
   * The host injects the reply into the WebView. It lives here rather than in
   * `onMessage` because only the host can reach the WebView to inject.
   */
  onNeedCredential?: () => Promise<CredentialReply>;
}

/**
 * What a caller gets back for the surface it just opened, so it can take that
 * surface down on a path the hosted page will never end itself. A terminal
 * message and the load watchdog both clear the slot through the host's own
 * teardown; the sign-in check's total deadline fires precisely when no message
 * is coming, and without this the WebView would stay mounted — running JS and
 * holding a network context — until another check displaced it.
 */
export interface ModalHandle {
  /**
   * Take this surface out of the tree. Fires no callbacks: the caller has
   * already settled. No-op once the surface is gone or another one took its
   * slot.
   */
  close: () => void;
}

type ModalHandler = (params: ModalShowParams) => ModalHandle | void;

let _globalHandler: ModalHandler | null = null;

/** @internal Called by LevelMomentAdModal when it mounts. */
export function _registerModalHandler(handler: ModalHandler | null): void {
  _globalHandler = handler;
}

/** @internal Returns true if LevelMomentAdModal is mounted and ready. */
export function _hasModalHandler(): boolean {
  return _globalHandler !== null;
}

/**
 * @internal Triggered by LevelMomentAd.show() and by the startup gate. Returns
 * null when no modal is mounted, or when the host offers no handle.
 */
export function _triggerModal(params: ModalShowParams): ModalHandle | null {
  return _globalHandler?.(params) ?? null;
}
