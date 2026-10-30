using System;
using System.Globalization;
using System.Linq;
using Curve;
using Newtonsoft.Json;

namespace BeatLeader
{
    public interface IBeatLeaderScoreSource
    {
        System.Collections.Generic.IEnumerable<PlayerScores.PlayerScore> GetBeatLeaderScores(SongLibraryNS.SongID id);
        bool BeatLeaderScoresCurrent { get; }
    }
    public sealed class BeatLeaderMapRating
    {
        public string Id { get; set; }
        public string Hash { get; set; }
        public string Mode { get; set; }
        public string Difficulty { get; set; }
        public int Status { get; set; }
        public double? Stars { get; set; }
        public double? AccRating { get; set; }
        public double? PassRating { get; set; }
        public double? TechRating { get; set; }
        public double? PredictedAcc { get; set; }
        public double? SliderPercentage { get; set; }
        public int? Notes { get; set; }
        public double? NoteSpan { get; set; }
        [JsonIgnore] public bool IsComplete => !string.IsNullOrEmpty(Id) && Hash != null && Hash.Length == 40 && Hash.All(Uri.IsHexDigit) &&
            (Mode == "Standard" || Mode == "OneSaber") && !string.IsNullOrEmpty(Difficulty) && Status == 3 &&
            new[] { Stars, AccRating, PassRating, TechRating, PredictedAcc, NoteSpan }
                .All(n => n.HasValue && BeatLeaderCurve.IsFinite(n.Value) && n.Value >= 0) && Notes.HasValue && Notes >= 0 &&
            Stars > 0 && PredictedAcc <= 1 && SliderPercentage.HasValue && BeatLeaderCurve.IsFinite(SliderPercentage.Value) &&
            // The API can return tiny signed residuals for zero sliders. Preserve them for official formula parity.
            SliderPercentage >= -0.000001;
        [JsonIgnore] public string ValidationError
        {
            get
            {
                if (IsComplete) return null;
                var names = new[] { "Stars", "AccRating", "PassRating", "TechRating", "PredictedAcc", "NoteSpan", "SliderPercentage", "Notes" };
                var values = new[] { Stars, AccRating, PassRating, TechRating, PredictedAcc, NoteSpan, SliderPercentage, (double?)Notes };
                for (int i = 0; i < names.Length; i++)
                {
                    double minimum = names[i] == "SliderPercentage" ? -0.000001 : 0;
                    if (!values[i].HasValue || !BeatLeaderCurve.IsFinite(values[i].Value) || values[i].Value < minimum)
                        return names[i] + "=" + (values[i]?.ToString("R", CultureInfo.InvariantCulture) ?? "missing");
                }
                return "Invalid identity, ranked status, Stars or PredictedAcc range: id=" + Id + ", hash=" + Hash +
                    ", mode=" + Mode + ", difficulty=" + Difficulty + ", status=" + Status + ", stars=" + Stars + ", predictedAcc=" + PredictedAcc;
            }
        }
        [JsonIgnore] public string CurveKey => BeatLeaderCurve.ModelVersion + "|" + string.Join("|", new[] {
            PredictedAcc, SliderPercentage, (double?)Notes, NoteSpan }.Select(n => n?.ToString("R", CultureInfo.InvariantCulture) ?? "missing"));
    }

    public enum BeatLeaderScoreStatus { Available, NotPlayed, Ineligible, MissingMetadata }
    public sealed class BeatLeaderScoreValue
    {
        public BeatLeaderScoreStatus Status { get; }
        public double Value { get; }
        public BeatLeaderScoreValue(BeatLeaderScoreStatus status, double value = 0) { Status = status; Value = value; }
    }

    public static class BeatLeaderModifiers
    {
        public static bool IsNoMods(string modifiers) => modifiers != null &&
            modifiers.Split(',').All(m => m.Trim().Length == 0 || m.Trim().Equals("IF", StringComparison.OrdinalIgnoreCase) ||
                m.Trim().Equals("BE", StringComparison.OrdinalIgnoreCase));
    }
}
