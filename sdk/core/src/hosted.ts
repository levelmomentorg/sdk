import type { SlotDeclaration } from "./types.js";
import { normalizeSlotDeclaration } from "./types.js";

export const HOSTED_BREAK_URL = "https://levelmoment.com/break";
export const HOSTED_API_URL = "https://levelmoment.com/api";
export const BRIDGE_PROTOCOL_VERSION = 1;
export const SDK_VERSION = "0.2.0";

/** Development only. Never reads or writes a paired device's credentials. */
export interface UnsafeTestingOptions {
  breakUrl?: string;
  apiUrl?: string;
  token?: string;
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

export function resolveHostedOptions<T extends HostedOptions>(options: T) {
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
  return {
    ...options,
    breakUrl,
    apiUrl,
    studentToken: options.unsafeTesting?.token ?? options.studentToken,
  };
}

export function addBridgeVersion(
  params: URLSearchParams,
  testing = false,
): void {
  params.set("protocolVersion", String(BRIDGE_PROTOCOL_VERSION));
  params.set("sdkVersion", SDK_VERSION);
  if (testing) params.set("sandbox", "true");
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
        (payload.amount === 0 || payload.amount === 1) &&
        (payload.rewardId === undefined ||
          (typeof payload.rewardId === "string" &&
            payload.rewardId.length > 0 &&
            payload.rewardId.length <= 256))
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
