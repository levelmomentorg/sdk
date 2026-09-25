// ---------------------------------------------------------------------------
// LevelMoment Unity SDK — P/Invoke wrapper over the iOS storefront plugin.
//
// The native side (Runtime/Plugins/iOS/LevelMomentStorefront.m) reads
// SKPaymentQueue.defaultQueue.storefront.countryCode synchronously and
// returns null when no Apple account is signed in or the device predates
// iOS 13. This wrapper is what makes that safe to call from anywhere: the
// `__Internal` symbol resolves in an iOS player build — device or Simulator,
// both link the plugin and both can call StoreKit — while the Editor, Android,
// and standalone compile the call out entirely and return null.
// ---------------------------------------------------------------------------

using System;
using System.Runtime.InteropServices;

namespace LevelMoment
{
    internal static class NativeStorefront
    {
#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern IntPtr _levelMomentStorefrontCountryCode();
#endif

        /// <summary>
        /// The StoreKit storefront's ISO 3166-1 alpha-3 country code (e.g.
        /// <c>"USA"</c>), or null when unavailable or not running as an iOS
        /// player.
        /// </summary>
        public static string ReadCountryCode()
        {
#if UNITY_IOS && !UNITY_EDITOR
            var ptr = _levelMomentStorefrontCountryCode();
            return ptr == IntPtr.Zero ? null : Marshal.PtrToStringAnsi(ptr);
#else
            return null;
#endif
        }
    }
}
