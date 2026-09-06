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

        internal void Validate()
        {
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

    internal static class LevelMomentEndpoints
    {
        public const string BreakUrl = "https://levelmoment.com/break";
        public const string AccessUrl = "https://levelmoment.com/access";
        public const string ApiUrl = "https://levelmoment.com/api";
        public const string SdkVersion = "0.2.0";
        public const int ProtocolVersion = 1;
    }
}
