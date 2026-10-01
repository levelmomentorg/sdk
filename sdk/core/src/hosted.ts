import type { LevelMomentAdErrorCode, SlotDeclaration } from "./types.js";
import { normalizeSlotDeclaration } from "./types.js";

export const HOSTED_BREAK_URL = "https://levelmoment.com/break";
export const HOSTED_API_URL = "https://levelmoment.com/api";
export const BRIDGE_PROTOCOL_VERSION = 1;
export const SDK_VERSION = "0.2.0";

/**
 * Hosts a game may pair against with `unsafeTesting.realPairing`. Always
 * `http:` with an explicit port. Mirrored in the Flutter and Unity SDKs; see
 * docs/decisions/sdk-real-pairing-testing-2026-10-01.md.
 */
export const REAL_PAIRING_HOSTS: readonly string[] = ["localhost", "127.0.0.1"];

/** Development only. Never reads or writes a paired device's credentials. */
export interface UnsafeTestingOptions {
  breakUrl?: string;
  apiUrl?: string;
  token?: string;
  /**
   * Run the real pairing path (pairing, a learner session, the learner's own
   * curriculum) against a local hosted page instead of sandbox content.
   * Debug builds only; `breakUrl` must be `http://localhost:<port>/…` or
   * `http://127.0.0.1:<port>/…`. Takes no token and no `apiUrl`: the page
   * owns its API destination.
   */
  realPairing?: boolean;
}

export interface HostedOptions {
  /** @deprecated Omit this field. The SDK owns the hosted destination. */
  breakUrl?: string;
  /** @deprecated Omit this field. The hosted service owns its API destination. */
  apiUrl?: string;
  /** @deprecated Use unsafeTesting.token for sandbox integration tests. */
  studentToken?: string;
  unsafeTesting?: UnsafeTestingOptions;
  mock?: boolean;
}

/**
 * Where the SDK is running, supplied by each wrapper. `sdk/core` reads no
 * globals: a release native build or a deployed web page must not be able to
 * enable real pairing, and only the wrapper knows which one it is.
 */
export type HostEnvironment =
  | { kind: "native"; debugBuild: boolean }
  | { kind: "web"; pageUrl: string };

/**
 * `production` — the hosted destination; `sandbox` — a test URL serving
 * sandbox content with no stored credential; `realPairing` — a loopback page
 * running the real path, with credentials kept apart from production.
 */
export type HostedMode = "production" | "sandbox" | "realPairing";

/**
 * Values this module resolved. A spread copy, a value from another bundle, or
 * one that went through JSON is not in here and is validated again, so a
 * caller cannot skip the checks by naming a mode itself.
 */
const resolvedValues = new WeakSet<object>();

declare const resolvedBrand: unique symbol;

export type ResolvedHostedOptions<T extends HostedOptions> = T & {
  readonly [resolvedBrand]: true;
  mode: HostedMode;
  breakUrl: string;
  /** Unset under `realPairing`: the page uses its own API base. */
  apiUrl: string | undefined;
  studentToken: string | undefined;
};

const warnedOrigins = new Set<string>();

function refuseRealPairing(reason: string): never {
  throw new Error(`unsafeTesting.realPairing: ${reason}`);
}

/**
 * Parse a loopback URL the way real pairing allows it: `http:`, a host from
 * REAL_PAIRING_HOSTS, an explicit port, no userinfo, query or fragment, and
 * nothing in the raw string a parser would quietly normalise away.
 */
function parseLoopbackHttp(raw: string): URL | null {
  if (/[\\\s@?#]/.test(raw) || /[^\x21-\x7e]/.test(raw)) return null;
  // The host must be written out as allowed, before any parser normalises
  // it: WHATWG turns `127.1` into `127.0.0.1`, Dart and C# do not, and every
  // SDK must give the same answer for the same string.
  const literal = /^http:\/\/([^/:]+):/i.exec(raw);
  if (!literal || !REAL_PAIRING_HOSTS.includes(literal[1]!.toLowerCase())) {
    return null;
  }
  let url: URL;
  try {
    url = new URL(raw);
  } catch {
    return null;
  }
  if (
    url.protocol !== "http:" ||
    !REAL_PAIRING_HOSTS.includes(url.hostname) ||
    url.port === "" ||
    url.port === "0" ||
    url.username ||
    url.password ||
    url.search ||
    url.hash
  ) {
    return null;
  }
  return url;
}

/**
 * True when the game's own page is a local dev server: `http:` on a host from
 * REAL_PAIRING_HOSTS with an explicit port. Path, query and fragment are the
 * game's business and are not checked.
 */
function isLocalDevPage(raw: string): boolean {
  const literal = /^http:\/\/([^/:?#\\@\s]+):(\d+)(?:[/?#]|$)/i.exec(raw);
  if (!literal || !REAL_PAIRING_HOSTS.includes(literal[1]!.toLowerCase())) {
    return false;
  }
  try {
    const url = new URL(raw);
    return (
      url.protocol === "http:" &&
      REAL_PAIRING_HOSTS.includes(url.hostname) &&
      url.port !== "" &&
      !url.username &&
      !url.password
    );
  } catch {
    return false;
  }
}

/** True when `raw` is a URL real pairing may load. */
export function isRealPairingUrl(raw: string): boolean {
  return parseLoopbackHttp(raw) !== null;
}

function resolveRealPairing(
  options: HostedOptions,
  env: HostEnvironment,
): { breakUrl: string } {
  const testing = options.unsafeTesting ?? {};
  if (env.kind === "native" && !env.debugBuild) {
    refuseRealPairing("only available in a debug build");
  }
  if (env.kind === "web" && !isLocalDevPage(env.pageUrl)) {
    refuseRealPairing(
      "only available on a page served from http://localhost:<port> or http://127.0.0.1:<port>",
    );
  }
  if (options.mock) refuseRealPairing("cannot be combined with mock");
  if (testing.token || options.studentToken) {
    refuseRealPairing("takes no token; the page pairs the device itself");
  }
  if (testing.apiUrl || options.apiUrl) {
    refuseRealPairing("takes no apiUrl; the page uses its own API");
  }
  const url = testing.breakUrl ? parseLoopbackHttp(testing.breakUrl) : null;
  if (!url) {
    refuseRealPairing(
      "breakUrl must be http://localhost:<port>/… or http://127.0.0.1:<port>/…",
    );
  }
  // A copy of a resolved value carries the resolved breakUrl at the top level
  // (LevelMomentWebClient.loadAd passes one); that one is accepted. Anything else at the top level is a mistake.
  if (options.breakUrl && options.breakUrl !== url.href) {
    refuseRealPairing("set unsafeTesting.breakUrl, not breakUrl");
  }
  if (!warnedOrigins.has(url.origin)) {
    warnedOrigins.add(url.origin);
    console.warn(
      `[LevelMoment] unsafeTesting.realPairing is on: pairing against ${url.origin}, not Level Moment. Remove it before release.`,
    );
  }
  return { breakUrl: url.href };
}

/**
 * Resolve hosted options once. Every URL builder takes the result, so the
 * mode is decided here and nowhere else. Resolving an already-resolved value
 * returns it unchanged.
 */
export function resolveHostedOptions<T extends HostedOptions>(
  options: T,
  env: HostEnvironment,
): ResolvedHostedOptions<T> {
  if (resolvedValues.has(options)) {
    return options as ResolvedHostedOptions<T>;
  }
  if (options.unsafeTesting?.realPairing) {
    const { breakUrl } = resolveRealPairing(options, env);
    return register<T>({
      ...options,
      mode: "realPairing",
      breakUrl,
      apiUrl: undefined,
      studentToken: undefined,
    });
  }
  if (options.studentToken && !options.unsafeTesting) {
    throw new Error(
      "Player tokens are managed by Level Moment. Use unsafeTesting for tests.",
    );
  }
  if (
    !options.unsafeTesting &&
    ((options.breakUrl && options.breakUrl !== HOSTED_BREAK_URL) ||
      (options.apiUrl && options.apiUrl !== HOSTED_API_URL))
  ) {
    throw new Error(
      "Level Moment owns its hosted URLs. Use unsafeTesting for local development.",
    );
  }
  const breakUrl =
    options.unsafeTesting?.breakUrl ?? options.breakUrl ?? HOSTED_BREAK_URL;
  const apiUrl =
    options.unsafeTesting?.apiUrl ?? options.apiUrl ?? HOSTED_API_URL;
  for (const raw of [breakUrl, apiUrl]) {
    const url = new URL(raw);
    if (
      !["http:", "https:"].includes(url.protocol) ||
      url.username ||
      url.password ||
      url.search ||
      url.hash
    ) {
      throw new Error(
        "Use an HTTP(S) test URL without credentials, a query, or a fragment.",
      );
    }
  }
  return register<T>({
    ...options,
    mode: options.unsafeTesting ? "sandbox" : "production",
    breakUrl,
    apiUrl,
    studentToken: options.unsafeTesting?.token ?? options.studentToken,
  });
}

function register<T extends HostedOptions>(
  value: T & { mode: HostedMode },
): ResolvedHostedOptions<T> {
  resolvedValues.add(value);
  return value as unknown as ResolvedHostedOptions<T>;
}

export function addBridgeVersion(
  params: URLSearchParams,
  mode: HostedMode,
): void {
  params.set("protocolVersion", String(BRIDGE_PROTOCOL_VERSION));
  params.set("sdkVersion", SDK_VERSION);
  if (mode === "sandbox") params.set("sandbox", "true");
}

/**
 * The native credential-store key for a placement on a hosted origin. The
 * production origin keeps its original key, so stored credentials survive;
 * any other origin gets a key under a different prefix, so a test credential
 * can never be read or overwritten as a production one.
 */
export function credentialStoreKey(
  origin: string,
  placementId: string,
): string {
  if (origin === new URL(HOSTED_BREAK_URL).origin) {
    return `com.levelmoment.credential.${placementId}`;
  }
  const encoded = origin.replace(/[^A-Za-z0-9.-]+/g, "_");
  return `com.levelmoment.test-credential.${encoded}.${placementId}`;
}

/**
 * Put a slot declaration on the hosted break URL. The names here are the names
 * the hosted page and the API read, so every platform SDK spells them the same
 * way. A slot that declared nothing adds nothing, and the page falls back to
 * the format's own default size.
 */
export function addSlotParams(
  params: URLSearchParams,
  slot: SlotDeclaration | null | undefined,
): void {
  const declared = normalizeSlotDeclaration(slot);
  if (!declared) return;
  params.set("adType", declared.adType);
  params.set("targetDurationSeconds", String(declared.targetDurationSeconds));
  if (declared.rewardAmount !== undefined) {
    params.set("rewardAmount", String(declared.rewardAmount));
  }
}

export function sameHostedOrigin(url: string, hostedUrl: string): boolean {
  try {
    const parsed = new URL(url);
    return (
      !parsed.username &&
      !parsed.password &&
      parsed.origin === new URL(hostedUrl).origin
    );
  } catch {
    return false;
  }
}

/** Validate messages before they reach game callbacks or credential storage. */
export function isBridgeMessage(value: unknown): boolean {
  if (!value || typeof value !== "object") return false;
  const msg = value as Record<string, unknown>;
  if (
    msg.protocolVersion !== undefined &&
    msg.protocolVersion !== BRIDGE_PROTOCOL_VERSION
  )
    return false;
  if (
    [
      "ready",
      "signedIn",
      "dismissed",
      "needCredential",
      "credentialInvalid",
    ].includes(String(msg.type))
  )
    return true;
  if (!msg.payload || typeof msg.payload !== "object") return false;
  const payload = msg.payload as Record<string, unknown>;
  switch (msg.type) {
    case "earnedReward":
      return (
        typeof payload.rewardId === "string" &&
        payload.rewardId.length > 0 &&
        payload.rewardId.length <= 256 &&
        typeof payload.earnedAt === "string" &&
        !Number.isNaN(Date.parse(payload.earnedAt))
      );
    case "credentialIssued":
      return typeof payload.token === "string" && payload.token.length > 0;
    case "openExternal":
      return typeof payload.url === "string";
    case "error":
      return (
        typeof payload.code === "string" && typeof payload.message === "string"
      );
    default:
      return false;
  }
}

// ---------------------------------------------------------------------------
// Store claims on the credential reply
//
// Every host answers the hosted page's `needCredential` with a credential
// reply. The reply also says which platform the game runs on and, on iOS,
// which App Store storefront the player's account uses, so Level Moment can
// follow each store's rules. The page forwards both to the API, never on a
// URL, and they decide nothing about grading or earnings.
// ---------------------------------------------------------------------------

/** The platform a host runs on. */
export type HostPlatform = "ios" | "android" | "web";

/** What a host states about its store, next to the credential fields. */
export interface HostStoreClaims {
  /**
   * `ios` or `android` from a native shell, `web` from a browser host. A
   * native shell must never say `web`, and the page ignores it if one does.
   */
  platform: HostPlatform;
  /**
   * The App Store storefront as StoreKit reports it (`USA`), or null when the
   * host has not read one. Always null on Android and the web.
   */
  storefront: string | null;
}

const PUBLIC_AD_ERROR_CODES: readonly LevelMomentAdErrorCode[] = [
  "network_error",
  "no_fill",
  "invalid_token",
  "not_loaded",
  "unknown",
];

/**
 * Map an error code the hosted page posts to one a game may branch on.
 *
 * The five public codes pass through. `store_unavailable`, the API's
 * store-policy refusal, becomes `no_fill`: to the game it means only "no break
 * this time", and it must never read as `invalid_token`, which tells a host its
 * credential is dead. Anything else becomes `unknown`.
 */
export function toPublicAdErrorCode(code: unknown): LevelMomentAdErrorCode {
  if (code === "store_unavailable") return "no_fill";
  return PUBLIC_AD_ERROR_CODES.includes(code as LevelMomentAdErrorCode)
    ? (code as LevelMomentAdErrorCode)
    : "unknown";
}
