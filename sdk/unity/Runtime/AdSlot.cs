// ---------------------------------------------------------------------------
// LevelMoment Unity SDK — the slot a game declares for an ad placement.
//
// A game declares two things about each slot when it integrates: the target
// duration of the slot, and the reward amount it grants the player for it.
// Level Moment chooses what fills the slot; the game keeps its pacing and its
// reward economy.
//
// The duration is a target. How long a break actually runs varies with the
// learner — what the slot fixes is how much content goes into it. The reward
// amount is whatever the ad unit already granted, so a ported game pays the
// player what it always paid.
//
// See docs/decisions/break-formats-and-payment-2026-09-11.md.
// ---------------------------------------------------------------------------

namespace LevelMoment
{
    /// <summary>
    /// The ad type a slot replaces, and the two values the game declares for
    /// it. Build one per ad placement and pass it to Load (or, for the MAX
    /// facade, to <c>LevelMomentMaxSdk.MapAdUnit</c>).
    /// </summary>
    public sealed class LevelMomentAdSlot
    {
        /// <summary>Shortest slot a game may declare, in seconds.</summary>
        public const int MinDurationSeconds = 5;

        /// <summary>Longest slot a game may declare, in seconds.</summary>
        public const int MaxDurationSeconds = 300;

        /// <summary>Largest reward amount a game may declare for one slot.</summary>
        public const int MaxRewardAmount = 1000000;

        /// <summary><c>"rewarded"</c> or <c>"interstitial"</c>.</summary>
        public string AdType { get; private set; }

        /// <summary>How long this slot should run, in seconds.</summary>
        public int TargetDurationSeconds { get; private set; }

        /// <summary>
        /// What the game grants the player when a graded break passes. 0 when
        /// the slot grants nothing, which is every interstitial slot.
        /// </summary>
        public int RewardAmount { get; private set; }

        /// <summary>
        /// Declare a slot. <paramref name="targetDurationSeconds"/> is clamped
        /// to <see cref="MinDurationSeconds"/>..<see cref="MaxDurationSeconds"/>
        /// and <paramref name="rewardAmount"/> to 0..<see cref="MaxRewardAmount"/>:
        /// a slot outside the range still serves a break, which is a better
        /// outcome for the player than refusing one over a typo.
        /// </summary>
        public LevelMomentAdSlot(string adType, int targetDurationSeconds, int rewardAmount = 0)
        {
            AdType = adType == "interstitial" ? "interstitial" : "rewarded";
            TargetDurationSeconds = Clamp(targetDurationSeconds, MinDurationSeconds, MaxDurationSeconds);
            RewardAmount = Clamp(rewardAmount, 0, MaxRewardAmount);
        }

        /// <summary>
        /// The ad type a break format replaces: graded formats
        /// (<c>quick_question</c>, <c>practice_set</c>) fill a rewarded slot,
        /// the rest fill an interstitial one.
        /// </summary>
        public static string AdTypeForFormat(string format)
        {
            // "deep_dive" is the pre-2026-09-11 name for a mastery round; the
            // porting CLI still writes it, so map it here too.
            return format == "mastery_round" || format == "intro_lesson" || format == "deep_dive"
                ? "interstitial"
                : "rewarded";
        }

        /// <summary>
        /// The slot a game gets when it declares none: the format's own
        /// default duration, and no reward amount. Keeps an un-migrated call
        /// site serving the same break it served before.
        /// </summary>
        public static LevelMomentAdSlot DefaultFor(string format)
        {
            return new LevelMomentAdSlot(AdTypeForFormat(format), DefaultDurationSeconds(format));
        }

        /// <summary>
        /// The duration assumed for a format when the game declares none.
        /// These match the sizes those formats served before slots existed.
        /// </summary>
        public static int DefaultDurationSeconds(string format)
        {
            if (format == "mastery_round" || format == "intro_lesson" || format == "deep_dive")
                return 60;
            if (format == "practice_set" || format == "quiz")
                return 30;
            return 15;
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
