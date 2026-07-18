// ---------------------------------------------------------------------------
// LevelMoment Unity SDK — top-level entry point (ADR-001 WebView shell).
//
// Mirrors MobileAds.Initialize() (AdMob Unity) / UnityAds.Initialize(). The SDK
// is a thin shell over the hosted /break page: Initialize() stores config,
// RewardedAd.Load()/Show() open that page in a WebView. All question rendering,
// answer submission, and impression queueing live in the hosted page — see
// docs/ADR-001-webview-rendering.md.
//
// USAGE:
//   LevelMomentAds.Initialize(new LevelMomentConfig {
//       ApiUrl   = "https://api.levelmoment.com",
//       BreakUrl = "https://app.levelmoment.com/break",
//   });
//   RewardedAd.Load("your-placement-id", new RewardedAdLoadCallbacks {
//       OnAdLoaded       = ad => _ad = ad,
//       OnAdFailedToLoad = err => Retry(),
//   });
//   _ad.Show(new RewardedAdShowCallbacks {
//       OnUserEarnedReward = amount => { if (amount == 1) GrantBonus(); },
//       OnAdDismissed      = () => ResumeGame(),
//       OnAdFailedToShow   = err => ResumeGame(),
//   });
// ---------------------------------------------------------------------------

using System;
using UnityEngine;

namespace LevelMoment
{
    public static class LevelMomentAds
    {
        internal const double DefaultLoadTimeoutSeconds = 15.0;

        /// <summary>Active configuration, or null until Initialize() is called.</summary>
        internal static LevelMomentConfig Config { get; private set; }

        /// <summary>True once Initialize() has been called. Mirrors AdMob's init gate.</summary>
        public static bool IsInitialized
        {
            get { return Config != null; }
        }

        // ---- Injectable seams (overridden by the EditMode tests) ------------

        /// <summary>Monotonic clock in seconds, used by the load watchdog.</summary>
        internal static Func<double> ClockSeconds = DefaultClock;

        /// <summary>Pre-`ready` load-timeout in seconds (0 disables the watchdog).</summary>
        internal static double LoadTimeoutSeconds = DefaultLoadTimeoutSeconds;

        /// <summary>
        /// Resolves the student session token when Load() is not given one
        /// explicitly. Defaults to reading <c>?token=</c> from the game's launch
        /// URL (Application.absoluteURL / deep link) — the parent portal embeds
        /// it there.
        /// </summary>
        internal static Func<string> LaunchToken = ReadLaunchTokenFromUrl;

        // ---- Public API -----------------------------------------------------

        /// <summary>
        /// Initialise the SDK. Call once from Awake()/Start() in your bootstrap
        /// scene, before loading any ads. Mirrors MobileAds.Initialize().
        /// </summary>
        public static void Initialize(LevelMomentConfig config)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));
            if (string.IsNullOrEmpty(config.BreakUrl))
                throw new ArgumentException(
                    "LevelMomentConfig.BreakUrl is required (the hosted /break page URL).",
                    nameof(config));
            if (!config.Mock && string.IsNullOrEmpty(config.ApiUrl))
                throw new ArgumentException(
                    "LevelMomentConfig.ApiUrl is required unless Mock is true.",
                    nameof(config));

            Config = config;
        }

        // ---- Internal helpers ----------------------------------------------

        /// <summary>Resolve the token to send: explicit if given, else from the launch URL.</summary>
        internal static string ResolveToken(string explicitToken)
        {
            if (!string.IsNullOrEmpty(explicitToken))
                return explicitToken;
            return LaunchToken != null ? LaunchToken() : null;
        }

        private static double DefaultClock()
        {
            return Time.realtimeSinceStartup;
        }

        private static string ReadLaunchTokenFromUrl()
        {
            try
            {
                var url = Application.absoluteURL;
                if (string.IsNullOrEmpty(url))
                    return null;
                var query = new Uri(url).Query; // "?a=1&token=..."
                if (string.IsNullOrEmpty(query))
                    return null;
                if (query[0] == '?')
                    query = query.Substring(1);

                var pairs = query.Split('&');
                for (var i = 0; i < pairs.Length; i++)
                {
                    var pair = pairs[i];
                    var eq = pair.IndexOf('=');
                    if (eq <= 0)
                        continue;
                    var key = pair.Substring(0, eq);
                    if (key == "token")
                        return Uri.UnescapeDataString(pair.Substring(eq + 1));
                }
            }
            catch (Exception)
            {
                // Non-WebGL / no launch URL / malformed — token simply unavailable.
            }

            return null;
        }

        /// <summary>Reset all state + seams. Intended for tests.</summary>
        internal static void ResetForTests()
        {
            Config = null;
            ClockSeconds = DefaultClock;
            LoadTimeoutSeconds = DefaultLoadTimeoutSeconds;
            LaunchToken = ReadLaunchTokenFromUrl;
        }
    }
}
