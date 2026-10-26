import type { HostEnvironment } from "@levelmoment/sdk-core";

/**
 * The page this SDK runs on. Real pairing is allowed only when it is a local
 * dev server, so the check reads the SDK's own frame, never a parent's.
 */
export function webEnvironment(): HostEnvironment {
  return {
    kind: "web",
    pageUrl: typeof window === "undefined" ? "" : window.location?.href ?? "",
  };
}

/**
 * True only when the browser itself says it has no network connection.
 *
 * `navigator.onLine === false` is a claim about the device (Wi-Fi and mobile
 * data both off, airplane mode), never about Level Moment's servers. A server
 * outage leaves this false-negative on purpose: an outage on our side must
 * never lock a game. Anything other than an explicit `false`, including no `navigator` at all,
 * reads as online.
 */
export function browserReportsOffline(): boolean {
  return typeof navigator !== "undefined" && navigator?.onLine === false;
}
