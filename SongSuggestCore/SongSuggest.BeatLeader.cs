using System;
using System.Linq;
using System.IO;
using BeatLeader;
using Curve;
using Data;
using LinkedData;
using Newtonsoft.Json;

namespace SongSuggestNS
{
    public partial class SongSuggest
    {
        public BeatLeaderDataset BeatLeaderData { get; private set; }
        private readonly object operationLock = new object();
        public string LastOperationError { get; private set; }

        public void RefreshBeatLeaderData(bool force = false)
        {
            lock (operationLock)
            {
                BeatLeaderData.Refresh(force);
                ApplyBeatLeaderDataset();
            }
        }

        private void ApplyBeatLeaderDataset()
        {
            if (BeatLeaderData.Manifest == null)
            {
                beatLeaderScoreBoard = new Top10kPlayers { songSuggest = this, FormatName = "BeatLeader PPv4" };
                return;
            }
            var songs = songLibrary.UIDStringToSong.Values.Distinct().ToList();
            foreach (var song in songs.Where(s => (s.songCategory & SongCategory.BeatLeader) != 0))
            {
                song.songCategory &= ~SongCategory.BeatLeader;
                song.starBeatLeader = song.starAccBeatLeader = song.starPassBeatLeader = song.starTechBeatLeader = 0;
                song.beatLeaderID = null;
            }
            songLibrary.ResetLibraryLinks();
            foreach (var song in BeatLeaderData.Songs) songLibrary.UpsertSong(song);
            songLibrary.Save();
            var players = JsonConvert.DeserializeObject<System.Collections.Generic.List<Top10kPlayer>>(JsonConvert.SerializeObject(BeatLeaderData.Players));
            var known = new System.Collections.Generic.HashSet<string>(BeatLeaderData.Songs.Select(s => s.id));
            foreach (var p in players) p.top10kScore = p.top10kScore.Where(s => known.Contains(s.songID)).ToList();
            players = players.Where(p => p.top10kScore.Count >= 20).ToList();
            if (players.Count == 0) throw new InvalidDataException("PPv4 population has no supported profiles.");
            CreateComparativeBestLeaderboardEvenMapDistribution(players, "BeatLeaderLeaderboard");
            beatLeaderScoreBoard = new Top10kPlayers { songSuggest = this, FormatName = "BeatLeader PPv4" };
            beatLeaderScoreBoard.Load("BeatLeaderLeaderboard");
            if (CoreSettings.FilterScoreSaberBiasInBeatLeader) RemoveScoreSaberOnlyScoresFromBeatLeaderLeaderBoard();
            filesMeta.beatLeaderSongsUpdated = filesMeta.beatLeaderLeaderboardUpdated = BeatLeaderData.Manifest.Revision;
            fileHandler.SaveFilesMeta(filesMeta);
            activePlayer?.CachedRankings.Clear();
            log?.WriteLine($"BL {BeatLeaderCurve.ModelVersion}, context=noMods, revision={BeatLeaderData.Revision}, maps={BeatLeaderData.Ratings.Count}");
        }

        public double CalculateBeatLeaderPp(double accuracy, SongLibraryNS.Song song)
        {
            var rating = BeatLeaderData.GetRating(song?.beatLeaderID);
            if (rating == null) throw new InvalidOperationException("Missing PPv4 metadata for " + song?.name + ". Refresh BeatLeader data.");
            return BeatLeaderCurve.PP(accuracy, rating);
        }
    }
}
