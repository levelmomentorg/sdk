export { LevelMomentAd, type LevelMomentAdEvent } from "./LevelMomentAd.js";
// Add <LevelMomentAdModal /> once at your app root to enable automatic question rendering
export { LevelMomentAdModal } from "./LevelMomentAdModal.js";
// Startup sign-in gate — call ensureSignedIn() before enabling gameplay
export {
  ensureSignedIn,
  isSignedIn,
  ensureAccess,
  checkAccess,
  // Signing a household out means clearing BOTH stores — the keychain and the
  // hosted origin's. Use this rather than the keychain store below: clearing
  // only the keychain leaves the hosted copy to sign the previous learner
  // straight back in on the next ensureSignedIn().
  signOut,
  type SignInOptions,
} from "./gate.js";
export type {
  BreakFormat,
  EnsureSignedInResult,
  LevelMomentAdErrorCode,
  LevelMomentAdError,
  RewardItem,
  UnsafeTestingOptions,
} from "@levelmoment/sdk-core";
