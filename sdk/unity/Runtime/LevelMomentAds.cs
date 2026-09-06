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

        /// <summary>
        /// Total deadline for the headless check — load, credential walk,
        /// verdict. The load watchdog only covers the page coming up, and the
        /// hosted page's own `check_timeout` only covers the page still running;
        /// neither survives a WebView that loads and then goes silent, so
        /// IsSignedIn keeps one deadline over the whole thing. With default
        /// settings, the 30-second total deadline is the final backstop after
        /// the 15-second load watchdog and the page's 10-second deadline.
        /// EnsureSignedIn has no equivalent: past `ready` a parent is finding
        /// their phone.
        /// </summary>
        internal const double DefaultCheckTimeoutSeconds = 30.0;

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

        /// <summary>Total IsSignedIn deadline in seconds (0 disables it).</summary>
        internal static double CheckTimeoutSeconds = DefaultCheckTimeoutSeconds;

        // ---- Public API -----------------------------------------------------

        /// <summary>
        /// Initialise the SDK. Call once from Awake()/Start() in your bootstrap
        /// scene, before loading any ads. Mirrors MobileAds.Initialize().
        /// </summary>
        public static void Initialize(LevelMomentConfig config)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));
            config.Validate();

            Config = config;
        }

        // ---- Startup sign-in gate ------------------------------------------

        /// <summary>
        /// Run the startup sign-in gate. Call it once before enabling gameplay
        /// and start the game only on <see cref="EnsureSignedInResult.Ready"/>.
        ///
        /// It opens the hosted Level Moment surface fullscreen. A device that
        /// already holds a valid credential for this game passes through in a
        /// moment; otherwise the surface runs the ask-a-parent pairing flow and
        /// waits for the answer, for as long as a parent takes.
        ///
        /// The callback runs exactly once, and this method never throws:
        /// <list type="bullet">
        /// <item><description><c>Ready</c> — start the game.</description></item>
        /// <item><description><c>Canceled</c> — a person closed the gate or a
        /// parent denied the connection. Show your own "learning breaks are off"
        /// state or a retry action; do not retry automatically.</description></item>
        /// <item><description><c>TechnicalFailure</c> — the gate could not run.
        /// Retry later.</description></item>
        /// </list>
        ///
        /// None of the three reveals subscription, tier, or quota. With
        /// <see cref="LevelMomentConfig.Mock"/> set it reports <c>Ready</c>
        /// immediately and shows nothing. Pass <paramref name="studentToken"/>
        /// only for a sandbox token while you integrate; a paired device needs
        /// none.
        /// </summary>
        public static void EnsureSignedIn(
            string placementId,
            Action<EnsureSignedInResult> callback,
            string studentToken = null)
        {
            if (callback == null)
                return;
            string resolvedToken;
            try
            {
                resolvedToken = ResolveStudentToken(studentToken);
            }
            catch (Exception)
            {
                callback(EnsureSignedInResult.TechnicalFailure);
                return;
            }
            if (Config != null && Config.Mock)
            {
                callback(EnsureSignedInResult.Ready);
                return;
            }

            string url;
            try
            {
                url = BuildGateUrl(placementId, "gate");
            }
            catch (Exception)
            {
                // A missing Initialize() or a bad config string is a technical
                // failure, not an exception. A publisher branches on the three
                // results in their boot path, and a throw from here would crash
                // the game over a config string.
                callback(EnsureSignedInResult.TechnicalFailure);
                return;
            }

            SignInGate.Open(
                url,
                false,
                delegate { callback(EnsureSignedInResult.Ready); },
                delegate { callback(EnsureSignedInResult.Canceled); },
                delegate { callback(EnsureSignedInResult.TechnicalFailure); },
                resolvedToken);
        }

        /// <summary>
        /// Run the opt-in access gate on the hosted <c>/access</c> surface.
        /// Results have the same meaning as <see cref="EnsureSignedIn"/>;
        /// identity sign-in remains on <c>/break</c> for existing integrations.
        /// </summary>
        public static void EnsureAccess(
            string placementId,
            Action<EnsureSignedInResult> callback,
            string studentToken = null)
        {
            if (callback == null)
                return;
            string resolvedToken;
            try
            {
                resolvedToken = ResolveStudentToken(studentToken);
            }
            catch (Exception)
            {
                callback(EnsureSignedInResult.TechnicalFailure);
                return;
            }
            if (Config != null && Config.Mock)
            {
                callback(EnsureSignedInResult.Ready);
                return;
            }

            string url;
            try
            {
                url = BuildAccessUrl(placementId, "gate");
            }
            catch (Exception)
            {
                callback(EnsureSignedInResult.TechnicalFailure);
                return;
            }

            SignInGate.Open(
                url,
                false,
                delegate { callback(EnsureSignedInResult.Ready); },
                delegate { callback(EnsureSignedInResult.Canceled); },
                delegate { callback(EnsureSignedInResult.TechnicalFailure); },
                resolvedToken);
        }

        /// <summary>
        /// Ask whether this device holds a valid Level Moment credential for the
        /// game. This is an authoritative server-checked answer, not a cached
        /// flag: it loads a hidden hosted surface that validates the stored
        /// credential through the API, because that credential lives on the Level
        /// Moment origin where this SDK cannot read it.
        ///
        /// <paramref name="onError"/> — not <c>onResult(false)</c> — runs if the
        /// check fails technically, exceeds the load timeout, exceeds the total
        /// <see cref="DefaultCheckTimeoutSeconds"/> deadline (including load
        /// time), or no WebView provider is
        /// installed. <c>false</c> is a claim about the household, and guessing it
        /// from a network failure would push a linked player back through pairing.
        /// Treat an error as "unknown, try again", never as signed out. With
        /// <see cref="LevelMomentConfig.Mock"/> set it reports <c>true</c>.
        ///
        /// The surface stays invisible when the registered WebView provider
        /// implements <see cref="ILevelMomentHeadlessWebView"/>; otherwise it is
        /// briefly shown.
        /// </summary>
        public static void IsSignedIn(
            string placementId,
            Action<bool> onResult,
            Action<string> onError,
            string studentToken = null)
        {
            string url;
            string resolvedToken;
            try
            {
                resolvedToken = ResolveStudentToken(studentToken);
                url = BuildGateUrl(placementId, "check");
            }
            catch (Exception err)
            {
                if (onError != null)
                    onError("Level Moment could not open the sign-in check: " + err.Message);
                return;
            }

            if (Config != null && Config.Mock)
            {
                if (onResult != null)
                    onResult(true);
                return;
            }

            SignInGate.Open(
                url,
                true,
                delegate { if (onResult != null) onResult(true); },
                delegate { if (onResult != null) onResult(false); },
                delegate(string code)
                {
                    if (onError != null)
                        onError("Level Moment could not check this device: " + code);
                },
                resolvedToken);
        }

        /// <summary>
        /// Check whether access is already connected. <paramref name="onResult"/>
        /// receives <c>false</c> when a person needs to complete the access
        /// flow; <paramref name="onError"/> receives technical failures. This
        /// is the access counterpart to <see cref="IsSignedIn"/> and uses
        /// <c>/access?mode=check</c>.
        /// </summary>
        public static void CheckAccess(
            string placementId,
            Action<bool> onResult,
            Action<string> onError,
            string studentToken = null)
        {
            string url;
            string resolvedToken;
            try
            {
                resolvedToken = ResolveStudentToken(studentToken);
                url = BuildAccessUrl(placementId, "check");
            }
            catch (Exception err)
            {
                if (onError != null)
                    onError("Level Moment could not open the access check: " + err.Message);
                return;
            }

            if (Config != null && Config.Mock)
            {
                if (onResult != null)
                    onResult(true);
                return;
            }

            SignInGate.Open(
                url,
                true,
                delegate { if (onResult != null) onResult(true); },
                delegate { if (onResult != null) onResult(false); },
                delegate(string code)
                {
                    if (onError != null)
                        onError("Level Moment could not check access: " + code);
                },
                resolvedToken);
        }

        private static string BuildGateUrl(string placementId, string mode)
        {
            if (!IsInitialized)
                throw new InvalidOperationException(
                    "Call LevelMomentAds.Initialize() before the sign-in gate.");
            return BreakUrl.BuildGate(Config, placementId, mode);
        }

        private static string BuildAccessUrl(string placementId, string mode)
        {
            if (!IsInitialized)
                throw new InvalidOperationException(
                    "Call LevelMomentAds.Initialize() before the access gate.");
            return BreakUrl.BuildAccess(Config, placementId, mode);
        }

        internal static string ResolveStudentToken(string token)
        {
            if (string.IsNullOrEmpty(token))
                return Config != null && Config.UnsafeTesting != null ? Config.UnsafeTesting.Token : null;
            if (Config == null || Config.UnsafeTesting == null || !LevelMomentConfig.IsSandboxToken(token))
                throw new ArgumentException("studentToken is available only for eply_sbx_ credentials in UnsafeTesting.");
            return token;
        }

        // ---- Internal helpers ----------------------------------------------

        private static double DefaultClock()
        {
            return Time.realtimeSinceStartup;
        }

        /// <summary>Reset all state + seams. Intended for tests.</summary>
        internal static void ResetForTests()
        {
            Config = null;
            ClockSeconds = DefaultClock;
            LoadTimeoutSeconds = DefaultLoadTimeoutSeconds;
            CheckTimeoutSeconds = DefaultCheckTimeoutSeconds;
            LevelMomentRuntime.SkipDriver = false;
        }
    }
}
