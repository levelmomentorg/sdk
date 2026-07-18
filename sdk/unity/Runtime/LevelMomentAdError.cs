// ---------------------------------------------------------------------------
// LevelMoment Unity SDK — error surface.
//
// Mirrors AdMob's LoadAdError / AdError (google_mobile_ads Unity plugin): a
// simple { code, message } pair passed to OnAdFailedToLoad / OnAdFailedToShow.
// ---------------------------------------------------------------------------

namespace LevelMoment
{
    /// <summary>
    /// A load/show failure. Mirrors AdMob's <c>LoadAdError</c> / <c>AdError</c>.
    /// </summary>
    public class LevelMomentAdError
    {
        /// <summary>Short machine-readable code (e.g. <c>no_webview_provider</c>).</summary>
        public string Code { get; private set; }

        /// <summary>Human-readable description of the failure.</summary>
        public string Message { get; private set; }

        public LevelMomentAdError(string code, string message)
        {
            Code = code;
            Message = message;
        }

        public override string ToString()
        {
            return "LevelMomentAdError(" + Code + ": " + Message + ")";
        }
    }
}
