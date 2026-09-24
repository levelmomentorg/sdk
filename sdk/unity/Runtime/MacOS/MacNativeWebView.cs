// ---------------------------------------------------------------------------
// LevelMoment Unity SDK — native macOS WebView provider.
//
// gree/unity-webview draws a macOS player's WebView into a texture from a hidden
// window parked off-screen, and replays Unity mouse events into it. Assistive
// technology finds the page only in that hidden window, at off-screen
// positions, so VoiceOver's cursor, Voice Control, and position-based UI
// automation miss it, and pointer input depends on a coordinate remap. On a
// macOS standalone player this provider replaces it with a real WKWebView
// inside the player window (Plugins/macOS/LevelMomentWebView.bundle, source in
// Native~/macOS), so the hosted break is an ordinary accessible web page.
//
// It needs no gree install and no scripting define. The gree adapter defers to
// it on macOS players. It registers at AfterAssembliesLoaded, ahead of every
// BeforeSceneLoad hook, so a provider the game registers itself still wins.
// ---------------------------------------------------------------------------

#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using UnityEngine.Scripting;

namespace LevelMoment
{
    internal sealed class MacNativeWebView :
        ILevelMomentHeadlessWebView, ILevelMomentScriptableWebView, ITickable
    {
        private const string Lib = "LevelMomentWebView";

        [DllImport(Lib)] private static extern int LevelMomentWebView_Version();
        [DllImport(Lib)] private static extern IntPtr LevelMomentWebView_Create(byte[] origin);
        [DllImport(Lib)] private static extern void LevelMomentWebView_Load(IntPtr instance, byte[] url);
        [DllImport(Lib)] private static extern void LevelMomentWebView_SetVisible(IntPtr instance, int visible);
        [DllImport(Lib)] private static extern void LevelMomentWebView_EvaluateJS(IntPtr instance, byte[] js);
        [DllImport(Lib)] private static extern IntPtr LevelMomentWebView_Poll(IntPtr instance);
        [DllImport(Lib)] private static extern void LevelMomentWebView_Free(IntPtr text);
        [DllImport(Lib)] private static extern void LevelMomentWebView_Destroy(IntPtr instance);

        // OnClosed is required by the interface. The view has no platform close
        // control (the hosted page posts `dismissed` itself), so it is unused.
#pragma warning disable 0067
        public event Action OnClosed;
#pragma warning restore 0067

        public event Action<string> OnMessage;

        private IntPtr _instance;

        /// <summary>
        /// True when the native bundle loaded in this player. Probed once; a
        /// build that stripped or failed to sign the bundle falls back to gree.
        /// </summary>
        internal static bool IsAvailable
        {
            get
            {
                if (!_probed)
                {
                    _probed = true;
                    try
                    {
                        _available = LevelMomentWebView_Version() >= 1;
                    }
                    catch (Exception err)
                    {
                        Debug.LogWarning("[LevelMoment] The macOS WebView bundle did not load (" +
                            err.GetType().Name + "); falling back to any other registered provider.");
                        _available = false;
                    }
                }
                return _available;
            }
        }

        private static bool _probed;
        private static bool _available;

        public void Open(string url)
        {
            Load(url, true);
        }

        public void OpenHidden(string url)
        {
            Load(url, false);
        }

        private void Load(string url, bool visible)
        {
            if (_instance != IntPtr.Zero)
                throw new InvalidOperationException("This WebView is already open; create a new one per surface.");
            var origin = MacWebViewEntry.OriginOf(url);
            if (origin == null)
                throw new InvalidOperationException("The break URL is not an absolute http(s) URL.");
            _instance = LevelMomentWebView_Create(Utf8(origin));
            if (_instance == IntPtr.Zero)
                throw new InvalidOperationException("The macOS WebView could not attach to the player window.");
            LevelMomentRuntime.Track(this);
            LevelMomentWebView_SetVisible(_instance, visible ? 1 : 0);
            LevelMomentWebView_Load(_instance, Utf8(url));
        }

        public void EvaluateJS(string js)
        {
            if (_instance != IntPtr.Zero)
                LevelMomentWebView_EvaluateJS(_instance, Utf8(js));
        }

        public void Close()
        {
            LevelMomentRuntime.Untrack(this);
            if (_instance == IntPtr.Zero)
                return;
            var instance = _instance;
            _instance = IntPtr.Zero;
            LevelMomentWebView_Destroy(instance);
        }

        void ITickable.Tick()
        {
            // Drain everything queued since the last frame. A handler may close
            // this view mid-drain (a terminal message), so re-check each pass.
            while (_instance != IntPtr.Zero)
            {
                var raw = LevelMomentWebView_Poll(_instance);
                if (raw == IntPtr.Zero)
                    return;
                string entry;
                try
                {
                    entry = FromUtf8(raw);
                }
                finally
                {
                    LevelMomentWebView_Free(raw);
                }
                var message = MacWebViewEntry.ToBridgeMessage(entry);
                var handler = OnMessage;
                if (message != null && handler != null)
                    handler(message);
            }
        }

        private static byte[] Utf8(string s)
        {
            var bytes = Encoding.UTF8.GetBytes(s ?? string.Empty);
            var terminated = new byte[bytes.Length + 1];
            Buffer.BlockCopy(bytes, 0, terminated, 0, bytes.Length);
            return terminated;
        }

        private static string FromUtf8(IntPtr ptr)
        {
            var bytes = new List<byte>(256);
            for (var offset = 0; ; offset++)
            {
                var b = Marshal.ReadByte(ptr, offset);
                if (b == 0)
                    break;
                bytes.Add(b);
            }
            return Encoding.UTF8.GetString(bytes.ToArray());
        }
    }

    // Nothing references this class, so managed code stripping would remove it
    // without [Preserve] in a game whose link.xml does not preserve the whole
    // LevelMomentSDK.Runtime assembly.
    [Preserve]
    internal static class MacNativeWebViewAutoRegister
    {
        [Preserve]
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void Register()
        {
            if (MacNativeWebView.IsAvailable)
                LevelMomentWebViewRegistry.Register(delegate { return new MacNativeWebView(); });
        }
    }
}
#endif
