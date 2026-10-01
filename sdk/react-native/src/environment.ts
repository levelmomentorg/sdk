import type { HostEnvironment } from "@levelmoment/sdk-core";

declare const __DEV__: boolean | undefined;

/**
 * Whether this is a debug build. Metro sets `__DEV__` to false in a release
 * bundle, so real pairing cannot be switched on in a build that ships.
 */
export function nativeEnvironment(): HostEnvironment {
  return {
    kind: "native",
    debugBuild: typeof __DEV__ !== "undefined" && __DEV__ === true,
  };
}
