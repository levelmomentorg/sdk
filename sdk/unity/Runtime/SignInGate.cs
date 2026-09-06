// ---------------------------------------------------------------------------
// LevelMoment Unity SDK — the startup sign-in gate (ADR-001 WebView shell).
//
// Both entry points open the hosted /break page in the same WebView a break
// uses, differing only in the `mode` param and whether the surface is shown:
//
//   LevelMomentAds.EnsureSignedIn → ?mode=gate    fullscreen; pairs if needed
//   LevelMomentAds.IsSignedIn     → ?mode=check   headless; validates, closes
//
// Why IsSignedIn needs a WebView at all: the device credential lives in the
// hosted page's storage, on the hosted origin. The SDK cannot read it — that is
// the point of putting it there — so the only place that can answer
// authoritatively is the hosted page itself. It calls POST /device-checks and
// posts the verdict back.
//
// Structurally mirrors RewardedAd: the same terminal-once guard, the same
// pre-`ready` LoadWatchdog, the same teardown. Mirrors sdk/web/src/gate.ts
// message-for-message.
// ---------------------------------------------------------------------------

using System;

namespace LevelMoment
{
    /// <summary>
    /// The result of the startup gate. Every Level Moment SDK reports these
    /// three values, so a publisher shipping on two platforms writes the startup
    /// branch once. None of them reveals subscription, tier, or quota.
    /// </summary>
    public enum EnsureSignedInResult
    {
        /// <summary>Start the game.</summary>
        Ready,

        /// <summary>
        /// A person closed the gate, or a parent denied the connection. Show your
        /// own "learning breaks are off" state or a retry action; do not retry
        /// automatically.
        /// </summary>
        Canceled,

        /// <summary>The gate could not run. Retry later.</summary>
        TechnicalFailure,
    }

    internal class SignInGate : ITickable
    {
        private readonly Action _onReady;
        private readonly Action _onCanceled;
        private readonly Action<string> _onFailure;
        // Answered to the page's `needCredential`. Usually empty: a paired
        // device's credential lives on the hosted origin, not here.
        private readonly string _studentToken;

        private ILevelMomentWebView _webView;
        // The hosted URL this gate opened. An `openExternal` message is judged
        // against its origin, so a WebView that wandered off cannot send a
        // parent to a page of its own choosing.
        private string _hostedUrl;
        private LoadWatchdog _watchdog;
        // The headless check's total deadline. Never notified of `ready` — that
        // is what separates it from _watchdog, which stops there.
        private LoadWatchdog _checkDeadline;
        private bool _terminal;

        private SignInGate(
            Action onReady,
            Action onCanceled,
            Action<string> onFailure,
            string studentToken)
        {
            _onReady = onReady;
            _onCanceled = onCanceled;
            _onFailure = onFailure;
            _studentToken = studentToken;
        }

        /// <summary>
        /// Open the hosted surface at <paramref name="url"/> and settle exactly
        /// once. With <paramref name="headless"/> true the surface stays hidden
        /// if the registered provider implements
        /// <see cref="ILevelMomentHeadlessWebView"/>.
        /// </summary>
        public static SignInGate Open(
            string url,
            bool headless,
            Action onReady,
            Action onCanceled,
            Action<string> onFailure,
            string studentToken = null)
        {
            var gate = new SignInGate(onReady, onCanceled, onFailure, studentToken);
            gate._hostedUrl = url;

            try
            {
                gate._webView = LevelMomentWebViewRegistry.Create();
                gate._webView.OnMessage += gate.HandleRawMessage;
                gate._webView.OnClosed += gate.HandleClosed;

                gate._watchdog = new LoadWatchdog(
                    LevelMomentAds.LoadTimeoutSeconds, LevelMomentAds.ClockSeconds);
                gate._watchdog.Start();

                // `headless` marks the credential check, the one caller with
                // nobody watching and nothing to approve. Only it gets a total
                // deadline: EnsureSignedIn waits as long as a parent takes.
                if (headless)
                {
                    gate._checkDeadline = new LoadWatchdog(
                        LevelMomentAds.CheckTimeoutSeconds, LevelMomentAds.ClockSeconds);
                    gate._checkDeadline.Start();
                }

                LevelMomentRuntime.Track(gate);

                // Open last: the NoWebViewFallback posts its error synchronously
                // here, and the handlers/watchdog are already wired to receive it.
                var headlessView = gate._webView as ILevelMomentHeadlessWebView;
                if (headless && headlessView != null)
                    headlessView.OpenHidden(url);
                else
                    gate._webView.Open(url);
            }
            catch (Exception)
            {
                // A third-party provider that throws out of its factory or its
                // Open() would otherwise escape into the publisher's boot path —
                // past EnsureSignedIn's three-results contract, past IsSignedIn's
                // onError, and leaving a tracked gate ticking forever. Route it
                // through the same failure path a posted `error` takes, which
                // also tears the gate down.
                gate.Terminate(delegate { Fire(gate._onFailure, "webview_error"); });
            }

            return gate;
        }

        void ITickable.Tick()
        {
            Tick();
        }

        internal void Tick()
        {
            if (_terminal)
                return;
            if (_watchdog != null && _watchdog.Tick())
            {
                // The surface never came up. For the gate that is a technical
                // failure, not a person saying no — mirroring sdk/web, which
                // resolves technicalFailure on the pre-`ready` watchdog.
                Terminate(delegate { Fire(_onFailure, "load_timeout"); });
                return;
            }
            if (_checkDeadline != null && _checkDeadline.Tick())
            {
                // The check loaded and then went quiet. IsSignedIn has to
                // settle on something, and "unknown" is the only honest one.
                Terminate(delegate { Fire(_onFailure, "check_timeout"); });
            }
        }

        private void HandleRawMessage(string raw)
        {
            if (_terminal)
                return;
            var msg = HostMessage.TryParse(raw);
            if (msg == null)
                return; // malformed / unknown — dropped, like the other shells.

            switch (msg.Type)
            {
                case HostMessageType.NeedCredential:
                    // The page asks rather than reading a token off its URL.
                    CredentialBridge.Deliver(
                        _webView,
                        LevelMomentAds.Config.Mock ? null : _studentToken,
                        LevelMomentAds.Config.CustomData,
                        OriginOf(_hostedUrl));
                    break;
                case HostMessageType.CredentialIssued:
                case HostMessageType.CredentialInvalid:
                    // Unity keeps no copy to reconcile (no secure store yet).
                    break;
                case HostMessageType.OpenExternal:
                    // A parent is off to approve the game in the device's
                    // browser. The gate stays open; the page's poll ends it.
                    ExternalBrowser.Open(msg.Url, _hostedUrl);
                    break;
                case HostMessageType.Ready:
                    // The page owns the lifecycle now. Pairing takes as long as
                    // a parent takes to walk to another room and scan a code, so
                    // `ready` stops the load watchdog for good. It deliberately
                    // does NOT stop _checkDeadline: the headless check has no
                    // parent to wait on, and its whole run stays bounded.
                    if (_watchdog != null)
                        _watchdog.NotifyReady();
                    break;
                case HostMessageType.SignedIn:
                    Terminate(_onReady);
                    break;
                case HostMessageType.Dismissed:
                    Terminate(_onCanceled);
                    break;
                case HostMessageType.Error:
                    var code = msg.Code;
                    Terminate(delegate { Fire(_onFailure, code); });
                    break;
            }
        }

        private void HandleClosed()
        {
            // Native-initiated close (e.g. hardware back). A person closed the
            // gate, which is the same answer as a parent's denial.
            Terminate(_onCanceled);
        }

        // The terminal-once guard: signedIn/dismissed/error/native-close/watchdog
        // all funnel here; only the first wins. A publisher's startup branch must
        // run exactly once. Mirrors RewardedAd.Terminate().
        private void Terminate(Action fire)
        {
            if (_terminal)
                return;
            _terminal = true;
            Teardown();
            if (fire != null)
                fire();
        }

        private void Teardown()
        {
            if (_watchdog != null)
                _watchdog.Cancel();
            if (_checkDeadline != null)
                _checkDeadline.Cancel();
            if (_webView != null)
            {
                _webView.OnMessage -= HandleRawMessage;
                _webView.OnClosed -= HandleClosed;
                try
                {
                    _webView.Close();
                }
                catch (Exception)
                {
                    // Teardown often runs while cleaning up after a provider that
                    // already threw. Its Close() throwing too must not mask the
                    // failure we are on our way to report.
                }
                _webView = null;
            }
            LevelMomentRuntime.Untrack(this);
        }

        private static void Fire(Action<string> callback, string code)
        {
            if (callback != null)
                callback(code);
        }

        private static string OriginOf(string url)
        {
            Uri parsed;
            if (!Uri.TryCreate(url, UriKind.Absolute, out parsed))
                return string.Empty;
            if (parsed.IsDefaultPort)
                return parsed.Scheme + "://" + parsed.Host;
            return parsed.GetLeftPart(UriPartial.Authority);
        }
    }
}
