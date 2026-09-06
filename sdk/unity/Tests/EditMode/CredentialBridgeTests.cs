// EditMode tests for CredentialBridge — how the SDK answers the hosted page's
// `needCredential` now that the credential no longer rides on the /break URL.
// Pure C#, no WebView. See BreakUrlTests for the URL side of this change, and
// RewardedAdTests / SignInGateTests for the bridge wired end-to-end.
//
// Run from Unity: Window → General → Test Runner → EditMode → Run All.

using System;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using LevelMoment;

namespace LevelMoment.Tests.EditMode
{
    public class CredentialBridgeTests
    {
        // ---- Fakes ------------------------------------------------------------
        //
        // Neither fake ever raises OnMessage/OnClosed — these tests drive
        // CredentialBridge directly rather than through a surface. The events
        // exist only to satisfy the interface, which is what CS0067 reports.
#pragma warning disable 0067

        /// <summary>Implements only the plain interface — the WebView plugins
        /// that predate ILevelMomentScriptableWebView, or a provider that never
        /// added script-evaluation support.</summary>
        private class PlainWebView : ILevelMomentWebView
        {
            public event System.Action<string> OnMessage;
            public event System.Action OnClosed;

            public void Open(string url) { }
            public void Close() { }
        }

        /// <summary>A provider that can run script in the loaded page.</summary>
        private class ScriptableWebView : ILevelMomentScriptableWebView
        {
            public event System.Action<string> OnMessage;
            public event System.Action OnClosed;

            public int EvaluateJSCount;
            public string LastJS;

            public void Open(string url) { }
            public void Close() { }

            public void EvaluateJS(string js)
            {
                EvaluateJSCount++;
                LastJS = js;
            }
        }

#pragma warning restore 0067

        // ---- BuildInjection -----------------------------------------------

        [Test]
        public void BuildInjection_CallsTheDeliverCredentialHook()
        {
            var js = CredentialBridge.BuildInjection("tok-1");
            StringAssert.Contains("window.__levelMomentDeliverCredential", js);
        }

        [Test]
        public void BuildInjection_CarriesTheToken()
        {
            var js = CredentialBridge.BuildInjection("tok-1");
            StringAssert.Contains("tok-1", js);
        }

        [Test]
        public void BuildInjection_CustodyIsAlwaysFalse()
        {
            // Fixed false: Unity has no secure store, so it never asks to be
            // told about a newly minted credential.
            var js = CredentialBridge.BuildInjection("tok-1");
            StringAssert.Contains("\\\"custody\\\":false", js);
        }

        [Test]
        public void BuildInjection_BindsReplyToExpectedOriginAndProtocol()
        {
            var js = CredentialBridge.BuildInjection(
                "tok-1", "impression-7", "https://levelmoment.com");

            StringAssert.Contains("window.location.origin ===", js);
            StringAssert.Contains("https://levelmoment.com", js);
            StringAssert.Contains("\\\"customData\\\":\\\"impression-7\\\"", js);
            StringAssert.Contains("\\\"protocolVersion\\\":1", js);
            StringAssert.Contains("\\\"sdkVersion\\\":\\\"0.2.0\\\"", js);
        }

        [Test]
        public void BuildInjection_EmptyToken_StillProducesAValidCall()
        {
            var js = CredentialBridge.BuildInjection("");
            StringAssert.Contains("window.__levelMomentDeliverCredential", js);
            StringAssert.Contains("\\\"token\\\":\\\"\\\"", js);
        }

        [Test]
        public void BuildInjection_NullToken_IsTreatedAsEmpty()
        {
            var js = CredentialBridge.BuildInjection(null);
            StringAssert.Contains("\\\"token\\\":\\\"\\\"", js);
        }

        // A token is an opaque server string, but nothing stops it containing
        // characters that are special to JSON or to a JS string literal. The
        // reply is JSON-encoded once, then that JSON is embedded as a quoted JS
        // string literal — two layers of escaping. A token holding a raw quote
        // and a raw backslash must not survive either layer unescaped, or it
        // could close the outer JS string early and splice arbitrary script
        // into the page.
        [Test]
        public void BuildInjection_TokenWithQuoteAndBackslash_CannotBreakOutOfTheJsStringLiteral()
        {
            var token = "abc\"; alert(1); //\\";
            var js = CredentialBridge.BuildInjection(token);

            // Reading the reply back the way the page's runtime does is the only
            // assertion that actually proves the escaping. Substring matching on
            // the raw script cannot: the injection's own literal delimiters are
            // unescaped quotes by design, so any rule strict enough to catch a
            // token breaking out also condemns the well-formed output.
            var reply = DecodeInjection(js);

            Assert.AreEqual(token, reply.token);
            Assert.IsFalse(reply.custody);
        }

        /// <summary>
        /// Recover the reply an injection carries: unwrap the JS string literal
        /// the page's parser would see, then read the JSON inside it. Throws a
        /// test failure if the argument is not one well-formed literal — which
        /// is exactly what a token breaking out of the literal would produce.
        /// </summary>
        private static WireReply DecodeInjection(string js)
        {
            var open = js.LastIndexOf("__levelMomentDeliverCredential(", StringComparison.Ordinal);
            Assert.GreaterOrEqual(open, 0, "injection shape changed: " + js);
            open = js.IndexOf('(', open) + 1;

            var close = js.LastIndexOf(");", StringComparison.Ordinal);
            Assert.Greater(close, open, "injection shape changed: " + js);

            var literal = js.Substring(open, close - open);
            Assert.IsTrue(
                literal.Length >= 2 && literal[0] == '"' && literal[literal.Length - 1] == '"',
                "the argument is not a single string literal: " + literal);

            var json = JsUnescape(literal.Substring(1, literal.Length - 2));
            var reply = JsonUtility.FromJson<WireReply>(json);
            Assert.IsNotNull(reply, "the literal did not carry parseable JSON: " + json);
            return reply;
        }

        /// <summary>
        /// Reverse one layer of the escaping BuildInjection applies — the layer
        /// a JavaScript engine undoes when it reads a double-quoted literal.
        /// </summary>
        private static string JsUnescape(string s)
        {
            var sb = new StringBuilder(s.Length);
            for (var i = 0; i < s.Length; i++)
            {
                if (s[i] != '\\')
                {
                    // A raw quote here would mean the literal ended early — the
                    // break-out this test exists to rule out.
                    Assert.AreNotEqual('"', s[i], "unescaped quote inside the literal: " + s);
                    sb.Append(s[i]);
                    continue;
                }

                i++;
                Assert.Less(i, s.Length, "literal ends on a dangling backslash: " + s);
                switch (s[i])
                {
                    case '\\': sb.Append('\\'); break;
                    case '"': sb.Append('"'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        sb.Append((char)Convert.ToInt32(s.Substring(i + 1, 4), 16));
                        i += 4;
                        break;
                    default:
                        Assert.Fail("unknown escape \\" + s[i] + " in " + s);
                        break;
                }
            }
            return sb.ToString();
        }

        // JsonUtility DTO for the reply the injection carries. Field names must
        // match the JSON keys the page reads.
#pragma warning disable 0649
        [Serializable]
        private class WireReply
        {
            public string token;
            public bool custody;
        }
#pragma warning restore 0649

        // ---- EscapeJson ------------------------------------------------------

        [Test]
        public void EscapeJson_NullOrEmpty_ReturnsEmpty()
        {
            Assert.AreEqual(string.Empty, CredentialBridge.EscapeJson(null));
            Assert.AreEqual(string.Empty, CredentialBridge.EscapeJson(""));
        }

        [Test]
        public void EscapeJson_PlainString_IsUnchanged()
        {
            Assert.AreEqual("abc123", CredentialBridge.EscapeJson("abc123"));
        }

        [Test]
        public void EscapeJson_LineSeparatorsThatWouldEndAJsStatement_AreEscaped()
        {
            var escaped = CredentialBridge.EscapeJson("a\u2028b\u2029c");
            StringAssert.Contains("\\u2028", escaped);
            StringAssert.Contains("\\u2029", escaped);
        }

        // ---- Deliver ----------------------------------------------------------

        [Test]
        public void Deliver_ScriptableWebView_EvaluatesTheInjection()
        {
            var fake = new ScriptableWebView();
            CredentialBridge.Deliver(fake, "tok-1");

            Assert.AreEqual(1, fake.EvaluateJSCount);
            StringAssert.Contains("tok-1", fake.LastJS);
            StringAssert.Contains("window.__levelMomentDeliverCredential", fake.LastJS);
        }

        // A provider that only implements the plain interface cannot run
        // script. That is not an error — the page waits its short window and
        // then falls back to whatever credential it already has stored.
        [Test]
        public void Deliver_PlainWebView_IsASilentNoOp()
        {
            var fake = new PlainWebView();
            Assert.DoesNotThrow(() => CredentialBridge.Deliver(fake, "tok-1"));
        }
    }
}
