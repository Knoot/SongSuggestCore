using Actions;
using ActivePlayerData;
using BeatLeader;
using BeatLeaderJson;
using Curve;
using Data;
using LinkedData;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PlayerScores;
using Settings;
using SongLibraryNS;
using SongSuggestNS;

int checks = 0;
void Check(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
void Throws(Action action, string message) { bool failed = false; try { action(); } catch { failed = true; } Check(failed, message); }
var fixtures = JArray.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "ppv4-official.json")));
foreach (var item in JArray.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "ppv4-slider-noise.json")))) fixtures.Add(item);
BeatLeaderMapRating Rating(JToken fixture)
{
    var lb = fixture["Leaderboard"]!; var d = lb["difficulty"]!;
    return new BeatLeaderMapRating { Id = (string)lb["id"]!, Hash = (string)lb["song"]!["hash"]!, Mode = (string)d["modeName"]!,
        Difficulty = (string)d["difficultyName"]!, Status = (int)d["status"]!, Stars = (double?)d["stars"], AccRating = (double?)d["accRating"],
        PassRating = (double?)d["passRating"], TechRating = (double?)d["techRating"], PredictedAcc = (double?)d["predictedAcc"],
        SliderPercentage = (double?)d["sliderPercentage"], Notes = (int?)d["notes"], NoteSpan = (double?)d["noteSpan"] };
}

foreach (var fixture in fixtures)
{
    var r = Rating(fixture);
    var parsed = WebDownloading.WebDownloader.ParseBeatLeaderMapRatingsPage(new JObject {
        ["metadata"] = new JObject { ["page"] = 1, ["itemsPerPage"] = 100, ["total"] = 1 },
        ["data"] = new JArray(fixture["Leaderboard"]!.DeepClone())
    });
    Check(parsed.Ratings.Single().IsComplete && parsed.Ratings[0].NoteSpan == r.NoteSpan &&
        parsed.Ratings[0].SliderPercentage == r.SliderPercentage, "Batch DTO lost PPv4 fields");
    Check(r.IsComplete, "Incomplete fixture " + r.Id);
    foreach (var pp in fixture["Predictions"]!)
    {
        double expected = (double)pp["pp"]!, accuracy = (double)pp["accuracy"]!;
        double actual = BeatLeaderCurve.PP(accuracy, r);
        Console.WriteLine($"{r.Id} {accuracy:P0}: local={actual:F6}, official={expected:F6}");
        Check(Math.Abs(expected - actual) <= Math.Max(0.01, expected * 1e-4), "Official PP mismatch " + r.Id);
    }
    Check(BeatLeaderCurve.PP(0, r) == 0 && double.IsFinite(BeatLeaderCurve.PP(1, r)), "Endpoints");
    foreach (double bad in new[] { -1, 1.01, double.NaN, double.PositiveInfinity }) Throws(() => BeatLeaderCurve.PP(bad, r), "Invalid accuracy accepted");
    var incomplete = JsonConvert.DeserializeObject<BeatLeaderMapRating>(JsonConvert.SerializeObject(r))!;
    incomplete.NoteSpan = null;
    Throws(() => BeatLeaderCurve.PP(0.97, incomplete), "Missing inputs accepted");
}
foreach (string mod in new[] { "", "IF", "BE", "IF,BE" }) Check(BeatLeaderModifiers.IsNoMods(mod), "Valid noMods rejected");
foreach (double? slider in new double?[] { null, double.NaN, double.PositiveInfinity, -0.001 })
{
    var map = Rating(fixtures[0]); map.SliderPercentage = slider;
    Check(!map.IsComplete && map.ValidationError.Contains("SliderPercentage"), "Invalid slider metadata accepted or not diagnosed");
    Throws(() => BeatLeaderCurve.PP(0.97, map), "Invalid slider percentage used in calculation");
}
foreach (string? mod in new[] { "FS", "SF", "SS", "NF", "UNKNOWN", "IF,FS", null }) Check(!BeatLeaderModifiers.IsNoMods(mod!), "Invalid noMods accepted");
foreach (double predicted in new[] { 0.6, 0.955, 0.9575, 0.96, 0.97, 0.985, 0.995, 0.9995 })
foreach (int notes in new[] { 20, 114, 115, 116 })
foreach (double span in new[] { 12.0, 40, 74.999, 75, 75.001 })
{
    var map = Rating(fixtures[0]); map.PredictedAcc = predicted; map.Notes = notes; map.NoteSpan = span;
    double prev = 0;
    foreach (double accuracy in new[] { 0.90, 0.95, 0.97, 0.98, 0.99, 1.0 })
    {
        double pp = BeatLeaderCurve.PP(accuracy, map);
        Check(double.IsFinite(pp) && pp >= prev, "Boundary curve is not finite/monotonic"); prev = pp;
    }
}

var root = Path.Combine(Path.GetTempPath(), "SongSuggestCore.Tests", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    SongSuggest.MainInstance = null;
    var initializationStatuses = new List<string>();
    var core = new SongSuggest(new CoreSettings { FilePathSettings = new FilePathSettings(root), UserID = "-1",
        StatusChanged = initializationStatuses.Add,
        UseBeatLeaderLeaderboard = false, UpdateBeatLeaderLeaderboard = false, UseAccSaberLeaderboard = false, UpdateAccSaberLeaderboard = false,
        UseScoreSaberLeaderboard = false, UpdateScoreSaberLeaderboard = false, LoadRuntimeDataOnInitialize = false, ValidateCacheFilesOnInitialize = false });
    Check(initializationStatuses.Contains("Loading local BeatLeader data") && initializationStatuses.Contains("Loading song library") &&
        initializationStatuses.Last() == "Ready", "Initialization stages were not published before constructor completion");
    if (args.Contains("--live"))
    {
        var batch = core.webDownloader.GetBeatLeaderMapRatingsPage(1, 100);
        Check(batch.Page == 1 && batch.ItemsPerPage == 100 && batch.Ratings.Count == Math.Min(100, batch.Total) &&
            batch.Ratings.All(m => m.IsComplete), "Live batch metadata contract");
        var compact = core.webDownloader.GetBeatLeaderScoresCompact("3225556157461414", 1, 1, 0);
        Check(compact.metadata?.page == 1 && compact.data?.Count == 1 && BeatLeaderModifiers.IsNoMods(compact.data[0].score.modifiers), "Live noMods compact score contract");
        foreach (var fixture in fixtures)
        {
            var current = core.webDownloader.GetBeatLeaderMapRating((string)fixture["Leaderboard"]!["id"]!);
            Check(current.IsComplete, "Live difficulty DTO incomplete");
            foreach (double accuracy in new[] { 0.90, 0.95, 0.97, 0.98, 0.99 })
            {
                double expected = core.webDownloader.GetBeatLeaderPp(current.Id, accuracy);
                Check(Math.Abs(expected - BeatLeaderCurve.PP(accuracy, current)) <= Math.Max(0.01, expected * 1e-4), "Live PPv4 mismatch");
            }
        }
    }
    var r = Rating(fixtures[0]);
    var songDto = new SongSuggestSong { id = r.Id, hash = r.Hash, name = "Fixture", mode = r.Mode, difficulty = r.Difficulty,
        stars = (float)r.Stars!, accRating = (float)r.AccRating!, passRating = (float)r.PassRating!, techRating = (float)r.TechRating! };
    var players = new List<Top10kPlayer> { new Top10kPlayer { id = "other", name = "fixture", rank = 1,
        top10kScore = new List<Top10kScore> { new Top10kScore { songID = r.Id, pp = 600, rank = 1 } } } };
    var datasetSongs = new List<SongSuggestSong> { songDto };
    var datasetRatings = new Dictionary<string, BeatLeaderMapRating> { [r.Id] = r };
    for (int i = 1; i < 20; i++)
    {
        var extra = JsonConvert.DeserializeObject<BeatLeaderMapRating>(JsonConvert.SerializeObject(r))!;
        extra.Id = "fixture" + i; extra.Hash = i.ToString("X40");
        var dto = JsonConvert.DeserializeObject<SongSuggestSong>(JsonConvert.SerializeObject(songDto))!;
        dto.id = extra.Id; dto.hash = extra.Hash;
        datasetSongs.Add(dto); datasetRatings[extra.Id] = extra;
        players[0].top10kScore.Add(new Top10kScore { songID = extra.Id, pp = 600 - i, rank = i + 1 });
    }
    var noisyRating = Rating(fixtures[fixtures.Count - 1]);
    datasetRatings[noisyRating.Id] = noisyRating;
    datasetSongs.Add(new SongSuggestSong { id = noisyRating.Id, hash = noisyRating.Hash, mode = noisyRating.Mode, difficulty = noisyRating.Difficulty,
        name = "Slider noise regression", stars = (float)noisyRating.Stars!, accRating = (float)noisyRating.AccRating!,
        passRating = (float)noisyRating.PassRating!, techRating = (float)noisyRating.TechRating! });
    core.BeatLeaderData.Commit(datasetSongs, players, datasetRatings, 123);
    Check(new BeatLeaderDataset(core).Load(), "Manifest round trip");
    var id = core.songLibrary.UpsertSong(songDto);
    var song = id.GetSong();
    core.activePlayer = new ActivePlayer("test", core);
    core.activePlayer.Load();
    core.activePlayer.ActiveScoreLocations.Add(ScoreLocation.SessionScores);
    core.AddSessionScore(id, 0.973123, "");
    core.activePlayer.ValidateBeatLeaderData();
    Check(core.activePlayer.GetRatedScore(id, LeaderboardType.BeatLeader) > 0, "New Session accuracy offline");
    Check(core.activePlayer.GetLeaderboardRank(id, LeaderboardType.BeatLeader) == 1, "Session-only rank");
    core.AddSessionScore(id, 0.999, "FS");
    double ppBefore = core.activePlayer.GetRatedScore(id, LeaderboardType.BeatLeader);
    Check(Math.Abs(ppBefore - BeatLeaderCurve.PP(0.973123, r)) < 1e-8, "Modified best hid noMods record");
    Check(core.activePlayer.BeatLeaderPlayedIDs().Contains(id), "Played history lost");
    core.activePlayer.ActiveScoreLocations.Clear();
    core.activePlayer.ActiveScoreLocations.Add(ScoreLocation.LocalScores);
    core.activePlayer.CachedRankings.Clear();
    core.AddLocalScore(id, 0.978123, "");
    Check(core.activePlayer.GetLeaderboardRank(id, LeaderboardType.BeatLeader) == 1, "Local-only rank");
    song.starScoreSaber = 9.3; song.complexityAccSaber = 5.5;
    Check(Math.Abs(core.activePlayer.GetRatedScore(id, LeaderboardType.ScoreSaber) - ScoreSaberCurve.PP(0.978123, 9.3)) < 1e-8, "Local ScoreSaber regression");
    Check(Math.Abs(core.activePlayer.GetRatedScore(id, LeaderboardType.AccSaber) - AccSaberCurve.AP(0.978123, 5.5)) < 1e-8, "Local AccSaber regression");
    core.fileHandler.SaveScoreCollection(new ScoreCollection { ScoresMeta = new ScoresMeta { FormatVersion = "1.0", DataVersion = core.filesMeta.top10kVersion },
        PlayerScores = new List<PlayerScore> { new PlayerScore { SongID = song.internalID, Accuracy = 0.99, RatedScore = 1234, Modifiers = "FS" } } }, "SStest");
    core.activePlayer.Load(); core.activePlayer.ActiveScoreLocations.Clear(); core.activePlayer.ActiveScoreLocations.Add(ScoreLocation.ScoreSaber);
    Check(core.activePlayer.GetRatedScore(id, LeaderboardType.ScoreSaber) == 1234, "Stored ScoreSaber PP changed");
    Check(core.activePlayer.GetRatedScore(id, LeaderboardType.BeatLeader) == 0 && core.activePlayer.BeatLeaderRankedIDs().Count == 0, "Modified SS accuracy counted as BL noMods");
    Check(core.activePlayer.BeatLeaderPlayedIDs().Contains(id), "Ineligible score lost played history");

    ScoresCompact Page(int number, int total, params DataItem[] items) => new ScoresCompact { metadata = new Metadata { page = number, total = total, itemsPerPage = 1 }, data = items.ToList() };
    DataItem Item(int time, float pp) => new DataItem { leaderboard = new LeaderboardCompact { id = r.Id },
        score = new BeatLeaderJson.Score { epochTime = time, accuracy = 0.97f, pp = pp, modifiers = "" } };
    var old = new ScoreCollection { PlayerScores = new List<PlayerScore> { new PlayerScore { SongID = song.internalID, RatedScore = 900, Modifiers = "" } } };
    var full = BeatLeaderScoreSync.Download(old, false, "123", (p, from) => { Check(from == 0, "Full refresh watermark"); return Page(1, 1, Item(100, 600)); }, _ => song);
    Check(full.PlayerScores.Single().RatedScore == 600 && old.PlayerScores.Single().RatedScore == 900, "Staging mutated old cache");
    Check(full.ScoresMeta.LastSuccessfulFullRefresh.HasValue, "Missing full-refresh marker");
    var inc = BeatLeaderScoreSync.Download(full, true, "123", (p, from) => { Check(from == 98, "Overlap missing"); return Page(1, 1, Item(100, 650)); }, _ => song);
    Check(inc.PlayerScores.Single().RatedScore == 650 && full.PlayerScores.Single().RatedScore == 600, "Incremental mutation");
    Throws(() => BeatLeaderScoreSync.Download(full, true, "123", (p, from) => p == 1 ? Page(1, 2, Item(101, 700)) : new ScoresCompact(), _ => song), "Partial sync succeeded");
    Check(full.ScoresMeta.CompletedWatermark == 100 && full.PlayerScores.Single().RatedScore == 600, "Failed sync committed");
    Throws(() => BeatLeaderScoreSync.Download(full, false, "123", (p, from) => Page(p, 2, Item(100, 600)), _ => song), "Repeated page record accepted");
    Throws(() => BeatLeaderScoreSync.Download(full, false, "123", (p, from) => Page(1, 1, Item(100, 600)), _ => song, (s, a) => 900), "Mixed-epoch user PP accepted");
    var empty = BeatLeaderScoreSync.Download(old, false, "123", (p, from) => Page(1, 0), _ => song);
    Check(empty.PlayerScores.Count == 0 && empty.ScoresMeta.LastSuccessfulFullRefresh.HasValue, "Empty profile rejected");
    var copy = JsonConvert.DeserializeObject<BeatLeaderMapRating>(JsonConvert.SerializeObject(r))!; copy.Stars = double.NaN;
    var invalidRatings = new Dictionary<string, BeatLeaderMapRating>(datasetRatings) { [r.Id] = copy };
    Throws(() => core.BeatLeaderData.Commit(datasetSongs, players, invalidRatings, 124), "Bad generation committed");
    Check(new BeatLeaderDataset(core).Load(), "Bad commit damaged valid generation");
    var transport = new FakeBeatLeaderDownloader { Revision = 124, Songs = datasetSongs, Players = players, Ratings = datasetRatings, FailMap = "fixture5" };
    core.webDownloader = transport;
    string generationBeforeFailure = core.BeatLeaderData.Manifest.Generation;
    Throws(() => core.BeatLeaderData.Refresh(true), "Failed metadata refresh committed");
    Check(core.BeatLeaderData.Manifest.Generation == generationBeforeFailure && new BeatLeaderDataset(core).Load(), "Failed refresh damaged manifest");
    transport.FailMap = null; transport.ChangeRevision = true;
    Throws(() => core.BeatLeaderData.Refresh(true), "Changing snapshot accepted");
    Check(new BeatLeaderDataset(core).Load(), "Changing snapshot damaged current data");
    transport.ChangeRevision = false;
    var mismatchedRating = JsonConvert.DeserializeObject<BeatLeaderMapRating>(JsonConvert.SerializeObject(r))!;
    mismatchedRating.AccRating += 1;
    transport.Ratings = new Dictionary<string, BeatLeaderMapRating>(datasetRatings) { [r.Id] = mismatchedRating };
    try { core.BeatLeaderData.Refresh(true); Check(false, "Real rating mismatch accepted"); }
    catch (IOException ex) { Check(ex.Message.Contains("Snapshot/live mismatch") && ex.Message.Contains("accRating="), "Rating mismatch not diagnosed"); }
    mismatchedRating.AccRating = r.AccRating; mismatchedRating.SliderPercentage = -0.001;
    try { core.BeatLeaderData.Refresh(true); Check(false, "Invalid live metadata accepted"); }
    catch (IOException ex) { Check(ex.Message.Contains("Invalid live PPv4 metadata") && ex.Message.Contains("SliderPercentage"), "Invalid metadata mislabeled as snapshot mismatch"); }
    transport.Ratings = datasetRatings; transport.Revision = 0;
    Throws(() => core.BeatLeaderData.Refresh(true), "Zero revision accepted");
    transport.Revision = 124; transport.Players = new List<Top10kPlayer>();
    Throws(() => core.BeatLeaderData.Refresh(true), "Empty population accepted");
    transport.Players = players;
    transport.ExtraMaps = 100;
    foreach (string fault in new[] { "short", "duplicate", "page", "total", "empty", "missing-field" })
    {
        transport.PageFault = fault;
        Throws(() => core.BeatLeaderData.Refresh(true), "Invalid batch accepted: " + fault);
        Check(core.BeatLeaderData.Manifest.Generation == generationBeforeFailure, "Invalid batch replaced dataset");
    }
    transport.PageFault = null;
    transport.FailPage = 2;
    Throws(() => core.BeatLeaderData.Refresh(true), "Failed second page committed");
    transport.FailPage = 0;
    int retryPages = transport.PageCalls;
    core.BeatLeaderData.Refresh();
    Check(transport.PageCalls - retryPages == 2, "Incomplete checkpoint skipped pagination on retry");
    transport.OmitMaps = 6;
    int singles = transport.MapCalls;
    Throws(() => core.BeatLeaderData.Refresh(true), "Mass single-map fallback accepted");
    Check(transport.MapCalls == singles, "Mass fallback issued individual requests");
    transport.OmitMaps = 1;
    core.BeatLeaderData.Refresh(true);
    Check(transport.MapCalls == singles + 1, "Missing single map was not recovered");
    transport.OmitMaps = 0;
    singles = transport.MapCalls;
    int pagesBefore = transport.PageCalls;
    core.BeatLeaderData.Refresh(true);
    Check(transport.MapCalls == singles && transport.PageCalls - pagesBefore == 2, "Full refresh did not use two batch pages");
    Check(core.BeatLeaderData.Manifest.Revision == 124 && new BeatLeaderDataset(core).Load(), "Successful refresh did not commit");
    int calls = transport.PageCalls;
    core.BeatLeaderData.Refresh();
    Check(transport.PageCalls == calls, "Unchanged dataset redownloaded map metadata");

    SongSuggest.MainInstance = null;
    var runtime = new SongSuggest(new CoreSettings { FilePathSettings = new FilePathSettings(root), UserID = "-1",
        UseBeatLeaderLeaderboard = true, UpdateBeatLeaderLeaderboard = false, FilterScoreSaberBiasInBeatLeader = false,
        UseScoreSaberLeaderboard = false, UpdateScoreSaberLeaderboard = false, UseAccSaberLeaderboard = false,
        UpdateAccSaberLeaderboard = false, ValidateCacheFilesOnInitialize = false });
    var origin = (SongID)(BeatLeaderID)r.Id;
    runtime.activePlayer.ActiveScoreLocations.Add(ScoreLocation.SessionScores);
    runtime.AddSessionScore(origin, 0.97, "");
    runtime.AddLocalScore(origin, 0.975, "");
    var request = new SongSuggestSettings { PlayerID = "-1", Leaderboard = LeaderboardType.BeatLeader,
        PlaylistLength = 3, IgnorePlayedDays = 0, IgnoreNonImproveable = false,
        PlaylistSettings = new PlaylistSettings { fileName = "PPv4-fixture" } };
    runtime.GenerateSongSuggestions(request);
    Check(runtime.songSuggest.songSuggestIDs.Count == 3, "Mini-population produced empty playlist");
    var previousSources = runtime.activePlayer.ActiveScoreLocations.ToArray();
    request.UseLocalScores = true;
    runtime.GenerateSongSuggestions(request);
    Check(runtime.activePlayer.ActiveScoreLocations.SequenceEqual(previousSources), "Local source selection leaked");
    string playlistPath = Path.Combine(root, "Playlists", "PPv4-fixture.bplist");
    string previousPlaylist = File.ReadAllText(playlistPath);
    var unknown = new SongSuggestSong { id = "missing", name = "Missing", hash = new string('A', 40), mode = "Standard", difficulty = "9", stars = 10 };
    var missingId = runtime.songLibrary.UpsertSong(unknown);
    runtime.AddLocalScore(missingId, 0.99, "");
    Throws(() => runtime.GenerateSongSuggestions(request), "Missing PP metadata silently ignored");
    Check(runtime.status == "Ready" && runtime.LastOperationError != null && RankedSongSuggest.ActiveRankedSongSuggest == null, "Operation did not terminate");
    Check(runtime.activePlayer.ActiveScoreLocations.SequenceEqual(previousSources), "Failed operation leaked sources");
    Check(File.ReadAllText(playlistPath) == previousPlaylist, "Failed operation replaced playlist");
    request.UseLocalScores = false;
    runtime.GenerateSongSuggestions(request);
    Check(runtime.songSuggest.songSuggestIDs.Count == 3 && runtime.LastOperationError == null, "Retry failed");

    var manifest = core.BeatLeaderData.Manifest;
    File.AppendAllText(Path.Combine(root, "BeatLeaderData", manifest.Generation, "BeatLeaderMapRatings.json"), " ");
    Check(!new BeatLeaderDataset(core).Load(), "Hash mismatch accepted");
}
finally { Directory.Delete(root, true); }
Console.WriteLine($"PASS: {checks} checks");

sealed class FakeBeatLeaderDownloader : WebDownloading.WebDownloader
{
    public long Revision;
    public bool ChangeRevision;
    public string? FailMap;
    public int MapCalls;
    public int PageCalls;
    public int ExtraMaps;
    public int OmitMaps;
    public int FailPage;
    public string? PageFault;
    public List<SongSuggestSong> Songs = new();
    public List<Top10kPlayer> Players = new();
    public Dictionary<string, BeatLeaderMapRating> Ratings = new();
    private int revisionCalls;
    public override long GetBeatLeaderLeaderboardUpdateTime() => Revision + (ChangeRevision && ++revisionCalls % 2 == 0 ? 1 : 0);
    public override List<SongSuggestSong> GetBeatLeaderRankedSongs(long timestamp) => Songs;
    public override List<Top10kPlayer> GetBeatLeaderLeaderboard(string mode)
    {
        if (mode != "noMods") throw new Exception("Wrong population context");
        return Players;
    }
    public override BeatLeaderMapRating GetBeatLeaderMapRating(string id)
    {
        MapCalls++;
        if (id == FailMap) throw new IOException("Simulated metadata failure");
        return Ratings[id];
    }
    public override BeatLeaderMapRatingsPage GetBeatLeaderMapRatingsPage(int page, int count)
    {
        PageCalls++;
        if (page == FailPage) throw new IOException("Simulated page failure");
        var maps = Ratings.Values.Skip(OmitMaps).ToList();
        for (int i = 0; i < ExtraMaps; i++)
        {
            var extra = JsonConvert.DeserializeObject<BeatLeaderMapRating>(JsonConvert.SerializeObject(Ratings.Values.First()))!;
            extra.Id = "outside-snapshot" + i;
            maps.Add(extra);
        }
        var result = new BeatLeaderMapRatingsPage { Page = page, ItemsPerPage = count, Total = maps.Count,
            Ratings = maps.Skip((page - 1) * count).Take(count).ToList() };
        if (result.Ratings.Any(m => m.Id == FailMap)) throw new IOException("Simulated batch failure");
        if (PageFault == "short") result.Ratings.RemoveAt(0);
        if (PageFault == "empty") result.Ratings.Clear();
        if (PageFault == "page") result.Page++;
        if (PageFault == "total" && page == 2) result.Total++;
        if (PageFault == "duplicate" && page == 2) result.Ratings[0] = maps[0];
        if (PageFault == "missing-field")
        {
            var invalid = JsonConvert.DeserializeObject<BeatLeaderMapRating>(JsonConvert.SerializeObject(result.Ratings[0]))!;
            invalid.NoteSpan = null; result.Ratings[0] = invalid;
        }
        return result;
    }
    public override double GetBeatLeaderPp(string id, double accuracy) => BeatLeaderCurve.PP(accuracy, Ratings[id]);
}
