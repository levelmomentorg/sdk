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
