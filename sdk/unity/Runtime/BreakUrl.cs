// ---------------------------------------------------------------------------
// LevelMoment Unity SDK — hosted /break page URL builder.
//
// The WebView shell opens the hosted page at:
//   {breakUrl}?placementId=…&format=…&apiUrl=…&caps=…   (live)
//   {breakUrl}?placementId=…&format=…&mock=true&caps=…  (mock)
// &caps=… names what this shell can do, so the page only offers the parts of
// the pairing screen this build can carry out.
//
// NO CREDENTIAL rides on this URL. The page asks for one over the bridge
// (`needCredential`) and the SDK answers by running script in the page — see
// CredentialBridge. A token in a URL ends up in launch links, crash reports,
// and web logs; a token in a bridge message does not.
//
// Structurally mirrors LevelMomentAd._buildUrl() (sdk/web) and
// LevelMomentRewardedAd.buildUrl() (sdk/flutter): same params, same mock/live
// split, same encoding. Kept as a pure static method so it is exercised by the
// EditMode tests without a WebView.
// ---------------------------------------------------------------------------

using System;
using System.Text;

namespace LevelMoment
{
    internal static class BreakUrl
    {
        /// <summary>
        /// What this shell tells the page it can do, on the URL it loads.
        ///
        /// The page has to decide whether to offer "Approve in your browser"
        /// before it can know anything about the shell around it, and a shell
        /// built before <c>openExternal</c> existed drops the message unparsed
        /// — a button that silently does nothing, next to a QR code that works.
        /// So every break and gate URL this SDK builds names the capability,
        /// and a page loaded by an older build sees no <c>caps</c> at all and
        /// offers the QR alone. Comma-separated, so adding a second capability
        /// changes only this constant.
        /// </summary>
        public const string CapabilitiesParam = "caps";
        public const string Capabilities = "openExternal";

        /// <summary>
        /// Build the hosted /break URL for a placement. In mock mode the
        /// apiUrl is omitted and <c>mock=true</c> is appended instead.
        /// <paramref name="kind"/> is the placement kind announced to the
        /// page — omitted (as every call site but <c>InterstitialAd</c> does)
        /// for backward compatibility; <c>InterstitialAd</c> passes
        /// <c>"interstitial"</c> so the hosted page can tell it apart from a
        /// rewarded break. The web /break page does not yet read this param —
        /// see sdk/unity/README.md → InterstitialAd.
        /// </summary>
        public static string Build(
            LevelMomentConfig config,
            string placementId,
            string format,
            string kind = null,
            LevelMomentAdSlot slot = null)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));
            config.Validate();
            if (string.IsNullOrEmpty(placementId))
                throw new ArgumentException("placementId is required", nameof(placementId));

            var resolvedFormat = string.IsNullOrEmpty(format) ? "quick_question" : format;
            var query = new StringBuilder();
            Append(query, "placementId", placementId);
            Append(query, "format", resolvedFormat);
            if (!string.IsNullOrEmpty(kind))
                Append(query, "kind", kind);

            // What the game declared for this slot. Absent when it declared
            // nothing, and the hosted page then sizes the break by format.
            if (slot != null)
            {
                Append(query, "adType", slot.AdType);
                Append(query, "targetDurationSeconds", slot.TargetDurationSeconds.ToString(
                    System.Globalization.CultureInfo.InvariantCulture));
                if (slot.RewardAmount > 0)
                {
                    Append(query, "rewardAmount", slot.RewardAmount.ToString(
                        System.Globalization.CultureInfo.InvariantCulture));
                }
            }

            if (config.Mock)
            {
                Append(query, "mock", "true");
            }
            else
                Append(query, "apiUrl", config.EffectiveApiUrl);

            Append(query, CapabilitiesParam, Capabilities);
            Append(query, "protocolVersion", LevelMomentEndpoints.ProtocolVersion.ToString());
            Append(query, "sdkVersion", LevelMomentEndpoints.SdkVersion);
            if (config.UnsafeTesting != null)
                Append(query, "sandbox", "true");

            var separator = config.EffectiveBreakUrl.IndexOf('?') >= 0 ? "&" : "?";
            return config.EffectiveBreakUrl + separator + query.ToString();
        }

        /// <summary>
        /// Build the hosted sign-in URL for a placement. <paramref name="mode"/>
        /// is <c>gate</c> (fullscreen; runs pairing if needed) or <c>check</c>
        /// (headless credential validation). No <c>format</c> rides along — the
        /// gate shows no question. Mirrors gateUrl() in sdk/web/src/gate.ts.
        /// </summary>
        public static string BuildGate(
            LevelMomentConfig config,
            string placementId,
            string mode)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));
            config.Validate();
            if (string.IsNullOrEmpty(placementId))
                throw new ArgumentException("placementId is required", nameof(placementId));

            return BuildGateAt(config, config.EffectiveBreakUrl, placementId, mode);
        }

        /// <summary>
        /// Build the access surface URL. Production uses the canonical
        /// <c>/access</c> page; an explicitly configured unsafe test origin
        /// keeps its scheme and authority while changing only the path.
        /// </summary>
        public static string BuildAccess(
            LevelMomentConfig config,
            string placementId,
            string mode)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));
            config.Validate();
            if (string.IsNullOrEmpty(placementId))
                throw new ArgumentException("placementId is required", nameof(placementId));

            var accessBase = config.EffectiveBreakUrl == LevelMomentEndpoints.BreakUrl
                ? LevelMomentEndpoints.AccessUrl
                : AccessSurfaceUrl(config.EffectiveBreakUrl);
            return BuildGateAt(config, accessBase, placementId, mode);
        }

        private static string BuildGateAt(
            LevelMomentConfig config,
            string surfaceUrl,
            string placementId,
            string mode)
        {
            if (string.IsNullOrEmpty(surfaceUrl))
                throw new ArgumentException("surface URL is required", nameof(surfaceUrl));

            var query = new StringBuilder();
            Append(query, "mode", mode);
            Append(query, "placementId", placementId);

            if (config.Mock)
            {
                Append(query, "mock", "true");
            }
            else
                Append(query, "apiUrl", config.EffectiveApiUrl);

            Append(query, CapabilitiesParam, Capabilities);
            Append(query, "protocolVersion", LevelMomentEndpoints.ProtocolVersion.ToString());
            Append(query, "sdkVersion", LevelMomentEndpoints.SdkVersion);
            if (config.UnsafeTesting != null)
                Append(query, "sandbox", "true");

            var separator = surfaceUrl.IndexOf('?') >= 0 ? "&" : "?";
            return surfaceUrl + separator + query.ToString();
        }

        private static string AccessSurfaceUrl(string breakUrl)
        {
            var builder = new UriBuilder(new Uri(breakUrl))
            {
                Path = "/access",
                Query = string.Empty,
                Fragment = string.Empty,
            };
            return builder.Uri.ToString();
        }

        private static void Append(StringBuilder query, string key, string value)
        {
            if (query.Length > 0)
                query.Append('&');
            query.Append(Uri.EscapeDataString(key));
            query.Append('=');
            query.Append(Uri.EscapeDataString(value ?? string.Empty));
        }
    }
}
