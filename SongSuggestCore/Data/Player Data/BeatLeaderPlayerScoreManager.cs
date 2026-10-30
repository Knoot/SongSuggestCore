using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ActivePlayerData;
using Actions;
using BeatLeader;
using Curve;
using SongLibraryNS;
using SongSuggestNS;

namespace PlayerScores
{
    public class BeatLeaderPlayerScoreManager : IPlayerScores, IBeatLeaderScoreSource
    {
        public ActivePlayer ActivePlayer { get; set; }
        public SongSuggest songSuggest => ActivePlayer.songSuggest;
        private ScoreCollection collection = new ScoreCollection();
        public bool Updated { get; set; }
        private Dictionary<string, List<PlayerScore>> lookup;
        public IEnumerable<PlayerScore> GetBeatLeaderScores(SongID id)
        {
            if (lookup == null) lookup = collection.PlayerScores.GroupBy(s => s.SongID).ToDictionary(g => g.Key, g => g.ToList());
            string key = id?.GetSong()?.internalID;
            return key != null && lookup.TryGetValue(key, out var records) ? records : Enumerable.Empty<PlayerScore>();
        }
        public bool BeatLeaderScoresCurrent => songSuggest.BeatLeaderData.Manifest != null &&
            collection.ScoresMeta?.FormatVersion == "1.0" && collection.ScoresMeta.PpModelVersion == BeatLeaderCurve.ModelVersion && collection.ScoresMeta.LeaderboardContext == Data.BeatLeaderDataset.Context &&
            collection.ScoresMeta.DataVersion == songSuggest.BeatLeaderData.Revision && collection.ScoresMeta.LastSuccessfulFullRefresh.HasValue;

        public void Load() { collection = songSuggest.fileHandler.LoadScoreCollection("BL" + ActivePlayer.PlayerID) ?? new ScoreCollection(); lookup = null; }
        public void Save() { if (BeatLeaderScoresCurrent) songSuggest.fileHandler.SaveScoreCollection(collection, "BL" + ActivePlayer.PlayerID); }
        public void Clear()
        {
            collection = new ScoreCollection();
            lookup = null;
            songSuggest.fileHandler.SaveScoreCollection(collection, "BL" + ActivePlayer.PlayerID);
            ActivePlayer.CachedRankings.Clear();
        }
        public void ClearIfOutdated() { /* Invalid data remains on disk, but never participates in BL evaluation. */ }

        public void Refresh()
        {
            if (songSuggest.BeatLeaderData.Manifest == null) return;
            try
            {
                var staged = BeatLeaderScoreSync.Download(collection, BeatLeaderScoresCurrent, songSuggest.BeatLeaderData.Revision,
                    (page, from) => songSuggest.webDownloader.GetBeatLeaderScoresCompact(ActivePlayer.PlayerID, 100, page, from),
                    id => songSuggest.BeatLeaderData.GetRating(id) == null ? null : ((BeatLeaderID)id).GetSong(),
                    (song, accuracy) => songSuggest.CalculateBeatLeaderPp(accuracy, song));
                songSuggest.fileHandler.SaveScoreCollection(staged, "BL" + ActivePlayer.PlayerID);
                collection = staged;
                lookup = null;
                Updated = false;
                ActivePlayer.CachedRankings.Clear();
            }
            catch (Exception ex) { songSuggest.log?.WriteLine("BeatLeader sync failed; previous complete cache retained: " + ex.Message); }
        }

        public List<SongID> GetRankedScoreIDs(SongCategory category) => collection.PlayerScores
            .Select(s => (SongID)(InternalID)s.SongID).Where(id => id?.GetSong() != null && SongLibrary.HasAnySongCategory(id, category)).Distinct().ToList();
        public List<SongID> GetScoreIDs() => collection.PlayerScores.Select(s => (SongID)(InternalID)s.SongID).Distinct().ToList();
        private PlayerScore Find(SongID id) => GetBeatLeaderScores(id).Where(s => BeatLeaderModifiers.IsNoMods(s.Modifiers))
            .OrderByDescending(s => s.Accuracy).FirstOrDefault();
        public double GetAccuracy(SongID id) => Find(id)?.Accuracy ?? 0;
        public DateTime GetTimeSet(SongID id) => Find(id)?.TimeSet ?? DateTime.MinValue;
        public PlayerScore GetScore(SongID id) => Find(id);
        public bool Contains(SongID id) => collection.PlayerScores.Any(s => s.SongID == id?.GetSong()?.internalID);
        public double GetRatedScore(SongID id, LeaderboardType leaderboard)
        {
            var score = Find(id); var song = id?.GetSong();
            if (score == null || song == null) return 0;
            switch (leaderboard)
            {
                case LeaderboardType.BeatLeader: return ActivePlayer.BeatLeaderValue(id, this).Value;
                case LeaderboardType.ScoreSaber: return ScoreSaberCurve.PP(score.Accuracy, song.starScoreSaber);
                case LeaderboardType.AccSaber: return AccSaberCurve.AP(score.Accuracy, song.complexityAccSaber);
                case LeaderboardType.AutoBalancer: return AutoBalancerCurve.PP(score.Accuracy, song.complexityAutoBalancer);
                default: return 0;
            }
        }
        public void ShowCache(TextWriter log) { log?.WriteLine($"BeatLeader PPv4 scores: {collection.PlayerScores.Count}, valid={BeatLeaderScoresCurrent}"); }
    }

    public static class BeatLeaderScoreSync
    {
        public static ScoreCollection Download(ScoreCollection previous, bool current, string revision,
            Func<int, long, BeatLeaderJson.ScoresCompact> download, Func<string, Song> resolve, Func<Song, double, double> expectedPp = null)
        {
            var records = current ? previous.PlayerScores.ToDictionary(s => s.SongID, s => s) : new Dictionary<string, PlayerScore>();
            long watermark = current ? previous.ScoresMeta.CompletedWatermark : 0;
            long from = Math.Max(0, watermark - 2), latest = watermark;
            long previousTime = from;
            var seen = new HashSet<string>();
            int total = -1, received = 0;
            for (int page = 1; ; page++)
            {
                var response = download(page, from);
                if (response?.metadata == null || response.data == null || response.metadata.page != page || response.metadata.itemsPerPage <= 0 || response.metadata.total < 0)
                    throw new InvalidDataException("Incomplete BeatLeader scores page " + page);
                if (total < 0) total = response.metadata.total;
                if (total != response.metadata.total) throw new InvalidDataException("Scores changed during pagination. Retry.");
                foreach (var item in response.data)
                {
                    if (item?.score == null || item.leaderboard?.id == null || !BeatLeaderCurve.IsFinite(item.score.pp) || item.score.pp < 0 ||
                        !BeatLeaderCurve.IsFinite(item.score.accuracy) || item.score.accuracy < 0 || item.score.accuracy > 1)
                        throw new InvalidDataException("Invalid BeatLeader score.");
                    if (item.score.epochTime < previousTime || !seen.Add(item.leaderboard.id))
                        throw new InvalidDataException("Unstable BeatLeader score pagination. Retry.");
                    previousTime = item.score.epochTime;
                    latest = Math.Max(latest, item.score.epochTime);
                    var song = resolve(item.leaderboard.id);
                    if (song == null) continue;
                    if (expectedPp != null && BeatLeaderModifiers.IsNoMods(item.score.modifiers))
                    {
                        double expected = expectedPp(song, item.score.accuracy);
                        if (!BeatLeaderCurve.IsFinite(expected) || Math.Abs(expected - item.score.pp) > Math.Max(0.01, expected * 0.0001))
                            throw new InvalidDataException("Server score PP disagrees with the installed PPv4 snapshot for " + item.leaderboard.id);
                    }
                    records[song.internalID] = new PlayerScore { SongID = song.internalID, SongName = song.name, RatedScore = item.score.pp,
                        Accuracy = item.score.accuracy, TimeSet = DateTimeOffset.FromUnixTimeSeconds(item.score.epochTime).UtcDateTime,
                        Modifiers = item.score.modifiers, ModifiersVerified = true };
                }
                received += response.data.Count;
                if (received == total) break;
                if (received > total || response.data.Count == 0) throw new InvalidDataException("Missing BeatLeader score records.");
            }
            return new ScoreCollection { PlayerScores = records.Values.ToList(), ScoresMeta = new ScoresMeta { FormatVersion = "1.0",
                DataVersion = revision, PpModelVersion = BeatLeaderCurve.ModelVersion, LeaderboardContext = Data.BeatLeaderDataset.Context,
                CompletedWatermark = latest, LastSuccessfulFullRefresh = current ? previous.ScoresMeta.LastSuccessfulFullRefresh : DateTime.UtcNow } };
        }
    }
}
