// ---------------------------------------------------------------------------
// LevelMoment Unity SDK — WebView provider seam.
//
// Unity ships no built-in WebView, so the SDK depends on a pluggable provider.
// A provider opens a fullscreen WebView at the hosted /break URL and surfaces
// the page's bridge messages as raw JSON strings. The bundled provider is a
// gree/unity-webview adapter (compiled only when LEVELMOMENT_GREE_WEBVIEW is
// set); other WebView plugins (Vuplex, 3D WebView) can register their own.
//
// See docs/ADR-001-webview-rendering.md and sdk/unity/README.md.
// ---------------------------------------------------------------------------

using System;

namespace LevelMoment
{
    /// <summary>
    /// A native WebView host that opens the hosted /break page and relays its
    /// postMessage/bridge events. Implement this to plug in a WebView plugin.
    /// </summary>
    public interface ILevelMomentWebView
    {
        /// <summary>
        /// Fired for each message the page posts over its native bridge, as the
        /// raw JSON string (parsed by the SDK, not the provider).
        /// </summary>
        event Action<string> OnMessage;

        /// <summary>
        /// Fired if the native WebView is closed by the platform (e.g. a hardware
        /// back gesture) rather than by a page-posted <c>dismissed</c>. Providers
        /// that cannot detect this need never raise it — the page still posts a
        /// terminal event.
        /// </summary>
        event Action OnClosed;

        /// <summary>Open a fullscreen WebView at <paramref name="url"/>.</summary>
        void Open(string url);

        /// <summary>Close and release the WebView.</summary>
        void Close();
    }

    /// <summary>
    /// Registry of the active <see cref="ILevelMomentWebView"/> factory. The gree
    /// adapter self-registers via [RuntimeInitializeOnLoadMethod] when
    /// LEVELMOMENT_GREE_WEBVIEW is set; custom providers call
    /// <see cref="Register"/> themselves. With no provider registered,
    /// <see cref="Create"/> returns a <see cref="NoWebViewFallback"/> that fails
    /// Show() with a clear, actionable error.
    /// </summary>
    public static class LevelMomentWebViewRegistry
    {
        private static Func<ILevelMomentWebView> _factory;

        /// <summary>Register the WebView factory used by every subsequent Show().</summary>
        public static void Register(Func<ILevelMomentWebView> factory)
        {
            _factory = factory;
        }

        /// <summary>True when a real provider has been registered.</summary>
        public static bool HasProvider
        {
            get { return _factory != null; }
        }

        /// <summary>
        /// Create a WebView from the registered factory, or a
        /// <see cref="NoWebViewFallback"/> when none is registered.
        /// </summary>
        public static ILevelMomentWebView Create()
        {
            return _factory != null ? _factory() : new NoWebViewFallback();
        }

        /// <summary>Clear the registered factory. Intended for tests.</summary>
        public static void Reset()
        {
            _factory = null;
        }
    }
}
