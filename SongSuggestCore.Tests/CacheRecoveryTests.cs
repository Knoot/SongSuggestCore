using Data;
using FileHandling;
using LinkedData;
using Settings;
using SongSuggestNS;

internal static class CacheRecoveryTests
{
    public static void Run(Action<bool, string> check, Action<Action, string> throws)
    {
        string root = Path.Combine(Path.GetTempPath(), "SongSuggestCore.RecoveryTests", Guid.NewGuid().ToString("N"));
        var previous = SongSuggest.MainInstance;
        Directory.CreateDirectory(root);
        try
        {
            SongSuggest.MainInstance = null;
            var core = new SongSuggest(Options(root));
            var source = new RecoveryDownloader();
            core.webDownloader = source;
            core.filesMeta.songLibraryVersion = "65.0";
            string top = Path.Combine(root, "Top10KPlayers.json");
            foreach (string? content in new string?[] { null, "", "[{\"id\":", new string('\0', 100), "null", "[]", "[null]",
                "[{\"id\":\"p\",\"rank\":1,\"top10kScore\":[]}]" })
            {
                if (content == null) File.Delete(top); else File.WriteAllText(top, content);
                core.EnsureScoreSaberCache();
                check(core.fileHandler.LoadScoreBoard("Top10KPlayers").Count == 1, "Corrupt Top10K was not restored");
                check(core.filesMeta.top10kVersion == "67.2" && core.filesMeta.songLibraryVersion == "65.0", "Recovery changed unrelated versions");
            }
            int calls = source.TopRequests;
            core.EnsureScoreSaberCache();
            check(source.TopRequests == calls, "Healthy cache redownloaded");
            File.WriteAllText(top, File.ReadAllText(top) + " {}");
            core.EnsureScoreSaberCache();
            check(source.TopRequests == calls + 1, "Trailing JSON was accepted");

            var protectedFiles = new Dictionary<string, string> {
                ["Liked Songs.json"] = "[]", ["Banned Songs.json"] = "[]",
                ["Players/Local Scores.json"] = "[]", ["Players/BLtest.json"] = "{\"history\":true}",
                ["SongLibrary.json"] = File.ReadAllText(Path.Combine(root, "SongLibrary.json"))
            };
            foreach (var file in protectedFiles) File.WriteAllText(Path.Combine(root, file.Key), file.Value);
            foreach (string fault in new[] { "offline", "metadata", "empty", "null", "invalid", "duplicate", "nonfinite", "invalid-metadata", "changed" })
            {
                File.WriteAllText(top, "broken-original");
                string metadata = File.ReadAllText(Path.Combine(root, "Files.meta"));
                source.Fault = fault;
                throws(core.EnsureScoreSaberCache, "Failed recovery was accepted: " + fault);
                check(File.ReadAllText(top) == "broken-original", "Failed recovery replaced the original");
                check(File.ReadAllText(Path.Combine(root, "Files.meta")) == metadata, "Failed recovery advanced version metadata");
                foreach (var file in protectedFiles)
                    check(File.ReadAllText(Path.Combine(root, file.Key)) == file.Value, "Recovery touched personal/other data: " + file.Key);
            }
            source.Fault = null;
            core.EnsureScoreSaberCache();
            check(Directory.GetFiles(root, "Top10KPlayers.json.corrupt-*").Length > 0, "Corruption evidence was not backed up");
            string valid = File.ReadAllText(top);
            throws(() => core.fileHandler.SaveScoreBoard(new List<Top10kPlayer>(), "Top10KPlayers"), "Invalid cache saved");
            throws(() => core.fileHandler.SaveScoreBoard<Top10kPlayer>(new List<Top10kPlayer>(), "Top10KPlayers"), "Invalid generic cache saved");
            check(File.ReadAllText(top) == valid, "Invalid save damaged valid cache");

            string retryRoot = Path.Combine(root, "retry");
            Directory.CreateDirectory(retryRoot);
            File.WriteAllText(Path.Combine(retryRoot, "SongLibrary.json"), "{");
            SongSuggest.MainInstance = null;
            throws(() => new SongSuggest(Options(retryRoot)), "Failed startup succeeded");
            check(SongSuggest.MainInstance == null, "Failed constructor retained the global instance");
            File.WriteAllText(Path.Combine(retryRoot, "SongLibrary.json"), "[]");
            var retry = new SongSuggest(Options(retryRoot));
            check(ReferenceEquals(retry, SongSuggest.MainInstance) && retry.status == "Ready", "Startup retry did not recover");
        }
        finally
        {
            SongSuggest.MainInstance = previous;
            previous?.songLibrary.SetActive();
            Directory.Delete(root, true);
        }
    }

    private static CoreSettings Options(string root) => new CoreSettings { FilePathSettings = new FilePathSettings(root),
        UseScoreSaberLeaderboard = false, UseAccSaberLeaderboard = false, UseBeatLeaderLeaderboard = false,
        UpdateScoreSaberLeaderboard = false, UpdateAccSaberLeaderboard = false, UpdateBeatLeaderLeaderboard = false,
        ValidateCacheFilesOnInitialize = false, LoadRuntimeDataOnInitialize = false };

    private sealed class RecoveryDownloader : WebDownloading.WebDownloader
    {
        public string? Fault;
        public int TopRequests;
        private bool versionChanged;
        public override FilesMeta GetFilesMeta()
        {
            if (Fault == "metadata") throw new IOException("Metadata unavailable");
            return new FilesMeta { top10kVersion = Fault == "invalid-metadata" ? "invalid" : Fault == "changed" && versionChanged ? "68.0" : "67.2",
                top10kUpdated = new DateTime(2026, 10, 28, 0, 0, 0, DateTimeKind.Utc) };
        }
        public override List<Top10kPlayer> GetTop10kPlayers()
        {
            TopRequests++;
            if (Fault == "changed") versionChanged = true;
            if (Fault == "offline") throw new IOException("Offline");
            if (Fault == "null") return null!;
            if (Fault == "empty") return new();
            var player = new Top10kPlayer { id = "fixture", rank = 1, top10kScore = new() {
                new Top10kScore { songID = "100024", rank = 1, pp = Fault == "nonfinite" ? float.NaN : 500 }
            } };
            if (Fault == "invalid") player.top10kScore = null!;
            return Fault == "duplicate" ? new() { player, player } : new() { player };
        }
    }
}
