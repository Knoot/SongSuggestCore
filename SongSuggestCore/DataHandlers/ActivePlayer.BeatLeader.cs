using System;
using System.Collections.Generic;
using System.Linq;
using BeatLeader;
using Curve;
using PlayerScores;
using SongLibraryNS;
using SongSuggestNS;
using Actions;

namespace ActivePlayerData
{
    public partial class ActivePlayer
    {
        public BeatLeaderScoreValue BeatLeaderValue(SongID id, IPlayerScores source)
        {
            if (!(source is IBeatLeaderScoreSource bl) || id?.GetSong() == null) return new BeatLeaderScoreValue(BeatLeaderScoreStatus.NotPlayed);
            var song = id.GetSong();
            var records = bl.GetBeatLeaderScores(id).ToList();
            if (records.Count == 0) return new BeatLeaderScoreValue(BeatLeaderScoreStatus.NotPlayed);
            var record = records.Where(s => s.ModifiersVerified != false && BeatLeaderModifiers.IsNoMods(s.Modifiers) &&
                BeatLeaderCurve.IsFinite(s.Accuracy) && s.Accuracy > 0 && s.Accuracy <= 1).OrderByDescending(s => s.Accuracy).FirstOrDefault();
            if (record == null) return new BeatLeaderScoreValue(BeatLeaderScoreStatus.Ineligible);
            if (!bl.BeatLeaderScoresCurrent) return new BeatLeaderScoreValue(BeatLeaderScoreStatus.MissingMetadata);
            var rating = songSuggest.BeatLeaderData.GetRating(song.beatLeaderID);
            if (rating == null || !rating.IsComplete) return new BeatLeaderScoreValue(BeatLeaderScoreStatus.MissingMetadata);
            return new BeatLeaderScoreValue(BeatLeaderScoreStatus.Available, source is BeatLeaderPlayerScoreManager ? record.RatedScore : BeatLeaderCurve.PP(record.Accuracy, rating));
        }

        public List<SongID> BeatLeaderPlayedIDs() => ActiveScoreLocations
            .SelectMany(l => scores[l].GetRankedScoreIDs(SongCategory.BeatLeader)).Distinct().ToList();

        public List<SongID> BeatLeaderRankedIDs() => BeatLeaderPlayedIDs().Where(id => ActiveScoreLocations
            .Any(l => BeatLeaderValue(id, scores[l]).Status == BeatLeaderScoreStatus.Available)).ToList();

        public void ValidateBeatLeaderData()
        {
            if (songSuggest.BeatLeaderData.Manifest == null) throw new InvalidOperationException("BeatLeader PPv4 dataset is missing. Run InitialDataTool --beatleader.");
            if (ActiveScoreLocations.Contains(ScoreLocation.BeatLeader) &&
                !((IBeatLeaderScoreSource)scores[ScoreLocation.BeatLeader]).BeatLeaderScoresCurrent)
                throw new InvalidOperationException("BeatLeader PPv4 score cache needs a successful full refresh.");
            foreach (var id in BeatLeaderPlayedIDs()) GetRatedScore(id, Actions.LeaderboardType.BeatLeader);
        }
    }
}
