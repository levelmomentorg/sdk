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
        public string ApiUrl;

        /// <summary>
        /// URL of the hosted /break page (e.g.
        /// <c>https://app.levelmoment.com/break</c>). The WebView shell has no
        /// renderer of its own — this is the page it opens. Required.
        /// </summary>
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
        /// forwarded to the hosted /break page as the <c>customData</c> query
        /// param so the page stamps it on every impression it records (and the
        /// server echoes it on <c>reward.earned</c>). Omitted from the URL when
        /// null/empty.
        /// </summary>
        public string CustomData;

        public LevelMomentConfig() { }

        public LevelMomentConfig(string apiUrl, string breakUrl, bool mock = false, string customData = null)
        {
            ApiUrl = apiUrl;
            BreakUrl = breakUrl;
            Mock = mock;
            CustomData = customData;
        }
    }
}
