// ---------------------------------------------------------------------------
// LevelMoment Unity SDK — gree/unity-webview adapter.
//
// Bridges the hosted /break page to Unity through the open-source
// gree/unity-webview plugin (https://github.com/gree/unity-webview). The page
// sends messages via `Unity.call(json)` (see the shim in
// platform/web/app/break/postToHost.ts); gree routes that to the `cb`
// Action<string> passed to WebViewObject.Init().
//
// Compiled ONLY when the LEVELMOMENT_GREE_WEBVIEW scripting define is set AND
// the gree package is installed (both this file's #if and the sibling
// LevelMomentSDK.Gree.asmdef defineConstraint gate it). Absent either, the SDK
// falls back to NoWebViewFallback, which fails Show() with install guidance.
// See sdk/unity/README.md for setup.
// ---------------------------------------------------------------------------

#if LEVELMOMENT_GREE_WEBVIEW
using System;
using UnityEngine;
#if LEVELMOMENT_GREE_UPM
// The UPM dist packages (net.gree.unity-webview, dist/package*) namespace the
// plugin; the classic copy-into-Assets install keeps WebViewObject global.
// LEVELMOMENT_GREE_UPM is set by a versionDefine in LevelMomentSDK.Gree.asmdef
// whenever the UPM package is present, so both install styles compile.
using Gree.UnityWebView;
#endif

namespace LevelMoment
{
    internal class GreeWebViewAdapter : ILevelMomentWebView
    {
        // OnClosed is required by the interface. gree surfaces no native "closed"
        // callback (the hosted page posts `dismissed` itself), so it is unused.
#pragma warning disable 0067
        public event Action OnClosed;
#pragma warning restore 0067

        public event Action<string> OnMessage;

        private GameObject _gameObject;
        private WebViewObject _webView;

        public void Open(string url)
        {
            _gameObject = new GameObject("[LevelMomentWebView]");
            _webView = _gameObject.AddComponent<WebViewObject>();

            // cb receives page → host messages (Unity.call(json)); err receives
            // native load failures, which we relay as an `error` bridge message.
            _webView.Init(
                cb: OnBridgeMessage,
                err: OnBridgeError,
                enableWKWebView: true);

            _webView.SetMargins(0, 0, 0, 0);
            _webView.SetVisibility(true);
            _webView.LoadURL(url);
        }

        public void Close()
        {
            if (_webView != null)
                _webView.SetVisibility(false);
            if (_gameObject != null)
                UnityEngine.Object.Destroy(_gameObject);
            _webView = null;
            _gameObject = null;
        }

        private void OnBridgeMessage(string message)
        {
            var handler = OnMessage;
            if (handler != null)
                handler(message);
        }

        private void OnBridgeError(string message)
        {
            var handler = OnMessage;
            if (handler == null)
                return;
            handler(
                "{\"type\":\"error\",\"payload\":{\"code\":\"webview_error\",\"message\":\"" +
                EscapeJson(message) + "\"}}");
        }

        private static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s))
                return string.Empty;
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", " ");
        }
    }

    /// <summary>
    /// Self-registers the gree adapter as the WebView provider at startup, so
    /// developers only need to install gree + add the scripting define.
    /// </summary>
    internal static class GreeWebViewAutoRegister
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            LevelMomentWebViewRegistry.Register(delegate { return new GreeWebViewAdapter(); });
        }
    }
}
#endif
