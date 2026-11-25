using System.Globalization;
using WebDownloading;

internal static class BeatSaverRetryTests
{
    public static void Run(Action<bool, string> check)
    {
        var now = new DateTimeOffset(2026, 11, 25, 10, 0, 0, TimeSpan.Zero);
        foreach (string? header in new string?[] { null, "", "invalid", "-1" })
        {
            for (int attempt = 0; attempt < 3; attempt++)
                check(WebDownloader.GetBeatSaverRetryDelay(header!, attempt, now).TotalSeconds == 30 * (1 << attempt),
                    "BeatSaver invalid/missing Retry-After must use exponential backoff");
        }
        check(WebDownloader.GetBeatSaverRetryDelay("45", 0, now).TotalSeconds == 45, "BeatSaver delta-seconds ignored");
        check(WebDownloader.GetBeatSaverRetryDelay("0", 0, now).TotalSeconds == 1, "BeatSaver zero delay must not spin");
        check(WebDownloader.GetBeatSaverRetryDelay(now.AddSeconds(90).ToString("R", CultureInfo.InvariantCulture), 0, now).TotalSeconds == 90,
            "BeatSaver HTTP-date ignored");
        check(WebDownloader.GetBeatSaverRetryDelay(now.AddSeconds(-90).ToString("R", CultureInfo.InvariantCulture), 0, now).TotalSeconds == 1,
            "BeatSaver expired HTTP-date must not produce a negative delay");
        check(WebDownloader.GetBeatSaverRetryDelay(long.MaxValue.ToString(CultureInfo.InvariantCulture), 0, now) > TimeSpan.FromMinutes(10),
            "BeatSaver excessive Retry-After must stop the update without overflowing");
    }
}
