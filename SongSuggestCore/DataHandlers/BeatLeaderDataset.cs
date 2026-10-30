using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using BeatLeader;
using BeatLeaderJson;
using Curve;
using FileHandling;
using LinkedData;
using Newtonsoft.Json;
using SongLibraryNS;
using SongSuggestNS;

namespace Data
{
    public sealed class BeatLeaderDatasetManifest
    {
        public int SchemaVersion { get; set; }
        public string PpModelVersion { get; set; }
        public string Context { get; set; }
        public long Revision { get; set; }
        public string Generation { get; set; }
        public Dictionary<string, string> Hashes { get; set; }
    }

    public sealed class BeatLeaderDataset
    {
        public const string Context = "noMods";
        private readonly SongSuggest core;
        public BeatLeaderDatasetManifest Manifest { get; private set; }
        public Dictionary<string, BeatLeaderMapRating> Ratings { get; private set; } = new Dictionary<string, BeatLeaderMapRating>();
        public List<Top10kPlayer> Players { get; private set; } = new List<Top10kPlayer>();
        public List<SongSuggestSong> Songs { get; private set; } = new List<SongSuggestSong>();
        private string Root => core.fileHandler.filePathSettings.leaderboardPath;
        public string Revision => Manifest == null ? null : Manifest.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" +
            Manifest.Hashes["BeatLeaderMapRatings.json"] + ":" + Manifest.Hashes["BeatLeaderLeaderboard.json"];
        public BeatLeaderDataset(SongSuggest core) { this.core = core; }

        public BeatLeaderMapRating GetRating(string id) => id != null && Ratings.TryGetValue(id, out var rating) ? rating : null;

        public bool Load()
        {
            try
            {
                string path = Path.Combine(Root, "BeatLeaderDataset.meta");
                if (!File.Exists(path)) return false;
                var manifest = JsonConvert.DeserializeObject<BeatLeaderDatasetManifest>(File.ReadAllText(path));
                if (manifest == null || manifest.SchemaVersion != 1 || manifest.PpModelVersion != BeatLeaderCurve.ModelVersion ||
                    manifest.Context != Context || manifest.Revision <= 0 || !Guid.TryParseExact(manifest.Generation, "N", out _)) return false;
                string dir = Path.Combine(Root, "BeatLeaderData", manifest.Generation);
                foreach (string name in new[] { "BeatLeaderMapRatings.json", "BeatLeaderLeaderboard.json", "BeatLeaderSongs.json" })
                {
                    string file = Path.Combine(dir, name);
                    if (manifest.Hashes == null || !manifest.Hashes.TryGetValue(name, out var hash) || !File.Exists(file) || Hash(File.ReadAllBytes(file)) != hash) return false;
                }
                var ratings = JsonConvert.DeserializeObject<Dictionary<string, BeatLeaderMapRating>>(File.ReadAllText(Path.Combine(dir, "BeatLeaderMapRatings.json")));
                var players = JsonConvert.DeserializeObject<List<Top10kPlayer>>(File.ReadAllText(Path.Combine(dir, "BeatLeaderLeaderboard.json")));
                var songs = JsonConvert.DeserializeObject<List<SongSuggestSong>>(File.ReadAllText(Path.Combine(dir, "BeatLeaderSongs.json")));
                Validate(songs, players, ratings);
                Manifest = manifest; Ratings = ratings; Players = players; Songs = songs;
                return true;
            }
            catch (Exception ex) { core.log?.WriteLine("PPv4 dataset not usable: " + ex.Message); return false; }
        }

        public void Refresh(bool force = false)
        {
            long revision = core.webDownloader.GetBeatLeaderLeaderboardUpdateTime();
            if (revision <= 0) throw new IOException("BeatLeader snapshot revision unavailable.");
            if (!force && Load() && Manifest.Revision == revision) return;
            var songs = core.webDownloader.GetBeatLeaderRankedSongs(0).Where(s => s.mode == "Standard" || s.mode == "OneSaber").ToList();
            var players = core.webDownloader.GetBeatLeaderLeaderboard(Context);
            if (songs.Count == 0 || players.Count == 0) throw new IOException("Empty BeatLeader snapshot; existing dataset preserved.");
            var ratings = new Dictionary<string, BeatLeaderMapRating>();
            string pendingPath = Path.Combine(Root, "BeatLeaderPendingRatings.json");
            if (!force && File.Exists(pendingPath))
            {
                try
                {
                    var pending = JsonConvert.DeserializeObject<PendingRatings>(File.ReadAllText(pendingPath));
                    if (pending?.Revision == revision && pending.Model == BeatLeaderCurve.ModelVersion && pending.Ratings != null)
                        ratings = pending.Ratings;
                }
                catch (JsonException) { }
            }
            int count = 0;
            foreach (var song in songs)
            {
                if (!ratings.TryGetValue(song.id, out var rating) || !Matches(song, rating))
                {
                    rating = core.webDownloader.GetBeatLeaderMapRating(song.id);
                    if (!Matches(song, rating)) throw new IOException("Cannot use BeatLeader map " + song.id + ": " + DescribeMismatch(song, rating));
                    ratings[song.id] = rating;
                }
                count++;
                if (count % 20 == 0 || count == songs.Count)
                {
                    AtomicWrite(pendingPath, JsonConvert.SerializeObject(new PendingRatings { Revision = revision, Model = BeatLeaderCurve.ModelVersion, Ratings = ratings }));
                    core.status = $"PPv4 map metadata: {count}/{songs.Count}";
                    core.log?.WriteLine(core.status);
                }
            }
            if (core.webDownloader.GetBeatLeaderLeaderboardUpdateTime() != revision) throw new IOException("BeatLeader snapshot changed during refresh. Retry.");
            Validate(songs, players, ratings);
            // Verify the deployed calculation before marking this snapshot as compatible with our model.
            foreach (var rating in ratings.Values.OrderBy(r => r.Stars).Where((r, i) => i % Math.Max(1, ratings.Count / 5) == 0).Take(5))
            {
                double expected = core.webDownloader.GetBeatLeaderPp(rating.Id, 0.97);
                double actual = BeatLeaderCurve.PP(0.97, rating);
                if (Math.Abs(actual - expected) > Math.Max(0.01, expected * 0.0001))
                    throw new IOException("Official PPv4 calculation mismatch for " + rating.Id);
            }
            Commit(songs, players, ratings, revision);
        }

        public void Commit(List<SongSuggestSong> songs, List<Top10kPlayer> players, Dictionary<string, BeatLeaderMapRating> ratings, long revision)
        {
            if (revision <= 0) throw new ArgumentOutOfRangeException(nameof(revision));
            Validate(songs, players, ratings);
            string generation = Guid.NewGuid().ToString("N"), dir = Path.Combine(Root, "BeatLeaderData", generation);
            Directory.CreateDirectory(dir);
            var files = new Dictionary<string, string> {
                { "BeatLeaderMapRatings.json", JsonConvert.SerializeObject(ratings) },
                { "BeatLeaderLeaderboard.json", JsonConvert.SerializeObject(players) },
                { "BeatLeaderSongs.json", JsonConvert.SerializeObject(songs) }
            };
            var manifest = new BeatLeaderDatasetManifest { SchemaVersion = 1, PpModelVersion = BeatLeaderCurve.ModelVersion,
                Context = Context, Revision = revision, Generation = generation, Hashes = new Dictionary<string, string>() };
            foreach (var file in files)
            {
                AtomicWrite(Path.Combine(dir, file.Key), file.Value);
                manifest.Hashes[file.Key] = Hash(File.ReadAllBytes(Path.Combine(dir, file.Key)));
            }
            // Immutable generations keep the old complete set usable until this single pointer changes.
            AtomicWrite(Path.Combine(Root, "BeatLeaderDataset.meta"), JsonConvert.SerializeObject(manifest));
            Manifest = manifest; Songs = songs; Players = players; Ratings = ratings;
        }

        private static void Validate(List<SongSuggestSong> songs, List<Top10kPlayer> players, Dictionary<string, BeatLeaderMapRating> ratings)
        {
            if (songs == null || players == null || ratings == null || songs.Count == 0 || players.Count == 0 ||
                songs.Select(s => s?.id).Distinct().Count() != songs.Count || players.Any(p => p?.top10kScore == null ||
                    string.IsNullOrEmpty(p.id) || p.top10kScore.Count == 0 || p.top10kScore.Select(s => s?.songID).Distinct().Count() != p.top10kScore.Count))
                throw new InvalidDataException("Invalid BeatLeader dataset.");
            foreach (var song in songs)
                if (song == null || song.id == null || !ratings.TryGetValue(song.id, out var r) || !Matches(song, r))
                    throw new InvalidDataException("Incomplete PPv4 ratings coverage for " + song?.id);
            var ids = new HashSet<string>(songs.Select(s => s.id));
            foreach (var score in players.SelectMany(p => p.top10kScore))
                if (score == null || score.songID == null || !BeatLeaderCurve.IsFinite(score.pp) || score.pp <= 0 || score.rank < 1)
                    throw new InvalidDataException("Invalid population score.");
            // The API also contains modes that this client does not support. Keep only known supported maps.
            if (!players.Any(p => p.top10kScore.Count(s => ids.Contains(s.songID)) >= 20)) throw new InvalidDataException("No supported population profiles with at least 20 scores.");
        }

        private static bool Matches(SongSuggestSong s, BeatLeaderMapRating r) => r != null && r.IsComplete && r.Id == s.id &&
            string.Equals(r.Hash, s.hash, StringComparison.OrdinalIgnoreCase) && r.Mode == s.mode &&
            Song.GetDifficultyValue(r.Difficulty) == Song.GetDifficultyValue(s.difficulty) &&
            Math.Abs(r.Stars.Value - s.stars) < 0.0005 && Math.Abs(r.AccRating.Value - s.accRating) < 0.0005 &&
            Math.Abs(r.PassRating.Value - s.passRating) < 0.0005 && Math.Abs(r.TechRating.Value - s.techRating) < 0.0005;

        private static string DescribeMismatch(SongSuggestSong s, BeatLeaderMapRating r)
        {
            if (r == null) return "Live metadata is missing.";
            if (!r.IsComplete) return "Invalid live PPv4 metadata: " + r.ValidationError;
            return $"Snapshot/live mismatch. ID={s.id}/{r.Id}, hash={s.hash}/{r.Hash}, mode={s.mode}/{r.Mode}, difficulty={s.difficulty}/{r.Difficulty}, " +
                System.FormattableString.Invariant($"stars={s.stars:G9}/{r.Stars:G9}, accRating={s.accRating:G9}/{r.AccRating:G9}, ") +
                System.FormattableString.Invariant($"passRating={s.passRating:G9}/{r.PassRating:G9}, techRating={s.techRating:G9}/{r.TechRating:G9}. ") +
                "Retry after the server snapshot refreshes.";
        }

        public static void AtomicWrite(string path, string content)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                { writer.Write(content); writer.Flush(); stream.Flush(true); }
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        private static string Hash(byte[] bytes) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", ""); }
        private sealed class PendingRatings
        {
            public long Revision { get; set; }
            public string Model { get; set; }
            public Dictionary<string, BeatLeaderMapRating> Ratings { get; set; }
        }
    }
}
