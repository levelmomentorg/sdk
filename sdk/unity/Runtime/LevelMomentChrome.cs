// ---------------------------------------------------------------------------
// LevelMoment Unity SDK — SDK-owned chrome: the loading layer over a hosted
// surface that has not posted `ready` yet, and the full-screen "No internet
// connection" message.
//
// Drawn with IMGUI from the runtime driver's OnGUI, so it needs no uGUI
// package, no Canvas and no EventSystem, and it draws over the game's own
// Screen Space Overlay canvases. IMGUI cannot stop the game reading input
// itself (Input System actions, uGUI clicks), which is why require-internet
// mode tells the game through OnConnectionChange and the game pauses there.
//
// A native WebView (gree on iOS/Android, the macOS view) sits above anything
// Unity draws. Providers that implement ILevelMomentRevealableWebView keep it
// hidden until the page posts `ready`, so this loading layer is what a player
// sees while the page loads.
//
// The layer list is pure state, which the EditMode tests drive directly;
// LevelMomentChromePainter is the IMGUI half. Mirrors sdk/web/src/chrome.ts.
// ---------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using UnityEngine;

namespace LevelMoment
{
    internal static class LevelMomentChrome
    {
        internal enum LayerKind
        {
            Loading,
            NoConnection,
        }

        internal sealed class Layer
        {
            public readonly LayerKind Kind;
            public readonly NoConnectionCopy Copy;
            private readonly Action _action;

            internal Layer(LayerKind kind, NoConnectionCopy copy, Action action)
            {
                Kind = kind;
                Copy = copy;
                _action = action;
            }

            /// <summary>
            /// Press the layer's one control: Close on a loading layer, Try
            /// again on the message.
            /// </summary>
            public void Press()
            {
                if (_action != null)
                    _action();
            }
        }

        // Drawn in order, so a later layer paints over an earlier one. The
        // message is always drawn last: no loading layer covers it.
        private static readonly List<Layer> _layers = new List<Layer>();

        internal static IList<Layer> Layers
        {
            get { return _layers; }
        }

        /// <summary>The no-connection message, or null when it is not showing.</summary>
        internal static Layer NoConnection
        {
            get
            {
                for (var i = 0; i < _layers.Count; i++)
                    if (_layers[i].Kind == LayerKind.NoConnection)
                        return _layers[i];
                return null;
            }
        }

        internal static int LoadingCount
        {
            get
            {
                var count = 0;
                for (var i = 0; i < _layers.Count; i++)
                    if (_layers[i].Kind == LayerKind.Loading)
                        count++;
                return count;
            }
        }

        /// <summary>A spinner and a Close control over a loading surface.</summary>
        internal static Layer ShowLoading(Action onClose)
        {
            var layer = new Layer(LayerKind.Loading, null, onClose);
            var message = NoConnection;
            if (message != null)
                _layers.Insert(_layers.IndexOf(message), layer);
            else
                _layers.Add(layer);
            LevelMomentRuntime.EnsureDriver();
            return layer;
        }

        internal static Layer ShowNoConnection(string gameName, Action onRetry)
        {
            var layer = new Layer(LayerKind.NoConnection, NoConnectionCopy.For(gameName), onRetry);
            _layers.Add(layer);
            LevelMomentRuntime.EnsureDriver();
            return layer;
        }

        internal static void Remove(Layer layer)
        {
            if (layer != null)
                _layers.Remove(layer);
            if (_pendingPress == layer)
                _pendingPress = null;
        }

        // A control pressed during OnGUI. Its action runs game callbacks, so it
        // waits for the driver's next Update rather than running mid-event.
        private static Layer _pendingPress;

        internal static void RequestPress(Layer layer)
        {
            _pendingPress = layer;
        }

        /// <summary>Run a press recorded during OnGUI. Called from the driver's Update.</summary>
        internal static void RunPendingPress()
        {
            var layer = _pendingPress;
            _pendingPress = null;
            if (layer != null && _layers.Contains(layer))
                layer.Press();
        }

        internal static void ResetStatic()
        {
            _layers.Clear();
            _pendingPress = null;
        }
    }

    /// <summary>The copy on the no-connection message, shared with web and Flutter.</summary>
    internal sealed class NoConnectionCopy
    {
        public readonly string Heading;
        public readonly string Body;
        public readonly string Button;

        private NoConnectionCopy(string heading, string body, string button)
        {
            Heading = heading;
            Body = body;
            Button = button;
        }

        public static NoConnectionCopy For(string gameName)
        {
            var trimmed = gameName == null ? string.Empty : gameName.Trim();
            var name = trimmed.Length == 0 ? "This game" : trimmed;
            return new NoConnectionCopy(
                "No internet connection",
                name + " needs an internet connection. Turn on Wi-Fi or mobile data, then try again.",
                "Try again");
        }
    }

    /// <summary>
    /// The IMGUI half of the chrome. Called from the runtime driver's OnGUI;
    /// nothing here runs in the EditMode tests.
    /// </summary>
    internal static class LevelMomentChromePainter
    {
        // The dark palette from sdk/web/src/chrome.ts. Unity has no portable
        // read of the system's light or dark setting, and dark is the web
        // and Flutter fallback too.
        private static readonly Color Background = Rgb(0x0b, 0x06, 0x18);
        private static readonly Color Text = Rgb(0xf8, 0xfa, 0xfc);
        private static readonly Color Muted = Rgb(0xcb, 0xd5, 0xe1);
        private static readonly Color CloseBackground = new Color(0.973f, 0.98f, 0.988f, 0.16f);

        private const float MaxContentWidth = 420f;
        private const float Padding = 24f;

        private static Texture2D _solid;
        private static Texture2D _pill;
        private static Texture2D _ring;
        private static GUIStyle _heading;
        private static GUIStyle _body;
        private static GUIStyle _button;
        private static GUIStyle _close;
        // Reused across OnGUI events so painting allocates nothing per frame.
        private static readonly List<LevelMomentChrome.Layer> _snapshot = new List<LevelMomentChrome.Layer>();
        private static readonly GUIContent _measure = new GUIContent(string.Empty);

        public static void Paint()
        {
            var layers = LevelMomentChrome.Layers;
            if (layers.Count == 0)
                return;
            EnsureAssets();

            var previousMatrix = GUI.matrix;
            var previousColor = GUI.color;
            try
            {
                _snapshot.Clear();
                _snapshot.AddRange(layers);
                for (var i = 0; i < _snapshot.Count; i++)
                {
                    var layer = _snapshot[i];
                    // Only the top layer takes input.
                    var top = i == _snapshot.Count - 1;
                    if (layer.Kind == LevelMomentChrome.LayerKind.NoConnection)
                        PaintNoConnection(layer, top);
                    else
                        PaintLoading(layer, top);
                }
            }
            finally
            {
                _snapshot.Clear();
                GUI.matrix = previousMatrix;
                GUI.color = previousColor;
            }

            // Nothing else drawn with IMGUI reacts to a press under the chrome.
            var e = Event.current;
            if (e != null && e.type != EventType.Repaint && e.type != EventType.Layout)
                e.Use();
        }

        private static void PaintNoConnection(LevelMomentChrome.Layer layer, bool top)
        {
            float scale;
            var area = BeginLayer(out scale);

            // `area` is already inset by the padding.
            var width = Mathf.Min(MaxContentWidth, area.width);
            var copy = layer.Copy;
            var headingHeight = _heading.CalcHeight(Measure(copy.Heading), width);
            var bodyHeight = _body.CalcHeight(Measure(copy.Body), width);
            const float buttonHeight = 48f;
            var buttonWidth = Mathf.Max(160f, _button.CalcSize(Measure(copy.Button)).x + 48f);
            var total = headingHeight + 12f + bodyHeight + 24f + buttonHeight;

            var x = area.x + (area.width - width) / 2f;
            var y = area.y + Mathf.Max(0f, (area.height - total) / 2f);
            GUI.Label(new Rect(x, y, width, headingHeight), copy.Heading, _heading);
            y += headingHeight + 12f;
            GUI.Label(new Rect(x, y, width, bodyHeight), copy.Body, _body);
            y += bodyHeight + 24f;

            var button = new Rect(area.x + (area.width - buttonWidth) / 2f, y, buttonWidth, buttonHeight);
            if (top && (GUI.Button(button, copy.Button, _button) || SubmitPressed()))
                LevelMomentChrome.RequestPress(layer);
            else if (!top)
                GUI.Label(button, copy.Button, _button);
        }

        private static void PaintLoading(LevelMomentChrome.Layer layer, bool top)
        {
            float scale;
            var area = BeginLayer(out scale);

            // The spinner: a ring with one bright quarter, turning once a second.
            const float size = 40f;
            var spinner = new Rect(
                area.x + (area.width - size) / 2f, area.y + (area.height - size) / 2f, size, size);
            var angle = (Time.realtimeSinceStartup % 1f) * 360f;
            var matrix = GUI.matrix;
            // RotateAroundPivot takes its pivot in screen pixels, before the
            // point scale in GUI.matrix.
            var pivot = spinner.center;
            GUIUtility.RotateAroundPivot(angle, new Vector2(pivot.x * scale, pivot.y * scale));
            GUI.DrawTexture(spinner, _ring);
            GUI.matrix = matrix;

            const float closeSize = 44f;
            var close = new Rect(area.xMax + Padding - 12f - closeSize, area.y - Padding + 12f, closeSize, closeSize);
            GUI.color = CloseBackground;
            GUI.DrawTexture(close, _pill);
            GUI.color = Color.white;
            if (top && (GUI.Button(close, "×", _close) || CancelPressed()))
                LevelMomentChrome.RequestPress(layer);
            else if (!top)
                GUI.Label(close, "×", _close);
        }

        /// <summary>
        /// Fill the screen, then scale to density-independent points and return
        /// the safe area inset by the layer padding, in those points.
        /// </summary>
        private static Rect BeginLayer(out float scale)
        {
            GUI.matrix = Matrix4x4.identity;
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _solid);

            scale = Screen.dpi > 0 ? Mathf.Max(1f, Screen.dpi / 160f) : 1f;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

            // Screen.safeArea is in pixels from the bottom-left; IMGUI is
            // top-left.
            var safe = Screen.safeArea;
            var top = Screen.height - safe.yMax;
            return new Rect(
                safe.x / scale + Padding,
                top / scale + Padding,
                safe.width / scale - 2 * Padding,
                safe.height / scale - 2 * Padding);
        }

        private static GUIContent Measure(string text)
        {
            _measure.text = text;
            return _measure;
        }

        private static bool SubmitPressed()
        {
            var e = Event.current;
            if (e == null || e.type != EventType.KeyDown)
                return false;
            return e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter;
        }

        private static bool CancelPressed()
        {
            var e = Event.current;
            return e != null && e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape;
        }

        private static void EnsureAssets()
        {
            if (_solid == null)
            {
                _solid = NewTexture(1, 1);
                _solid.SetPixel(0, 0, Background);
                _solid.Apply();
            }
            if (_pill == null)
                _pill = Disc(48, Color.white);
            if (_ring == null)
                _ring = Ring(80);
            if (_heading == null)
            {
                _heading = new GUIStyle();
                _heading.fontSize = 24;
                _heading.fontStyle = FontStyle.Bold;
                _heading.alignment = TextAnchor.UpperCenter;
                _heading.wordWrap = true;
                _heading.normal.textColor = Text;

                _body = new GUIStyle();
                _body.fontSize = 16;
                _body.alignment = TextAnchor.UpperCenter;
                _body.wordWrap = true;
                _body.normal.textColor = Muted;

                _button = new GUIStyle();
                _button.fontSize = 16;
                _button.fontStyle = FontStyle.Bold;
                _button.alignment = TextAnchor.MiddleCenter;
                _button.normal.background = Disc(48, Text);
                _button.normal.textColor = Background;
                _button.hover.background = _button.normal.background;
                _button.hover.textColor = Background;
                _button.active.background = _button.normal.background;
                _button.active.textColor = Background;
                // Nine-slice the disc into a pill of any width.
                _button.border = new RectOffset(24, 24, 24, 24);

                _close = new GUIStyle();
                _close.fontSize = 28;
                _close.alignment = TextAnchor.MiddleCenter;
                _close.normal.textColor = Text;
                _close.hover.textColor = Text;
                _close.active.textColor = Text;
            }
        }

        private static Texture2D NewTexture(int width, int height)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.hideFlags = HideFlags.HideAndDontSave;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            return texture;
        }

        // An anti-aliased filled circle.
        private static Texture2D Disc(int size, Color color)
        {
            var texture = NewTexture(size, size);
            var r = size / 2f;
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var d = Distance(x, y, r);
                    var alpha = Mathf.Clamp01(r - d);
                    texture.SetPixel(x, y, new Color(color.r, color.g, color.b, color.a * alpha));
                }
            }
            texture.Apply();
            return texture;
        }

        // A 4-point ring (at 40-point display size): dim all round, with one
        // bright quarter, like the web spinner's border-top.
        private static Texture2D Ring(int size)
        {
            var texture = NewTexture(size, size);
            var r = size / 2f;
            var thickness = size / 10f;
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var d = Distance(x, y, r);
                    var coverage = Mathf.Clamp01(r - d) * Mathf.Clamp01(d - (r - thickness));
                    var angle = Mathf.Atan2(y + 0.5f - r, x + 0.5f - r) * Mathf.Rad2Deg;
                    var bright = angle >= 45f && angle < 135f;
                    var c = bright ? Text : CloseBackground;
                    texture.SetPixel(x, y, new Color(c.r, c.g, c.b, c.a * coverage));
                }
            }
            texture.Apply();
            return texture;
        }

        private static float Distance(int x, int y, float r)
        {
            var dx = x + 0.5f - r;
            var dy = y + 0.5f - r;
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        private static Color Rgb(int r, int g, int b)
        {
            return new Color(r / 255f, g / 255f, b / 255f, 1f);
        }
    }
}
