// ---------------------------------------------------------------------------
// LevelMoment Unity SDK — MAX-shaped compatibility types.
//
// These are NOT AppLovin types. Nothing in this file, or anywhere under
// Compat/, references the AppLovin MAX plugin — the package compiles and
// runs in a project that has never installed MAX. `AdInfo`, `ErrorInfo`, and
// `Reward` below are shape-compatible stand-ins: plain classes/structs whose
// MEMBER NAMES match the members MAX's `MaxSdkBase.AdInfo` /
// `MaxSdkBase.ErrorInfo` / `MaxSdkBase.Reward` carry, so a callback body
// copied from a MAX integration that reads one of the members listed below
// keeps compiling unchanged. A callback body that reads a MAX member NOT
// listed here (e.g. `adInfo.Revenue`, `adInfo.WaterfallInfo`,
// `errorInfo.MediatedNetworkErrorCode`, `errorInfo.Code` as an enum) will
// NOT compile against these types — see the per-type notes below for why
// each omitted member has no faithful Level Moment equivalent, rather than
// fabricating a value for it.
//
// See LevelMomentMaxSdk.cs for the facade that constructs these.
// ---------------------------------------------------------------------------

namespace LevelMoment.Compat.Max
{
    /// <summary>
    /// Shape-compatible stand-in for MAX's <c>MaxSdkBase.AdInfo</c>.
    ///
    /// Provides: <see cref="AdUnitIdentifier"/>, <see cref="NetworkName"/>.
    ///
    /// Omitted vs. MAX's real <c>AdInfo</c> — <c>AdFormat</c>,
    /// <c>NetworkPlacement</c>, <c>Placement</c>, <c>CreativeIdentifier</c>,
    /// <c>Revenue</c>, <c>RevenuePrecision</c>, <c>WaterfallInfo</c>,
    /// <c>LatencyMillis</c>, <c>DspName</c> — all describe a mediation
    /// waterfall (which network filled the impression, at what price, after
    /// how many milliseconds). Level Moment is not a mediator: every
    /// placement is served by Level Moment itself, so there is no waterfall,
    /// no bid price, and no per-network identity to report. Faking these
    /// would be worse than omitting them.
    /// </summary>
    public sealed class AdInfo
    {
        /// <summary>The MAX-shaped ad-unit id this break was loaded/shown for.</summary>
        public string AdUnitIdentifier { get; }

        /// <summary>Always <c>"LevelMoment"</c> — there is no mediation network to name.</summary>
        public string NetworkName { get; }

        public AdInfo(string adUnitIdentifier)
        {
            AdUnitIdentifier = adUnitIdentifier;
            NetworkName = "LevelMoment";
        }
    }

    /// <summary>
    /// Shape-compatible stand-in for MAX's <c>MaxSdkBase.ErrorInfo</c>.
    ///
    /// Provides: <see cref="Code"/>, <see cref="Message"/>.
    ///
    /// Divergence: MAX's <c>Code</c> is a <c>MaxSdkBase.ErrorCode</c> enum
    /// (e.g. <c>ErrorCode.NoFill</c>); ours is the underlying
    /// <see cref="LevelMomentAdError.Code"/> string (e.g. <c>"not_loaded"</c>,
    /// <c>"no_webview_provider"</c>, <c>"unmapped_ad_unit"</c>) — a callback
    /// body that does `errorInfo.Code == MaxSdkBase.ErrorCode.NoFill` will not
    /// compile; one that does `errorInfo.Code == "no_fill"` or just logs/reads
    /// the string does.
    ///
    /// Omitted vs. MAX's real <c>ErrorInfo</c> — <c>MediatedNetworkErrorCode</c>,
    /// <c>MediatedNetworkErrorMessage</c>, <c>AdLoadFailureInfo</c>,
    /// <c>WaterfallInfo</c>, <c>LatencyMillis</c> — same reason as
    /// <see cref="AdInfo"/>: there is no mediation waterfall underneath a
    /// Level Moment placement for these to describe.
    /// </summary>
    public sealed class ErrorInfo
    {
        /// <summary>Short machine-readable code. Mirrors <see cref="LevelMomentAdError.Code"/> verbatim.</summary>
        public string Code { get; }

        /// <summary>Human-readable description. Mirrors <see cref="LevelMomentAdError.Message"/> verbatim.</summary>
        public string Message { get; }

        public ErrorInfo(string code, string message)
        {
            Code = code;
            Message = message;
        }

        public ErrorInfo(LevelMomentAdError error)
            : this(error != null ? error.Code : "unknown", error != null ? error.Message : string.Empty)
        {
        }
    }

    /// <summary>
    /// Shape-compatible stand-in for MAX's <c>MaxSdkBase.Reward</c> struct.
    ///
    /// Provides: <see cref="Label"/>, <see cref="Amount"/> — both real data,
    /// not fabricated: <see cref="Amount"/> is the underlying reward amount
    /// (1 for a correct answer, 0 otherwise — see
    /// <see cref="RewardedAdShowCallbacks.OnUserEarnedReward"/>);
    /// <see cref="Label"/> is the opaque impression id
    /// (<see cref="LevelMomentRewardItem.RewardId"/>) that correlates with the
    /// verified webhook, in the same string slot MAX uses for its
    /// dashboard-configured reward currency name. There is no currency name
    /// to report here — Level Moment rewards are pass/fail per answer, not a
    /// configurable currency — so <c>Label</c> carries the id instead of an
    /// empty string.
    /// </summary>
    public struct Reward
    {
        public string Label;
        public int Amount;
    }
}
