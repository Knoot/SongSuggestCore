using System.Net.Http;
using BeatLeader;
using Curve;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WebDownloading;

internal static class CaptureFixtures
{
    public static void Run(string path)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        JObject Read(string relative)
        {
            Thread.Sleep(400);
            return JObject.Parse(http.GetStringAsync("https://api.beatleader.com/" + relative).GetAwaiter().GetResult());
        }
        var ids = new List<string> { "28c591", "7e8f11", "290c991", "40370xxxxxxxx91", "3db71x91" };
        foreach (string filter in new[] { "mode=OneSaber&stars_from=4&stars_to=9", "mode=Standard&stars_from=6&stars_to=8", "mode=Standard&stars_from=3&stars_to=4" })
            ids.Add((string?)Read("leaderboards?type=ranked&count=1&page=1&" + filter)["data"]?[0]?["id"]
                ?? throw new InvalidDataException("Fixture selection returned no ranked maps."));
        var fixtures = new JArray();
        foreach (string id in ids.Distinct())
        {
            var before = Read("leaderboard/" + Uri.EscapeDataString(id) + "?count=1");
            var rating = WebDownloader.ParseBeatLeaderMapRating(before);
            var predictions = new JArray();
            foreach (double accuracy in new[] { 0, 0.90, 0.95, 0.96, 0.97, 0.98, 0.99, 0.995, 1 })
            {
                var pp = Read("pp?leaderboardId=" + Uri.EscapeDataString(id) + "&context=noMods&acc=" + accuracy.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                double expected = (double?)pp["pp"] ?? throw new InvalidDataException("Missing official PP.");
                double actual = BeatLeaderCurve.PP(accuracy, rating);
                if ((bool?)pp["hasPp"] != true || (string?)pp["leaderboardId"] != id ||
                    !double.IsFinite(expected) || Math.Abs(actual - expected) > Math.Max(0.01, expected * 0.0001))
                    throw new InvalidDataException($"Reference/API mismatch: {id}, acc={accuracy}, local={actual}, official={expected}");
                predictions.Add(pp);
            }
            var after = Read("leaderboard/" + Uri.EscapeDataString(id) + "?count=1");
            if (!JToken.DeepEquals(before["difficulty"], after["difficulty"]) || !JToken.DeepEquals(before["song"]?["hash"], after["song"]?["hash"]))
                throw new InvalidDataException("Ratings changed while capturing " + id);
            fixtures.Add(new JObject {
                ["ModelVersion"] = BeatLeaderCurve.ModelVersion,
                ["CapturedAt"] = DateTime.UtcNow,
                ["RatingApiRevision"] = "48cc07c61dd57a99d872c20b3df9085ffe9ba494",
                ["WebsiteRevision"] = "12415e959374f1813120fd67b9889ee3054e715e",
                ["Leaderboard"] = before, ["Predictions"] = predictions
            });
            Console.WriteLine($"Captured {id}: {predictions.Count} official values");
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, fixtures.ToString(Formatting.Indented));
    }
}
