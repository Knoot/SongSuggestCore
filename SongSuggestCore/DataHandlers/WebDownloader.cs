using System;
using System.Net;
using System.IO;
using ScoreSabersJson;
using BeatSaverJson;
using BeatLeaderJson;
using AccSaberJson;
using AccSaberReloadedJson;
using Newtonsoft.Json;
using SongSuggestNS;
using Data;
using System.Collections.Generic;
using System.Linq;
using SongLibraryNS;
using LinkedData;
using System.Text;
using Settings;
using PlaylistJson;
using Newtonsoft.Json.Linq;

namespace WebDownloading
{
    public class WebDownloader
    {
        private const string InitialDataBaseUrl = "https://raw.githubusercontent.com/Knoot/SmartSongSuggest/master/TaohSongSuggest/Configuration/InitialData";

        public SongSuggest songSuggest { get; set; }

        private WebClient client = new WebClient();
        private JsonSerializerSettings serializerSettings = new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore };

        //Throttlers
        private Throttler _ScoreSaberThrottler = new Throttler() {callPerPeriod = 75, callPeriodSeconds = 16 }; //400/60
        private Throttler _BeatLeaderThrottler = new Throttler() {callPerPeriod = 30, callPeriodSeconds = 11 }; //50/10
        private Throttler _AccSaberReloadedThrottler = new Throttler() { callPerPeriod = 75, callPeriodSeconds = 16 }; //400/60

        public WebDownloader()
        {
            //Adding Tls12 to allowed protocols to be able to download from the GIT.
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            client.Encoding = Encoding.UTF8;
        }

        //Default throttler should not impact in game client much unless a player has many scores, but the out of game client has some large batch jobs that could use full speed
        public void FullThrottle()
        {
            _ScoreSaberThrottler = new Throttler() { callPerPeriod = 50, callPeriodSeconds = 11 }; //400/60
            //_ScoreSaberThrottler = new Throttler() { callPerPeriod = 20, callPeriodSeconds = 6 }; //400/60
            _BeatLeaderThrottler = new Throttler() { callPerPeriod = 50, callPeriodSeconds = 11 }; //50/10
            _AccSaberReloadedThrottler = new Throttler() { callPerPeriod = 100, callPeriodSeconds = 16 }; //400/60
        }

        //Generic web puller for scores (starts page 1, page 0 and 1 gives same results)
        public PlayerScoreCollection GetScoreSaberPlayerScores(String playerID, String sorting, int count, int page)
        {
            try
            {
                _ScoreSaberThrottler.Call();
                //https://scoresaber.com/api/player/76561197993806676/scores?limit=20&sort=recent&page=2
                String scoresJSON = client.DownloadString("https://scoresaber.com/api/player/" + playerID + "/scores?limit=" + count + "&sort=" + sorting + "&page=" + page);
                return JsonConvert.DeserializeObject<PlayerScoreCollection>(scoresJSON, serializerSettings);
            }
            catch (Exception ex)
            {
                string path = $"https://scoresaber.com/api/player/{playerID}/scores?limit={count}&sort={sorting}&page={page}";
                songSuggest.log?.WriteLine($"Error on user: {playerID} page: {page}\nPath:{path}\n{ex.Message}");
            }
            return new PlayerScoreCollection();
        }

        //Generic web puller for top players (starts page 1, page 0 and 1 gives same results)
        public PlayerCollection GetPlayers(int page)
        {
            try
            {
                _ScoreSaberThrottler.Call();
                //https://scoresaber.com/api/players?page=2
                String playersJSON = client.DownloadString("https://scoresaber.com/api/players?page=" + page);
                return JsonConvert.DeserializeObject<PlayerCollection>(playersJSON, serializerSettings);

            }
            catch
            {
                songSuggest.log?.WriteLine("Error on " + page);
            }
            return new PlayerCollection();
        }

        //Generic web puller for song leaderboards general data via Hash and Difficulty
        public LeaderboardInfo GetLeaderboardInfo(String hash, String difficulty)
        {
            try
            {
                _ScoreSaberThrottler.Call();
                //https://scoresaber.com/api/leaderboard/by-hash/E42BCDF50EA1F961CB8CEFE502E82806866F6479/info?difficulty=9&gameMode=SoloStandard
                String webLink = $"https://scoresaber.com/api/leaderboard/by-hash/{hash}/info?difficulty={difficulty}&gameMode=SoloStandard";
                Console.WriteLine($"{webLink}");
                String songInfo = client.DownloadString(webLink);
                songSuggest.log?.WriteLine("Unknown Song found and downloaded");
                return JsonConvert.DeserializeObject<LeaderboardInfo>(songInfo, serializerSettings);
            }
            catch
            {
                songSuggest.log?.WriteLine("Error finding song Hash: " + hash + " Difficulty: " + difficulty);
            }
            return new LeaderboardInfo();
        }

        //Generic web puller for song leaderboards general data via SongID
        public LeaderboardInfo GetLeaderboardInfo(String songID)
        {
            try
            {
                _ScoreSaberThrottler.Call();
                //https://scoresaber.com/api/leaderboard/by-id/348871/info
                String songInfo = client.DownloadString("https://scoresaber.com/api/leaderboard/by-id/" + songID + "/info");
                return JsonConvert.DeserializeObject<LeaderboardInfo>(songInfo, serializerSettings);
            }
            catch
            {
                songSuggest.log?.WriteLine("Error finding song with ID: " + songID);
            }
            return new LeaderboardInfo();
        }

        //Generic web puller for a leaderboard collection
        public LeaderboardInfoCollection GetLeaderBoardCollection(int page)
        {
            try
            {
                _ScoreSaberThrottler.Call();
                //https://scoresaber.com/api/leaderboards?ranked=true&page=1
                String songInfo = client.DownloadString("https://scoresaber.com/api/leaderboards?ranked=true&page=" + page);
                return JsonConvert.DeserializeObject<LeaderboardInfoCollection>(songInfo, serializerSettings);
            }
            catch
            {
                songSuggest.log?.WriteLine("Error finding leaderboard page: " + page);
            }
            return new LeaderboardInfoCollection();
        }

        //Generic web puller for song leaderboards scores via SongID and page #
        public ScoreCollection GetLeaderboardScores(String songID, int page)
        {
            try
            {
                _ScoreSaberThrottler.Call();
                //https://scoresaber.com/api/leaderboard/by-id/348871/scores?page=1
                String songInfo = client.DownloadString("https://scoresaber.com/api/leaderboard/by-id/" + songID + "/scores?page=" + page);
                return JsonConvert.DeserializeObject<ScoreCollection>(songInfo, serializerSettings);
            }
            catch
            {
                songSuggest.log?.WriteLine($"Error finding song with ID: {songID} Page: {page}");
            }
            return new ScoreCollection();
        }

        //Generic web puller for song leaderboards scores via SongID and page #, countrycode
        public ScoreCollection GetLeaderboardScores(String songID, int page, String countryCode)
        {
            try
            {
                _ScoreSaberThrottler.Call();
                //https://scoresaber.com/api/leaderboard/by-id/348871/scores?page=1
                String songInfo = client.DownloadString("https://scoresaber.com/api/leaderboard/by-id/" + songID + "/scores?page=" + page + "&countries=" + countryCode);
                return JsonConvert.DeserializeObject<ScoreCollection>(songInfo, serializerSettings);
            }
            catch
            {
                songSuggest.log?.WriteLine("Error finding song with ID: " + songID);
            }
            return new ScoreCollection();
        }

        //Generic web puller for BeatSaver song ID
        public BeatSaverSongInfo GetBeatSaverSongInfo(String songHash)
        {
            try
            {
                //https://api.beatsaver.com/maps/hash/fda568fc27c20d21f8dc6f3709b49b5cc96723be
                String songInfo = client.DownloadString("https://api.beatsaver.com/maps/hash/" + songHash);
                return JsonConvert.DeserializeObject<BeatSaverSongInfo>(songInfo, serializerSettings);
            }
            catch
            {
                songSuggest.log?.WriteLine("Error finding song on BeatSaver with Hash: " + songHash);
            }
            return new BeatSaverSongInfo();
        }

        //Generic web puller for AccSaber Ranked Songs
        public List<AccSaberSongMeta> GetAccSaberSongInfo()
        {
            try
            {
                //https://api.accsaber.com/ranked-maps
                String songInfo = client.DownloadString("https://api.h2.accsaber.com/ranked-maps");
                return JsonConvert.DeserializeObject<List<AccSaberSongMeta>>(songInfo, serializerSettings);
            }
            catch
            {
                songSuggest.log?.WriteLine("Error getting AccSaber Ranked Songs");
            }
            return new List<AccSaberSongMeta>();
        }

        //Generic web puller for BeatLeader songScore
        public BeatLeaderJson.Score GetBeatLeaderScore(SongID songID)
        {
            try
            {
                _BeatLeaderThrottler.Call();
                //https://api.beatleader.com/score/76561197993806676/6F316D488C43288F3079407829C1028A5E998EBC/ExpertPlus/Standard
                string playerID = songSuggest.activePlayerID;
                string songHash = songID.GetSong().hash;// songSuggest.songLibrary.GetHash(songID);
                string songDifcName = songID.GetSong().GetDifficultyText();//songSuggest.songLibrary.GetDifficultyName(songID);
                string songCharacteristic = songID.GetSong().characteristic;
                string webString = $"https://api.beatleader.com/score/{playerID}/{songHash}/{songDifcName}/{songCharacteristic}";
                string songInfo = client.DownloadString(webString);
                return JsonConvert.DeserializeObject<BeatLeaderJson.Score>(songInfo, serializerSettings);
            }
            catch
            {
                songSuggest.log?.WriteLine("Error finding song on BeatLeader with ID: " + songID);
            }
            return new BeatLeaderJson.Score();
        }

        //https://api.beatleader.com/leaderboard/clanRankings/2bf61xx51?page=1&count=1
        //Generic web puller for BeatLeader Clan Leaderboard
        public BeatLeaderJson.LeaderboardClanRankings GetBeatLeaderClanLeaderboard(SongID songID, int page, int count)
        {
            try
            {
                _BeatLeaderThrottler.Call();
                //https://api.beatleader.com/leaderboard/2bf61xx51?page=1&count=1
                string beatLeaderLeaderboardID = songID.GetSong().beatLeaderID;

                //This is internal handling as it is data missing in SongLibrary that could have been there, but is not maintained currently, only ranked songs are kept.
                if (string.IsNullOrEmpty(beatLeaderLeaderboardID))
                {
                    songSuggest.log?.WriteLine("This Clan Leaderboard is not in known Beat Leader data (Ranked Songs Only): " + songID);
                    return new BeatLeaderJson.LeaderboardClanRankings();
                }
                string webString = $"https://api.beatleader.com/leaderboard/clanRankings/{beatLeaderLeaderboardID}/?page={page}&count={count}";
                String songInfo = client.DownloadString(webString);
                return JsonConvert.DeserializeObject<BeatLeaderJson.LeaderboardClanRankings>(songInfo, serializerSettings);
            }
            catch
            {
                songSuggest.log?.WriteLine("Error finding Clan Leaderboard on BeatLeader with ID: " + songID);
            }
            return new BeatLeaderJson.LeaderboardClanRankings();
        }

        //https://api.beatleader.com/leaderboard/clanRankings/3da61xxxx91/clan/85?page=1&count=5
        //Generic web puller for BeatLeader Specified Clans Leaderboard (by ClanID)
        public BeatLeaderJson.LeaderboardClanRankings GetBeatLeaderClanLeaderboardByID(SongID songID, int clanID, int page, int count)
        {
            try
            {
                _BeatLeaderThrottler.Call();
                //https://api.beatleader.com/leaderboard/clanRankings/3da61xxxx91/clan/85?page=1&count=5
                string beatLeaderLeaderboardID = songID.GetSong().beatLeaderID;

                //This is internal handling as it is data missing in SongLibrary that could have been there, but is not maintained currently, only ranked songs are kept.
                if (string.IsNullOrEmpty(beatLeaderLeaderboardID))
                {
                    songSuggest.log?.WriteLine("This Clan Leaderboard is not in known Beat Leader data (Ranked Songs Only): " + songID);
                    return new BeatLeaderJson.LeaderboardClanRankings();
                }
                //https://api.beatleader.com/leaderboard/clanRankings/3da61xxxx91/clan/85?page=1&count=5
                string webString = $"https://api.beatleader.com/leaderboard/clanRankings/{beatLeaderLeaderboardID}/clan/{clanID}?page={page}&count={count}";
                String songInfo = client.DownloadString(webString);
                return JsonConvert.DeserializeObject<BeatLeaderJson.LeaderboardClanRankings>(songInfo, serializerSettings);
            }
            catch
            {
                songSuggest.log?.WriteLine("Error finding Clan Leaderboard on BeatLeader with ID: " + songID);
            }
            return new BeatLeaderJson.LeaderboardClanRankings();
        }

        //Generic web puller for BeatLeader Leaderboard
        public BeatLeaderJson.Leaderboard GetBeatLeaderLeaderboard(SongID songID, int page, int count, string context)
        {
            try
            {
                _BeatLeaderThrottler.Call();
                //https://api.beatleader.com/leaderboard/2bf61xx51?page=1&count=0&leaderboardContext=noMods
                string beatLeaderLeaderboardID = songID.GetSong().beatLeaderID;

                //This is internal handling as it is data missing in SongLibrary that could have been there, but is not maintained currently, only ranked songs are kept.
                if (string.IsNullOrEmpty(beatLeaderLeaderboardID))
                {
                    songSuggest.log?.WriteLine("This leaderboard is not in known Beat Leader data (Ranked Songs Only): " + songID);
                    return new BeatLeaderJson.Leaderboard();
                }
                string webString = $"https://api.beatleader.com/leaderboard/{beatLeaderLeaderboardID}/?page={page}&count={count}&LeaderboardContext={context}";
                String songInfo = client.DownloadString(webString);
                return JsonConvert.DeserializeObject<BeatLeaderJson.Leaderboard>(songInfo, serializerSettings);
            }
            catch
            {
                songSuggest.log?.WriteLine("Error finding Leaderboard on BeatLeader with ID: " + songID);
            }
            return new BeatLeaderJson.Leaderboard();
        }

        //Generic web puller for BeatLeader Leaderboard
        public BeatLeaderJson.Leaderboard GetBeatLeaderLeaderboard(SongID songID, int page, int count)
        {
            try
            {
                _BeatLeaderThrottler.Call();
                //https://api.beatleader.com/leaderboard/2bf61xx51?page=1&count=1
                string beatLeaderLeaderboardID = songID.GetSong().beatLeaderID;
                
                //This is internal handling as it is data missing in SongLibrary that could have been there, but is not maintained currently, only ranked songs are kept.
                if (string.IsNullOrEmpty(beatLeaderLeaderboardID))
                {
                    songSuggest.log?.WriteLine("This leaderboard is not in known Beat Leader data (Ranked Songs Only): " + songID);
                    return new BeatLeaderJson.Leaderboard();
                }
                string webString = $"https://api.beatleader.com/leaderboard/{beatLeaderLeaderboardID}/?page={page}&count={count}";
                String songInfo = client.DownloadString(webString);
                return JsonConvert.DeserializeObject<BeatLeaderJson.Leaderboard>(songInfo, serializerSettings);
            }
            catch
            {
                songSuggest.log?.WriteLine("Error finding Leaderboard on BeatLeader with ID: " + songID);
            }
            return new BeatLeaderJson.Leaderboard();
        }

        //Generic web puller for BeatLeader Compact Scores
        public virtual ScoresCompact GetBeatLeaderScoresCompact(string playerID, int count, int page ,long fromUnixTimeStamp)
        {
            string webString = "";
            try
            {
                //We grab oldest scores first, so updates are performed chronological
                //https://api.beatleader.com/player/76561197993806676/scores/compact?sortBy=date&order=1&page=1&count=100&time_from=1700000000
                if (playerID == "-1") return new ScoresCompact();
                webString = $"https://api.beatleader.com/player/{playerID}/scores/compact?sortBy=date&order=1&page={page}&count={count}&time_from={fromUnixTimeStamp}&leaderboardContext=noMods";
                return DownloadBeatLeader<ScoresCompact>(webString);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                songSuggest.log?.WriteLine($"Error getting: {webString}");
            }
            return new ScoresCompact();
        }

        //BeatLeader last updated Leaderboard Time
        public virtual long GetBeatLeaderLeaderboardUpdateTime()
        {
            string webString = "";
            try
            {
                _BeatLeaderThrottler.Call();
                //We grab oldest scores first, so updates are performed chronological
                //https://api.beatleader.com/songsuggest/refreshTime
                String playerID = songSuggest.activePlayerID;
                webString = $"https://api.beatleader.com/songsuggest/refreshTime";
                String updated = client.DownloadString(webString);
                return JsonConvert.DeserializeObject<long>(updated, serializerSettings);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                songSuggest.log?.WriteLine($"Error getting: {webString}");
            }
            return 0;
        }


        //Beatleader Leaderboard
        public List<Top10kPlayer> GetBeatLeaderLeaderboard()
        {
            try
            {
                _BeatLeaderThrottler.Call();
                //https://api.beatleader.com/songsuggest/
                //String songInfo = client.DownloadString("https://api.beatleader.com/songsuggest/?leaderboardContext=general");
                //String songInfo = client.DownloadString("https://api.beatleader.com/songsuggest/?leaderboardContext=noMods");
                String songInfo = client.DownloadString("https://api.beatleader.com/songsuggest/");
                return JsonConvert.DeserializeObject<List<Top10kPlayer>>(songInfo, serializerSettings);
            }
            catch
            {
                songSuggest.log?.WriteLine("Error finding BeatLeader Leaderboard");
            }
            return new List<Top10kPlayer>();
        }

        //Beatleader Leaderboard
        public virtual List<Top10kPlayer> GetBeatLeaderLeaderboard(string mode)
        {
            try
            {
                _BeatLeaderThrottler.Call();
                //https://api.beatleader.com/songsuggest/
                //String songInfo = client.DownloadString("https://api.beatleader.com/songsuggest/?leaderboardContext=general");
                //String songInfo = client.DownloadString("https://api.beatleader.com/songsuggest/?leaderboardContext=noMods");
                String request = $"https://api.beatleader.com/songsuggest/?leaderboardContext={mode}";
                String songInfo = client.DownloadString(request);
                return JsonConvert.DeserializeObject<List<Top10kPlayer>>(songInfo, serializerSettings);
            }
            catch
            {
                songSuggest.log?.WriteLine("Error finding BeatLeader Leaderboard");
            }
            return new List<Top10kPlayer>();
        }

        //Beatleader New Songs
        public virtual List<BeatLeaderJson.SongSuggestSong> GetBeatLeaderRankedSongs(long unixTimestamp)
        {
            // The endpoint returns a complete latest snapshot. The argument is retained for source compatibility.

            try
            {
                _BeatLeaderThrottler.Call();
                //https://api.beatleader.com/songsuggest/songs
                String songInfo = client.DownloadString("https://api.beatleader.com/songsuggest/songs");
                return JsonConvert.DeserializeObject<List<BeatLeaderJson.SongSuggestSong>>(songInfo, serializerSettings);
            }
            catch
            {
                songSuggest.log?.WriteLine($"Error receiving Beatleader Songlist past Timestamp: {unixTimestamp}");
            }
            return new List<BeatLeaderJson.SongSuggestSong>();
        }

        private T DownloadBeatLeader<T>(string url) where T : class
        {
            for (int attempt = 0; ; attempt++)
            {
                _BeatLeaderThrottler.Call();
                try
                {
                    using (var web = new WebClient())
                        return JsonConvert.DeserializeObject<T>(web.DownloadString(url), serializerSettings)
                            ?? throw new InvalidDataException("Null BeatLeader response: " + url);
                }
                catch (WebException ex)
                {
                    var response = ex.Response as HttpWebResponse;
                    if (attempt >= 2 || (response != null && (int)response.StatusCode < 500 && (int)response.StatusCode != 429)) throw;
                    System.Threading.Thread.Sleep(1000 * (attempt + 1));
                }
            }
        }

        public virtual BeatLeader.BeatLeaderMapRating GetBeatLeaderMapRating(string id)
        {
            var data = DownloadBeatLeader<Newtonsoft.Json.Linq.JObject>("https://api.beatleader.com/leaderboard/" + Uri.EscapeDataString(id) + "?count=1");
            return ParseBeatLeaderMapRating(data);
        }

        public virtual BeatLeader.BeatLeaderMapRatingsPage GetBeatLeaderMapRatingsPage(int page, int count)
        {
            if (page < 1 || count < 1 || count > 100) throw new ArgumentOutOfRangeException();
            var data = DownloadBeatLeader<JObject>($"https://api.beatleader.com/leaderboards?type=ranked&count={count}&page={page}");
            return ParseBeatLeaderMapRatingsPage(data);
        }

        public static BeatLeader.BeatLeaderMapRatingsPage ParseBeatLeaderMapRatingsPage(JObject data)
        {
            var metadata = data["metadata"] ?? throw new InvalidDataException("Missing BeatLeader page metadata.");
            var items = data["data"] as JArray ?? throw new InvalidDataException("Missing BeatLeader page data.");
            return new BeatLeader.BeatLeaderMapRatingsPage {
                Page = (int?)metadata["page"] ?? throw new InvalidDataException("Missing BeatLeader page number."),
                ItemsPerPage = (int?)metadata["itemsPerPage"] ?? throw new InvalidDataException("Missing BeatLeader page size."),
                Total = (int?)metadata["total"] ?? throw new InvalidDataException("Missing BeatLeader page total."),
                Ratings = items.Select(ParseBeatLeaderMapRating).ToList()
            };
        }

        public static BeatLeader.BeatLeaderMapRating ParseBeatLeaderMapRating(JToken data)
        {
            var d = data["difficulty"] ?? throw new InvalidDataException("Missing BeatLeader difficulty.");
            return new BeatLeader.BeatLeaderMapRating {
                Id = (string)data["id"], Hash = (string)data["song"]?["hash"], Mode = (string)d["modeName"], Difficulty = (string)d["difficultyName"],
                Status = (int?)d["status"] ?? -1, Stars = (double?)d["stars"], AccRating = (double?)d["accRating"],
                PassRating = (double?)d["passRating"], TechRating = (double?)d["techRating"], PredictedAcc = (double?)d["predictedAcc"],
                SliderPercentage = (double?)d["sliderPercentage"], Notes = (int?)d["notes"], NoteSpan = (double?)d["noteSpan"]
            };
        }

        public virtual double GetBeatLeaderPp(string id, double accuracy)
        {
            var data = DownloadBeatLeader<Newtonsoft.Json.Linq.JObject>("https://api.beatleader.com/pp?leaderboardId=" + Uri.EscapeDataString(id) +
                "&context=noMods&acc=" + accuracy.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            if ((bool?)data["hasPp"] != true || (string)data["leaderboardId"] != id || data["pp"] == null)
                throw new InvalidDataException("Official PP unavailable: " + id);
            double pp = (double)data["pp"];
            if (!Curve.BeatLeaderCurve.IsFinite(pp) || pp < 0) throw new InvalidDataException("Invalid official PP.");
            return pp;
        }

        public virtual ScoresCompact GetBeatLeaderPopulationScores(string playerId) =>
            DownloadBeatLeader<ScoresCompact>("https://api.beatleader.com/player/" + Uri.EscapeDataString(playerId) +
                "/scores/compact?sortBy=pp&order=desc&count=100&page=1&leaderboardContext=noMods");

        //Beatleader New Songs
        public BeatLeaderJson.ClanMembersList GetBeatLeaderClanMembers(string clanTag, int page, int count, bool primary)
        {
            try
            {
                _BeatLeaderThrottler.Call();
                //https://api.beatleader.com/clan/genx?page=1&count=100&primary=true
                string downloadstring = $"https://api.beatleader.com/clan/{clanTag}?page={page}&count={count}&primary={primary}";
                String songInfo = client.DownloadString(downloadstring);
                return JsonConvert.DeserializeObject<BeatLeaderJson.ClanMembersList>(songInfo, serializerSettings);
            }
            catch
            {
                songSuggest.log?.WriteLine($"Error receiving Beatleader New songs: ClanTag: {clanTag} page: {page} count: {count} primary {primary}");
            }
            return new BeatLeaderJson.ClanMembersList();
        }

        //AccSaberReloaded last updated Leaderboard Time
        public AccSaberReloadedLastUpdated GetAccSaberReloadedLeaderboardUpdateTime()
        {
            string webString = "";
            try
            {
                _AccSaberReloadedThrottler.Call();
                //https://api.accsaberreloaded.com/v1/songsuggest/refresh-time
                webString = $"https://api.accsaberreloaded.com/v1/songsuggest/refresh-time";

                String updated = client.DownloadString(webString);
                return JsonConvert.DeserializeObject<AccSaberReloadedLastUpdated>(updated, serializerSettings);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                songSuggest.log?.WriteLine($"Error getting: {webString}");
            }
            return new AccSaberReloadedLastUpdated();
        }

        //AccSaberReloaded ranked songs
        public List<AccSaberReloadedRankedSong> GetAccSaberReloadedRankedSongs()
        {
            string webString = "";
            try
            {
                _AccSaberReloadedThrottler.Call();
                //https://api.accsaberreloaded.com/v1/maps/difficulties/all
                webString = $"https://api.accsaberreloaded.com/v1/maps/difficulties/all";

                String updated = client.DownloadString(webString);
                return JsonConvert.DeserializeObject<List<AccSaberReloadedRankedSong>>(updated, serializerSettings);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                songSuggest.log?.WriteLine($"Error getting: {webString}");
            }
            return new List<AccSaberReloadedRankedSong>();
        }


        //Beatleader Leaderboard
        public List<Top10kPlayer> GetAccSaberReloadedLeaderboard()
        {
            string webString = "";
            try
            {
                _AccSaberReloadedThrottler.Call();
                //https://api.accsaberreloaded.com/v1/songsuggest
                webString = $"https://api.accsaberreloaded.com/v1/songsuggest";

                String songInfo = client.DownloadString(webString);
                return JsonConvert.DeserializeObject<List<Top10kPlayer>>(songInfo, serializerSettings);
            }
            catch
            {
                songSuggest.log?.WriteLine($"Error getting: {webString}");
            }
            return new List<Top10kPlayer>();
        }

        public FilesMeta GetFilesMeta()
        {
            songSuggest.status = "Getting Files.meta";
            //Let us check meta for updates.
            string webPath = $"{InitialDataBaseUrl}/Files.meta";
            string metaInfo = client.DownloadString(webPath);
            return JsonConvert.DeserializeObject<FilesMeta>(metaInfo, serializerSettings);
        }

        public List<Song> GetSongLibrary()
        {
            songSuggest.status = "Downloading Song Library";
            //Song Library pull
            string webPath = $"{InitialDataBaseUrl}/SongLibrary.json";
            //Download the file to a tmp file, add contents, and delete tmp when done.
            string songLibraryInfo = client.DownloadString(webPath);
            List<Song> songLibrary = JsonConvert.DeserializeObject<List<Song>>(songLibraryInfo, serializerSettings);
            return songLibrary;
        }

        public List<Top10kPlayer> GetTop10kPlayers()
        {
            songSuggest.status = "Downloading Player Scores";
            //Top 10k Download
            //where to get the files from
            string webPath = $"{InitialDataBaseUrl}/Top10KPlayers.json";
            string downloadString = client.DownloadString(webPath);

            return JsonConvert.DeserializeObject<List<Top10kPlayer>>(downloadString, serializerSettings);
        }

        internal Playlist LoadWebURL(string weblink)
        {
            try
            {
                //https://api.accsaber.com/ranked-maps
                String songInfo = client.DownloadString(weblink);
                return JsonConvert.DeserializeObject<Playlist>(songInfo, serializerSettings);
            }
            catch
            {
                songSuggest.log?.WriteLine("Error downloading given WebURL");
                return null;
            }
        }

        internal JObject LoadWebURL(PlaylistSyncURL weblink)
        {
            try
            {
                //https://api.accsaber.com/ranked-maps
                String songInfo = client.DownloadString(weblink.SyncURL);
                return JsonConvert.DeserializeObject<JObject>(songInfo, serializerSettings);
            }
            catch
            {
                songSuggest.log?.WriteLine("Error downloading given WebURL");
                return null;
            }
        }
    }

    internal class Throttler
    {
        private int _callPerPeriod;
        public int callPerPeriod
        {
            get
            {
                return _callPerPeriod;
            }
            set
            {
                requests = new DateTime[value];
                _callPerPeriod = value;
            }
        }
        public int callPeriodSeconds { get; set; }

        public int callIndex = 0;

        public DateTime[] requests;

        public void Call()
        {
            var previousIndexTime = requests[callIndex];

            //Figure out how long the 100 calls have taken.
            double timeSince = (DateTime.UtcNow - previousIndexTime).TotalSeconds;

            double missingTime = callPeriodSeconds - timeSince;

            if (missingTime >= 0)
            {
                double sleepMS = (missingTime * 1000) + 1; //adding one MS to round up.
                Console.WriteLine($"Sleeping: {sleepMS}ms");
                System.Threading.Thread.Sleep((int)sleepMS);
            }
            //Update when new request is expected fired.
            requests[callIndex] = DateTime.UtcNow;
            callIndex = (callIndex + 1) % callPerPeriod;
        }
    }
}
