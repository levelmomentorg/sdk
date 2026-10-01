// ---------------------------------------------------------------------------
// LevelMoment Unity SDK — configuration passed to LevelMomentAds.Initialize().
//
// Mirrors the initialize() options of the other ADR-001 WebView shells
// (sdk/react-native, sdk/flutter): apiUrl + breakUrl + an optional mock flag.
// The SDK is a thin shell over the hosted /break page — see
// docs/ADR-001-webview-rendering.md. It renders nothing itself.
// ---------------------------------------------------------------------------

using System;

namespace LevelMoment
{
    [Serializable]
    public class UnsafeTesting
    {
        public string ApiUrl;
        public string BreakUrl;
        public string Token;

        /// <summary>
        /// Run the real pairing path (pairing, a learner session, the learner's
        /// own curriculum) against a local hosted page instead of sandbox
        /// content. Editor and development builds only; any other build throws
        /// at <see cref="LevelMomentAds.Initialize(LevelMomentConfig)"/>.
        /// <see cref="BreakUrl"/> must be <c>http://localhost:&lt;port&gt;/…</c>
        /// or <c>http://127.0.0.1:&lt;port&gt;/…</c>. Takes no
        /// <see cref="Token"/>, no <see cref="ApiUrl"/>, and no mock: the page
        /// pairs the device itself and uses its own API.
        /// </summary>
        public bool RealPairing;
    }

    /// <summary>
    /// Configuration for the LevelMoment SDK. Populate once and pass to
    /// <see cref="LevelMomentAds.Initialize(LevelMomentConfig)"/>.
    /// </summary>
    [Serializable]
    public class LevelMomentConfig
    {
        /// <summary>
        /// Base URL of the LevelMoment API (e.g. <c>https://api.levelmoment.com</c>).
        /// Forwarded to the hosted /break page as the <c>apiUrl</c> query param.
        /// Required unless <see cref="Mock"/> is true.
        /// </summary>
        [Obsolete("Use the canonical endpoint or UnsafeTesting for local tests.")]
        public string ApiUrl;

        /// <summary>
        /// URL of the hosted /break page (e.g.
        /// <c>https://app.levelmoment.com/break</c>). The WebView shell has no
        /// renderer of its own — this is the page it opens. Required.
        /// </summary>
        [Obsolete("Use the canonical endpoint or UnsafeTesting for local tests.")]
        public string BreakUrl;

        /// <summary>
        /// When true, the hosted page uses bundled mock questions instead of
        /// hitting the live API (offline demos / pre-deploy testing). No
        /// <c>apiUrl</c>/<c>token</c> is sent in mock mode.
        /// </summary>
        public bool Mock;

        /// <summary>
        /// SSV-parity custom data — the analogue of AdMob's
        /// <c>ServerSideVerificationOptions.customData</c>. An opaque string
        /// sent in the credential handshake so the page stamps it on every
        /// impression it records (and the server echoes it on
        /// <c>reward.earned</c>).
        /// </summary>
        public string CustomData;

        /// <summary>
        /// Explicit opt-in overrides for local and sandbox testing. Sandbox
        /// credentials are accepted only with the <c>eply_sbx_</c> prefix and
        /// never use a production device credential store.
        /// </summary>
        public UnsafeTesting UnsafeTesting;

        public LevelMomentConfig() { }

        public LevelMomentConfig(string apiUrl, string breakUrl, bool mock = false, string customData = null)
        {
            ApiUrl = apiUrl;
            BreakUrl = breakUrl;
            Mock = mock;
            CustomData = customData;
        }

        internal string EffectiveApiUrl
        {
            get { return UnsafeTesting != null ? (string.IsNullOrEmpty(UnsafeTesting.ApiUrl) ? LevelMomentEndpoints.ApiUrl : UnsafeTesting.ApiUrl) : (string.IsNullOrEmpty(ApiUrl) ? LevelMomentEndpoints.ApiUrl : ApiUrl); }
        }

        internal string EffectiveBreakUrl
        {
            get { return UnsafeTesting != null ? (string.IsNullOrEmpty(UnsafeTesting.BreakUrl) ? LevelMomentEndpoints.BreakUrl : UnsafeTesting.BreakUrl) : (string.IsNullOrEmpty(BreakUrl) ? LevelMomentEndpoints.BreakUrl : BreakUrl); }
        }

        /// <summary>
        /// Validate the configuration and decide its mode. Every URL builder
        /// takes the result, so nothing re-derives the mode from the raw
        /// fields. Whether real pairing is available is decided at compile
        /// time: only the editor and development builds pass true below.
        /// </summary>
        internal ResolvedHostedConfig Resolve()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || LEVELMOMENT_COMPILE_CHECK
            if (DevelopmentBuildOverrideForTests.HasValue)
                return Resolve(DevelopmentBuildOverrideForTests.Value);
#endif
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            return Resolve(true);
#else
            return Resolve(false);
#endif
        }

        /// <summary>
        /// Test seam: when set, <see cref="Resolve()"/> treats the build as a
        /// development build (true) or a release build (false) instead of
        /// reading the compile-time symbols. Internal, so game code cannot
        /// reach it; <see cref="LevelMomentAds.ResetForTests"/> clears it.
        /// A release player never reads it: the check above is compiled only
        /// in the editor, a development build, or tools/unity-compile-check
        /// (which defines LEVELMOMENT_COMPILE_CHECK).
        /// </summary>
        internal static bool? DevelopmentBuildOverrideForTests;

        /// <summary>
        /// The resolution itself, with the kind of build passed in. The
        /// EditMode tests call it directly to cover both kinds of build.
        /// </summary>
        internal ResolvedHostedConfig Resolve(bool developmentBuild)
        {
            if (UnsafeTesting != null && UnsafeTesting.RealPairing)
                return ResolveRealPairing(developmentBuild);
            Validate();
            return new ResolvedHostedConfig(
                UnsafeTesting != null ? HostedMode.Sandbox : HostedMode.Production,
                EffectiveBreakUrl,
                EffectiveApiUrl,
                UnsafeTesting != null ? UnsafeTesting.Token : null,
                Mock);
        }

        private ResolvedHostedConfig ResolveRealPairing(bool developmentBuild)
        {
            if (!developmentBuild)
                throw RealPairingRefused("only available in the Unity editor or a development build");
            if (Mock)
                throw RealPairingRefused("cannot be combined with Mock");
            if (!string.IsNullOrEmpty(UnsafeTesting.Token))
                throw RealPairingRefused("takes no token; the page pairs the device itself");
#pragma warning disable 0618 // the deprecated fields are read only to refuse them
            if (!string.IsNullOrEmpty(UnsafeTesting.ApiUrl) || !string.IsNullOrEmpty(ApiUrl))
                throw RealPairingRefused("takes no ApiUrl; the page uses its own API");
            if (!string.IsNullOrEmpty(BreakUrl))
                throw RealPairingRefused("set UnsafeTesting.BreakUrl, not BreakUrl");
#pragma warning restore 0618
            var breakUrl = RealPairingUrl.Normalize(UnsafeTesting.BreakUrl);
            if (breakUrl == null)
                throw RealPairingRefused(
                    "BreakUrl must be http://localhost:<port>/… or http://127.0.0.1:<port>/…");
            RealPairingUrl.WarnOnce(HostedOrigin.Of(breakUrl));
            return new ResolvedHostedConfig(HostedMode.RealPairing, breakUrl, null, null, false);
        }

        private static ArgumentException RealPairingRefused(string reason)
        {
            return new ArgumentException("UnsafeTesting.RealPairing: " + reason + ".");
        }

        /// <summary>Validate without keeping the result.</summary>
        internal void Validate()
        {
            if (UnsafeTesting != null && UnsafeTesting.RealPairing)
            {
                Resolve();
                return;
            }
            if (UnsafeTesting != null)
            {
                if (!IsSafeTestUrl(EffectiveApiUrl))
                    throw new ArgumentException("UnsafeTesting.ApiUrl must be an absolute URL.");
                if (!IsSafeTestUrl(EffectiveBreakUrl))
                    throw new ArgumentException("UnsafeTesting.BreakUrl must be an absolute URL.");
                if (!string.IsNullOrEmpty(UnsafeTesting.Token) && !IsSandboxToken(UnsafeTesting.Token))
                    throw new ArgumentException("UnsafeTesting.Token must start with eply_sbx_.");
                return;
            }
            if ((!string.IsNullOrEmpty(ApiUrl) && ApiUrl != LevelMomentEndpoints.ApiUrl) ||
                (!string.IsNullOrEmpty(BreakUrl) && BreakUrl != LevelMomentEndpoints.BreakUrl))
                throw new ArgumentException("Use canonical Level Moment endpoints, or configure UnsafeTesting for tests.");
        }

        internal static bool IsSandboxToken(string token)
        {
            return !string.IsNullOrEmpty(token) && token.StartsWith("eply_sbx_", StringComparison.Ordinal);
        }

        private static bool IsSafeTestUrl(string value)
        {
            Uri uri;
            if (!Uri.TryCreate(value, UriKind.Absolute, out uri)) return false;
            return (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
                string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) &&
                string.IsNullOrEmpty(uri.Fragment);
        }
    }

    /// <summary>
    /// <c>Production</c>: the hosted destination. <c>Sandbox</c>: a test URL
    /// serving sandbox content with no stored credential. <c>RealPairing</c>:
    /// a loopback page running the real pairing path. See
    /// docs/decisions/sdk-real-pairing-testing-2026-10-01.md.
    /// </summary>
    internal enum HostedMode
    {
        Production,
        Sandbox,
        RealPairing,
    }

    /// <summary>
    /// A configuration after <see cref="LevelMomentConfig.Resolve()"/>: one
    /// mode, the URL to load, and what may ride along with it. The URL
    /// builders accept only this type.
    /// </summary>
    internal sealed class ResolvedHostedConfig
    {
        public readonly HostedMode Mode;
        public readonly string BreakUrl;

        /// <summary>Null under real pairing: the page uses its own API.</summary>
        public readonly string ApiUrl;

        /// <summary>The configured sandbox token; null under real pairing.</summary>
        public readonly string Token;

        public readonly bool Mock;

        internal ResolvedHostedConfig(HostedMode mode, string breakUrl, string apiUrl, string token, bool mock)
        {
            Mode = mode;
            BreakUrl = breakUrl;
            ApiUrl = apiUrl;
            Token = token;
            Mock = mock;
        }
    }

    internal static class LevelMomentEndpoints
    {
        public const string BreakUrl = "https://levelmoment.com/break";
        public const string AccessUrl = "https://levelmoment.com/access";
        public const string ApiUrl = "https://levelmoment.com/api";
        public const string SdkVersion = "0.3.0";
        public const int ProtocolVersion = 1;
    }
}
