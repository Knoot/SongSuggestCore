using System;
using System.IO;
using FileHandling;
using Newtonsoft.Json;

namespace SongSuggestNS
{
    public partial class SongSuggest
    {
        private Exception beatLeaderStartupUpdateError;
        private bool NeedsScoreSaberPopulation => CoreSettings.UseScoreSaberLeaderboard || CoreSettings.UseAccSaberLeaderboard ||
            (CoreSettings.UseBeatLeaderLeaderboard && CoreSettings.FilterScoreSaberBiasInBeatLeader);

        public void EnsureScoreSaberCache()
        {
            status = "Checking Top10KPlayers.json integrity";
            try { fileHandler.LoadScoreBoard("Top10KPlayers"); return; }
            catch (Exception ex) when (ex is JsonException || ex is InvalidDataException || ex is IOException)
            { log?.WriteLine("Top10KPlayers.json needs recovery: " + ex.Message); }

            try
            {
                status = "Restoring Top10KPlayers.json";
                var metadata = webDownloader.GetFilesMeta();
                if (metadata == null || !Version.TryParse(metadata.top10kVersion, out _))
                    throw new InvalidDataException("Invalid Top10K version in remote Files.meta.");
                var players = webDownloader.GetTop10kPlayers();
                FileHandler.ValidateTop10kPlayers(players);
                var latest = webDownloader.GetFilesMeta();
                if (latest == null || latest.top10kVersion != metadata.top10kVersion || latest.top10kUpdated != metadata.top10kUpdated)
                    throw new InvalidDataException("Top10K version changed during recovery. Retry loading.");
                status = "Saving restored Top10KPlayers.json";
                string path = Path.Combine(fileHandler.filePathSettings.top10kPlayersPath, "Top10KPlayers.json");
                if (File.Exists(path)) File.Copy(path, path + ".corrupt-" + Guid.NewGuid().ToString("N"));
                fileHandler.SaveScoreBoard(players, "Top10KPlayers");
                // Only the restored source's version changes; personal and other source metadata are preserved.
                filesMeta.top10kVersion = metadata.top10kVersion;
                filesMeta.top10kUpdated = metadata.top10kUpdated;
                fileHandler.SaveFilesMeta(filesMeta);
                log?.WriteLine("Top10KPlayers.json restored successfully.");
            }
            catch (Exception ex)
            { throw new IOException("Cannot restore Top10KPlayers.json. Check the connection and retry loading.", ex); }
        }

        public void EnsureBeatLeaderCache()
        {
            status = "Checking BeatLeader cache integrity";
            if (BeatLeaderData.Load()) return;
            try
            {
                if (beatLeaderStartupUpdateError != null) throw beatLeaderStartupUpdateError;
                status = "Restoring BeatLeader data";
                BeatLeaderData.Refresh();
            }
            catch (Exception ex)
            { throw new IOException("Cannot restore BeatLeaderData. Check the connection and retry loading.", ex); }
        }

        private void WaitForBeatLeaderStartupCheck(System.Diagnostics.Stopwatch startupTimer)
        {
            var remaining = CoreSettings.BeatLeaderStartupCheckDelay - startupTimer.Elapsed;
            while (remaining > TimeSpan.Zero)
            {
                status = $"BeatLeader check in {Math.Ceiling(remaining.TotalSeconds):0}s";
                System.Threading.Thread.Sleep(TimeSpan.FromMilliseconds(Math.Min(1000, remaining.TotalMilliseconds)));
                remaining = CoreSettings.BeatLeaderStartupCheckDelay - startupTimer.Elapsed;
            }
        }
    }
}
