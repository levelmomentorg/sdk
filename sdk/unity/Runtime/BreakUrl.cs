// ---------------------------------------------------------------------------
// LevelMoment Unity SDK — hosted /break page URL builder.
//
// The WebView shell opens the hosted page at:
//   {breakUrl}?placementId=…&format=…&apiUrl=…&token=…   (live)
//   {breakUrl}?placementId=…&format=…&mock=true          (mock)
// An optional &customData=… rides along in both modes when config.CustomData
// is set (SSV-parity — stamped onto every impression the hosted page records).
//
// Structurally mirrors LevelMomentAd._buildUrl() (sdk/web) and
// LevelMomentRewardedAd.buildUrl() (sdk/flutter): same params, same mock/live
// split, same encoding. Kept as a pure static method so it is exercised by the
// EditMode tests without a WebView. Per ADR-001 the student token legitimately
// rides in the URL to the WebView, exactly as the web and Flutter shells do.
// ---------------------------------------------------------------------------

using System;
using System.Text;

namespace LevelMoment
{
    internal static class BreakUrl
    {
        /// <summary>
        /// Build the hosted /break URL for a placement. In mock mode the
        /// apiUrl/token are omitted and <c>mock=true</c> is appended instead.
        /// </summary>
        public static string Build(
            LevelMomentConfig config,
            string placementId,
            string format,
            string studentToken)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));
            if (string.IsNullOrEmpty(config.BreakUrl))
                throw new ArgumentException("config.BreakUrl is required", nameof(config));
            if (string.IsNullOrEmpty(placementId))
                throw new ArgumentException("placementId is required", nameof(placementId));

            var query = new StringBuilder();
            Append(query, "placementId", placementId);
            Append(query, "format", string.IsNullOrEmpty(format) ? "flashcard" : format);

            if (config.Mock)
            {
                Append(query, "mock", "true");
            }
            else
            {
                if (!string.IsNullOrEmpty(config.ApiUrl))
                    Append(query, "apiUrl", config.ApiUrl);
                if (!string.IsNullOrEmpty(studentToken))
                    Append(query, "token", studentToken);
            }

            // SSV-parity: carry the host-supplied customData to the hosted page
            // in both modes (opaque correlation data, not a credential) so the
            // page stamps it on every impression it records.
            if (!string.IsNullOrEmpty(config.CustomData))
                Append(query, "customData", config.CustomData);

            var separator = config.BreakUrl.IndexOf('?') >= 0 ? "&" : "?";
            return config.BreakUrl + separator + query.ToString();
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
