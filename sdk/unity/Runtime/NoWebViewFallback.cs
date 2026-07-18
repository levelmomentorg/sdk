// ---------------------------------------------------------------------------
// LevelMoment Unity SDK — no-op WebView fallback.
//
// Used when no ILevelMomentWebView provider is registered. Open() immediately
// surfaces an `error` message through the normal bridge channel, so Show()
// reports it via OnAdFailedToShow and the game resumes cleanly — the same path
// a page-posted error takes. The message tells the developer exactly how to
// install a WebView provider.
// ---------------------------------------------------------------------------

using System;

namespace LevelMoment
{
    internal class NoWebViewFallback : ILevelMomentWebView
    {
        // OnClosed is required by the interface but never raised here.
#pragma warning disable 0067
        public event Action OnClosed;
#pragma warning restore 0067

        public event Action<string> OnMessage;

        public const string ErrorJson =
            "{\"type\":\"error\",\"payload\":{" +
            "\"code\":\"no_webview_provider\"," +
            "\"message\":\"No WebView provider is installed. Install gree/unity-webview and " +
            "add the LEVELMOMENT_GREE_WEBVIEW scripting define (see sdk/unity/README.md), or " +
            "register a custom ILevelMomentWebView via LevelMomentWebViewRegistry.Register().\"}}";

        public void Open(string url)
        {
            var handler = OnMessage;
            if (handler != null)
                handler(ErrorJson);
        }

        public void Close()
        {
        }
    }
}
