using System;

namespace Settings
{
    public class FilterSettings
    {
        [Obsolete("This modifier has been disabled effective, the variable will be removed in a future version.")]
        public double modifierPP { get; set; } = 0.0;
        public double modifierStyle { get; set; } = 100.0;
        public double modifierOverweight { get; set; } = 20.0;

        public double minNjs { get; set; } = 0.0;
        public double maxNjs { get; set; } = 0.0;
        public double minNps { get; set; } = 0.0;
        public double maxNps { get; set; } = 0.0;
        public double minSeconds { get; set; } = 0.0;
        public double maxSeconds { get; set; } = 0.0;
        public double minScoreSaberStars { get; set; } = 0.0;
        public double maxScoreSaberStars { get; set; } = 0.0;
        public double minBeatLeaderStars { get; set; } = 0.0;
        public double maxBeatLeaderStars { get; set; } = 0.0;
    }
}
