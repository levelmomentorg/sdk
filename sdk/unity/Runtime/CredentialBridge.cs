// ---------------------------------------------------------------------------
// LevelMoment Unity SDK — answering the page's `needCredential`.
//
// The credential used to ride on the hosted URL as `?token=`. It no longer
// does, in any shell: the page asks for one over the bridge and the host
// answers by running a line of script inside the page. This file builds that
// line, and it is shared by a break and the sign-in gate so the two can never
// hand over different credentials for the same launch.
//
// SECURE STORAGE IS NOT IMPLEMENTED HERE. React Native and Flutter keep a copy
// of the credential in the device keychain, so it survives the WebView's own
// storage being cleared. Unity has no such store without a native plugin, and
// this SDK does not ship one. So Unity answers with whatever token the game
// configured — usually none — and never asks for custody: the page keeps the
// credential in its own storage, exactly as before. See sdk/unity/README.md.
// ---------------------------------------------------------------------------

using System.Text;

namespace LevelMoment
{
    internal static class CredentialBridge
    {
        /// <summary>
        /// The JavaScript that hands <paramref name="token"/> to the hosted
        /// page. Pass an empty token to say "I hold nothing" — answering
        /// promptly is what stops the page waiting out its whole window.
        ///
        /// The reply is JSON, and the JSON is embedded as a JS string literal
        /// the page parses. That double encoding is the safety argument: a
        /// token is an opaque server string, but it comes from outside this
        /// method, and splicing it raw into a script would make any quote or
        /// backslash in it executable code inside the page.
        /// </summary>
        public static string BuildInjection(
            string token,
            string customData = null,
            string expectedOrigin = "https://levelmoment.com")
        {
            // custody is fixed false: nothing here can outlive the WebView's
            // own storage, so asking to be told about minted credentials would
            // pull a token into a process with nowhere safer to put it.
            var json = "{\"token\":\"" + EscapeJson(token ?? string.Empty) +
                       "\",\"custody\":false,\"customData\":" +
                       (customData == null ? "null" : "\"" + EscapeJson(customData) + "\"") +
                       ",\"protocolVersion\":1,\"sdkVersion\":\"0.2.0\"}";
            return "if (window.location.origin === \"" + EscapeJson(expectedOrigin) + "\") {" +
                   "window.__levelMomentDeliverCredential && " +
                   "window.__levelMomentDeliverCredential(\"" + EscapeJson(json) + "\");}";
        }

        /// <summary>
        /// Escape a string for a double-quoted JSON/JavaScript literal.
        /// Applied twice by <see cref="BuildInjection"/>: once for the JSON
        /// value, once for the JS literal that carries the JSON.
        /// </summary>
        internal static string EscapeJson(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            var sb = new StringBuilder(value.Length + 8);
            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                switch (c)
                {
                    case '\\':
                        sb.Append("\\\\");
                        break;
                    case '"':
                        sb.Append("\\\"");
                        break;
                    case '\n':
                        sb.Append("\\n");
                        break;
                    case '\r':
                        sb.Append("\\r");
                        break;
                    case '\t':
                        sb.Append("\\t");
                        break;
                    default:
                        // Control characters are not legal raw inside a JSON
                        // string and would break the page's parse. U+2028 and
                        // U+2029 are legal JSON but end a JavaScript line,
                        // which would cut the injected statement in half.
                        if (c < 0x20 || c == '\u2028' || c == '\u2029')
                            sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else
                            sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// Answer a page that asked for a credential, if the registered WebView
        /// provider can run script. A provider that cannot is not an error: the
        /// page waits its short window and then uses its own stored credential,
        /// which is where a paired device's credential lives anyway.
        /// </summary>
        public static void Deliver(
            ILevelMomentWebView webView,
            string token,
            string customData = null,
            string expectedOrigin = "https://levelmoment.com")
        {
            var scriptable = webView as ILevelMomentScriptableWebView;
            if (scriptable == null)
                return;
            scriptable.EvaluateJS(BuildInjection(token, customData, expectedOrigin));
        }
    }
}
