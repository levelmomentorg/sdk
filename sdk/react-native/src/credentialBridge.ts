// The credential half of the bridge, shared by a break and the startup gate.
//
// Both open the same hosted surface and both must answer its `needCredential`
// the same way, or a gate could approve one credential and the break that
// follows hand over a different one. So the answering and the keychain
// bookkeeping live here, once.
//
// A credential is never put on the hosted URL. It travels only on this bridge:
// the page asks, the shell answers, and pairing hands the minted credential
// back so the keychain can outlive the WebView's own storage.
//
import type { HostedMode } from "@levelmoment/sdk-core";
import type { CredentialReply, HostMessage } from "./hostMessage.js";
import { deviceCredentials, type KeychainTokenStore } from "./tokenStore.js";

/**
 * Which keychain slot a surface may use. `origin` is the resolved hosted
 * page's origin and picks the slot; `useStore` is false for sandbox and mock
 * surfaces, which never read or write the keychain.
 */
export interface CredentialScope {
  placementId: string;
  origin: string;
  useStore: boolean;
}

export function credentialScope(
  resolved: { mode: HostedMode; breakUrl: string; mock?: boolean },
  placementId: string,
): CredentialScope {
  return {
    placementId,
    origin: new URL(resolved.breakUrl).origin,
    useStore: resolved.mode !== "sandbox" && !resolved.mock,
  };
}

/**
 * Build the answer to `needCredential` for one placement.
 *
 * `explicitToken` is whatever the game configured — a sandbox token, or one it
 * read from a parent-portal link. It leads when set: a token the game supplied
 * for this launch is the newer intent, and the page applies the same
 * known-bad demotion to it that it applied to `?token=`. With none, the
 * keychain copy answers. With neither, the reply carries an empty token and
 * still goes out promptly, so the page stops waiting.
 */
export function credentialResponder(
  scope: CredentialScope,
  explicitToken: string | undefined,
  store: KeychainTokenStore = deviceCredentials,
  customData?: string,
  reporting?: {
    slotType?: string;
    dimensions?: Record<string, string | number>;
  },
): () => Promise<CredentialReply> {
  return async () => ({
    token:
      explicitToken ||
      (scope.useStore ? await store.get(scope.origin, scope.placementId) : ""),
    customData,
    slotType: reporting?.slotType,
    dimensions: reporting?.dimensions,
    // Custody is a promise to keep a durable copy, so it is only claimed when
    // there is a keychain to keep it in. In Expo Go there is not — the native
    // module is absent and every write is silently dropped — and claiming it
    // anyway would have the page hand over credentials that go nowhere while
    // it stops treating its own storage as the record. Answering false there
    // degrades to exactly the pre-keychain model: the hosted origin keeps
    // ownership and pairing still works.
    custody: scope.useStore && (await store.isAvailable()),
  });
}

/**
 * Apply a credential message to the keychain. Returns whether it handled the
 * message, so callers can keep their own switch honest.
 *
 * Both directions matter. Storing what pairing issues is what saves the next
 * launch from asking a parent again; deleting what the page discards is what
 * stops the shell handing the same refused value straight back.
 */
export function applyCredentialMessage(
  msg: HostMessage,
  scope: CredentialScope,
  store: KeychainTokenStore = deviceCredentials,
): boolean {
  if (!scope.useStore) return false;
  if (msg.type === "credentialIssued") {
    void store.set(scope.origin, scope.placementId, msg.payload.token);
    return true;
  }
  if (msg.type === "credentialInvalid") {
    void store.clear(scope.origin, scope.placementId);
    return true;
  }
  return false;
}
