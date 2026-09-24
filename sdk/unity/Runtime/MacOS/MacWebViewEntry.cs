// ---------------------------------------------------------------------------
// LevelMoment Unity SDK — pure helpers for the native macOS WebView host.
//
// Compiled on every platform so EditMode tests cover them; only the adapter in
// MacNativeWebView.cs is macOS-player-only.
// ---------------------------------------------------------------------------

using System;

namespace LevelMoment
{
    internal static class MacWebViewEntry
    {
        /// <summary>
        /// The origin the native host fences navigation to, e.g.
        /// <c>https://levelmoment.com</c> or <c>http://localhost:3000</c>. Null
        /// when <paramref name="url"/> is not an absolute http(s) URL, which the
        /// adapter treats as a refusal to open.
        /// </summary>
        public static string OriginOf(string url)
        {
            Uri parsed;
            if (!Uri.TryCreate(url, UriKind.Absolute, out parsed))
                return null;
            if (parsed.Scheme != Uri.UriSchemeHttps && parsed.Scheme != Uri.UriSchemeHttp)
                return null;
            return parsed.GetLeftPart(UriPartial.Authority);
        }

        /// <summary>
        /// Turn one entry drained from the native queue into the raw bridge JSON
        /// the SDK parses. The host prefixes page messages with <c>M</c> and
        /// native load failures with <c>E</c>; a failure becomes the same
        /// <c>webview_error</c> message the gree adapter reports. Null for an
        /// entry with no known prefix.
        /// </summary>
        public static string ToBridgeMessage(string entry)
        {
            if (string.IsNullOrEmpty(entry))
                return null;
            var body = entry.Substring(1);
            switch (entry[0])
            {
                case 'M':
                    return body;
                case 'E':
                    return "{\"type\":\"error\",\"payload\":{\"code\":\"webview_error\",\"message\":\"" +
                        CredentialBridge.EscapeJson(body) + "\"}}";
                default:
                    return null;
            }
        }
    }
}
