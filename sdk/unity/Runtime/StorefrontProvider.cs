// ---------------------------------------------------------------------------
// LevelMoment Unity SDK — platform + App Store storefront reporting.
//
// The server needs to know which store a break was
// served through, so it can decide store policy (full checkout, sign-in
// only, or hidden entirely). The native shell reports the platform and, on
// iOS, the StoreKit storefront country code, next to the credential fields
// in the reply to the hosted page's `needCredential` bridge message — see
// CredentialBridge.
//
// The read happens exactly once, at LevelMomentAds.Initialize(), and is
// cached: the hosted page waits only 500ms for the `needCredential` reply,
// so a synchronous store read must never run on that path. IStorefrontProvider
// is the seam LevelMomentAds reads through, so EditMode tests can assert the
// cache-once behaviour and the reply JSON without a native call.
// ---------------------------------------------------------------------------

namespace LevelMoment
{
    /// <summary>
    /// Reads this device's platform and, on iOS, its App Store storefront.
    /// Injectable so tests can assert LevelMomentAds reads it exactly once
    /// (at Initialize) and never again from the `needCredential` reply path.
    /// </summary>
    internal interface IStorefrontProvider
    {
        /// <summary>
        /// <c>"ios"</c> or <c>"android"</c>, from the running OS on a device
        /// player. Null on macOS, in the Unity Editor (even when the active
        /// build target is iOS or Android — the Editor is not a device), and
        /// for every other standalone target — never <c>"web"</c>.
        /// </summary>
        string Platform { get; }

        /// <summary>
        /// iOS: the StoreKit storefront's ISO 3166-1 alpha-3 country code
        /// (e.g. <c>"USA"</c>), or null when no Apple account is signed in or
        /// the device predates iOS 13. Android and every other platform:
        /// always null — there is no Play Billing dependency here, by design
        /// (every Google Play row resolves the same store policy either way).
        /// </summary>
        string ReadStorefront();
    }

    /// <summary>Default provider: the real OS and, on a device iOS player, the
    /// real StoreKit storefront via <see cref="NativeStorefront"/>.</summary>
    internal sealed class DeviceStorefrontProvider : IStorefrontProvider
    {
        public string Platform
        {
            get
            {
                // The Editor defines UNITY_IOS/UNITY_ANDROID whenever that
                // platform is the active build target, but the Editor is not
                // a device — excluding it here is what keeps an Editor Play
                // Mode session (or an Android-target Editor) from claiming a
                // platform it is not running on. NativeStorefront applies the
                // same !UNITY_EDITOR guard for the storefront read.
#if UNITY_IOS && !UNITY_EDITOR
                return "ios";
#elif UNITY_ANDROID && !UNITY_EDITOR
                return "android";
#else
                return null;
#endif
            }
        }

        public string ReadStorefront()
        {
            return NativeStorefront.ReadCountryCode();
        }
    }
}
