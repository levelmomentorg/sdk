// SDK-owned credential storage, on the device keychain.
//
// Before this, a paired device's credential lived only in the hosted page's
// localStorage — inside the WebView, on the Level Moment origin. That storage
// is real, but it is not the app's to keep: WebView site data can be cleared by
// the platform or by the person using the device, and the SDK gets no say in
// when. Every time it goes, the household re-pairs, with a parent's phone
// involved.
//
// The keychain does not have that problem. It survives app updates and cache
// clears, it is encrypted at rest by the platform, and it is scoped to this
// app — no other app on the device can read it. So the shell keeps a copy: it
// answers the page's `needCredential` from here, writes what pairing issues
// (`credentialIssued`), and deletes on `credentialInvalid` so a dead value is
// not handed back next launch.
//
// One entry per placement, mirroring the page's per-placement keys: a
// credential is scoped to one game, and two Level Moment games in one app must
// not overwrite each other.
//
// A keychain that will not answer is never fatal. Every method here swallows
// its failure and reports "nothing stored", which lands the SDK back on the
// pre-keychain behavior: the hosted page's own storage, and pairing when that
// is empty too.

import * as Keychain from "react-native-keychain";

/** Keychain service name for one placement's credential. */
export function credentialService(placementId: string): string {
  return `com.levelmoment.credential.${placementId}`;
}

// The keychain API is a username/password pair; only the password carries
// anything. A fixed username keeps the entry identifiable in a keychain dump
// without saying anything about the household.
const CREDENTIAL_USERNAME = "levelmoment-device";

/**
 * Per-placement credential storage. The shells use the default instance; the
 * tests build their own over a fake keychain.
 */
export class KeychainTokenStore {
  constructor(private readonly keychain: KeychainApi = Keychain) {}

  /**
   * Whether this device actually has a working keychain, cached after the
   * first answer.
   *
   * Expo Go is why this exists. Installing react-native-keychain there adds
   * only JavaScript — the native module is not in the Expo Go binary, so every
   * call lands on an undefined `RNKeychainManager` and the catches below turn
   * it into a silent "nothing stored". Pairing then looks like it worked while
   * nothing durable was written, and the credential vanishes with the
   * WebView's data. Claiming custody in that state is a lie the household pays
   * for in re-pairs.
   *
   * A read is the probe. It needs no write permission, cannot disturb a stored
   * credential, and fails exactly the way a missing native module does.
   */
  private available: Promise<boolean> | null = null;

  isAvailable(): Promise<boolean> {
    this.available ??= this.probe();
    return this.available;
  }

  private async probe(): Promise<boolean> {
    try {
      await this.keychain.getGenericPassword({
        service: credentialService("availability-probe"),
      });
      return true;
    } catch {
      return false;
    }
  }

  /** The credential stored for this placement, or "" if there is none. */
  async get(placementId: string): Promise<string> {
    if (!placementId) return "";
    try {
      const result = await this.keychain.getGenericPassword({
        service: credentialService(placementId),
      });
      return result === false ? "" : result.password;
    } catch {
      // Locked keychain, a device without one, a failing native module. The
      // hosted page's own storage is still there to fall back on.
      return "";
    }
  }

  /** Keep a credential the hosted page just minted for this placement. */
  async set(placementId: string, token: string): Promise<void> {
    if (!placementId || !token) return;
    try {
      await this.keychain.setGenericPassword(CREDENTIAL_USERNAME, token, {
        service: credentialService(placementId),
      });
    } catch {
      // Unwritable keychain costs a re-pair after the WebView's own storage is
      // evicted, not this session.
    }
  }

  /** Forget this placement's credential after the server refused it. */
  async clear(placementId: string): Promise<void> {
    if (!placementId) return;
    try {
      await this.keychain.resetGenericPassword({
        service: credentialService(placementId),
      });
    } catch {
      // Nothing to do: the page refuses the same value again next launch and
      // clears it again from there.
    }
  }
}

/**
 * The slice of react-native-keychain this store uses. Declared structurally so
 * the tests can stand in a fake without a native module.
 */
export interface KeychainApi {
  getGenericPassword(
    options: Keychain.GetOptions,
  ): Promise<false | Keychain.UserCredentials>;
  setGenericPassword(
    username: string,
    password: string,
    options: Keychain.SetOptions,
  ): Promise<false | Keychain.Result>;
  resetGenericPassword(options: Keychain.BaseOptions): Promise<boolean>;
}

/** The store every Level Moment surface in this app shares. */
export const deviceCredentials = new KeychainTokenStore();
