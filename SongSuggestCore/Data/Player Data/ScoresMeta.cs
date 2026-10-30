using System;

namespace PlayerScores
{
    public class ScoresMeta
    {
        public string FormatVersion { get; set; }
        public string DataVersion { get; set; }
        public string PpModelVersion { get; set; }
        public string LeaderboardContext { get; set; }
        public long CompletedWatermark { get; set; }
        public DateTime? LastSuccessfulFullRefresh { get; set; }
    }
}
