﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Dynamic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NLog;
using Shoko.Models.Client;
using Shoko.Models.Server;
using Shoko.Server.API.v2.Models.core;
using Shoko.Server.CommandQueue.Commands.AniDB;
using Shoko.Server.CommandQueue.Commands.Trakt;
using Shoko.Server.Extensions;
using Shoko.Server.Import;
using Shoko.Server.Models;
using Shoko.Server.PlexAndKodi;
using Shoko.Server.Providers.AniDB;
using Shoko.Server.Providers.TvDB;
using Shoko.Server.Repositories;
using Shoko.Server.Settings;
using Shoko.Server.Utilities;

namespace Shoko.Server.API.v2.Modules
{
    [Authorize]
    [ApiController]// As this module requireAuthentication all request need to have apikey in header.
    [Route("/api")]
    [ApiVersion("2.0")]
    public class Core : BaseController
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        #region 01.Settings

        /// <summary>
        /// Set JMMServer Port
        /// </summary>
        /// <returns></returns>
        [HttpPost("config/port/set")]
        public object SetPort(ushort port)
        {
            ServerSettings.Instance.ServerPort = port;
            return APIStatus.OK();
        }

        /// <summary>
        /// Get JMMServer Port
        /// </summary>
        /// <returns>A dynamic object of x.port == port</returns>
        [HttpGet("config/port/get")]
        public object GetPort()
        {
            dynamic x = new ExpandoObject();
            x.port = ServerSettings.Instance.ServerPort;
            return x;
        }

        /// <summary>
        /// Set Imagepath as default or custom
        /// </summary>
        /// <returns></returns>
        [HttpPost("config/imagepath/set")]
        public ActionResult SetImagepath([FromBody] ImagePath imagepath)
        {
            if (imagepath.isdefault)
            {
                ServerSettings.Instance.ImagesPath = ServerSettings.DefaultImagePath;
                return APIStatus.OK();
            }
            if (!string.IsNullOrEmpty(imagepath.path) && imagepath.path != string.Empty)
            {
                if (Directory.Exists(imagepath.path))
                {
                    ServerSettings.Instance.ImagesPath = imagepath.path;
                    return APIStatus.OK();
                }
                return new APIMessage(404, "Directory Not Found on Host");
            }
            return new APIMessage(400, "Path Missing");
        }

        /// <summary>
        /// Return ImagePath object
        /// </summary>
        /// <returns></returns>
        [HttpGet("config/imagepath/get")]
        public object GetImagepath()
        {
            ImagePath imagepath = new ImagePath
            {
                path = ServerSettings.Instance.ImagesPath,
                isdefault = ServerSettings.Instance.ImagesPath == ServerSettings.DefaultImagePath
            };
            return imagepath;
        }

        /// <summary>
        /// Return body of current working settings.json - this could act as backup
        /// </summary>
        /// <returns>Server settings</returns>
        [HttpGet("config/export")]
        public ActionResult<ServerSettings> ExportConfig()
        {
            try
            {
                return ServerSettings.Instance;
            }
            catch
            {
                return APIStatus.InternalError("Error while reading settings.");
            }
        }

        /// <summary>
        /// Import config file that was sent to in API body - this act as import from backup
        /// </summary>
        /// <returns>APIStatus</returns>
        [HttpPost("config/import")]
        public ActionResult ImportConfig([FromBody] CL_ServerSettings settings)
        {
            string raw_settings = settings.ToJSON();

            if (raw_settings.Length != new CL_ServerSettings().ToJSON().Length)
            {
                string path = Path.Combine(ServerSettings.ApplicationPath, "temp.json");
                System.IO.File.WriteAllText(path, raw_settings, Encoding.UTF8);
                try
                {
                    ServerSettings.LoadSettingsFromFile(path, true);
                    return APIStatus.OK();
                }
                catch
                {
                    return APIStatus.InternalError("Error while importing settings");
                }
            }
            return APIStatus.BadRequest("Empty settings are not allowed");
        }

        /// <summary>
        /// Return given setting
        /// </summary>
        /// <returns></returns>
        [HttpPost("config/get")]
        public ActionResult<Setting> GetSetting([FromBody] Setting setting)
        {
            try
            {
                // TODO Refactor Settings to a POCO that is serialized, and at runtime, build a dictionary of types to validate against
                if (string.IsNullOrEmpty(setting?.setting)) return APIStatus.BadRequest("An invalid setting was passed");
                var value = typeof(ServerSettings).GetProperty(setting.setting)?.GetValue(null, null);
                if (value == null) return APIStatus.BadRequest("An invalid setting was passed");

                return new Setting
                {
                    setting = setting.setting,
                    value = value.ToString()
                };
            }
            catch
            {
                return APIStatus.InternalError();
            }
        }

        [HttpPost("config/set")]
        public ActionResult<List<APIMessage>> SetSetting(Setting setting) => SetSetting(new List<Setting> { setting });

        /// <summary>
        /// Set given setting
        /// </summary>
        /// <returns></returns>
        [HttpPost("config/setmultiple")]
        public ActionResult<List<APIMessage>> SetSetting(List<Setting> settings)
        {
            // TODO Refactor Settings to a POCO that is serialized, and at runtime, build a dictionary of types to validate against
            try
            {
                List<APIMessage> errors = new List<APIMessage>();
                for (var index = 0; index < settings.Count; index++)
                {
                    var setting = settings[index];
                    if (string.IsNullOrEmpty(setting.setting))
                    {
                        errors.Add(APIStatus.BadRequest($"{index}: An invalid setting was passed"));
                        continue;
                    }

                    if (setting.value == null)
                    {
                        errors.Add(APIStatus.BadRequest($"{index}: An invalid value was passed"));
                        continue;
                    }

                    var property = typeof(ServerSettings).GetProperty(setting.setting);
                    if (property == null)
                    {
                        errors.Add(APIStatus.BadRequest($"{index}: An invalid setting was passed"));
                        continue;
                    }

                    if (!property.CanWrite)
                    {
                        errors.Add(APIStatus.BadRequest($"{index}: An invalid setting was passed"));
                        continue;
                    }
                    var settingType = property.PropertyType;
                    try
                    {
                        var converter = TypeDescriptor.GetConverter(settingType);
                        if (!converter.CanConvertFrom(typeof(string)))
                        {
                            errors.Add(APIStatus.BadRequest($"{index}: An invalid value was passed"));
                            continue;
                        }
                        var value = converter.ConvertFromInvariantString(setting.value);
                        if (value == null)
                        {
                            errors.Add(APIStatus.BadRequest($"{index}: An invalid value was passed"));
                            continue;
                        }
                        property.SetValue(null, value);
                    }
                    catch
                    {
                        errors.Add(APIStatus.BadRequest($"{index}: An invalid value was passed"));
                    }
                }

                if (errors.Count > 0)
                {
                    HttpContext.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                    return errors;
                }

                return APIStatus.OK();
            }
            catch
            {
                return APIStatus.InternalError();
            }
        }

        #endregion

        #region 02.AniDB

        /// <summary>
        /// Set AniDB account with login, password and client port
        /// </summary>
        /// <returns></returns>
        [HttpPost("anidb/set")]
        public ActionResult SetAniDB([FromBody] Credentials cred)
        {
            if (!string.IsNullOrEmpty(cred.login) && !string.IsNullOrEmpty(cred.password))
            {
                ServerSettings.Instance.AniDb.Username = cred.login;
                ServerSettings.Instance.AniDb.Password = cred.password;
                if (cred.port != 0)
                {
                    ServerSettings.Instance.AniDb.ClientPort = cred.port;
                }
                return APIStatus.OK();
            }

            return new APIMessage(400, "Login and Password missing");
        }

        /// <summary>
        /// Test AniDB Creditentials
        /// </summary>
        /// <returns></returns>
        [HttpGet("anidb/test")]
        public ActionResult TestAniDB()
        {
            ShokoService.AnidbProcessor.ForceLogout();
            ShokoService.AnidbProcessor.CloseConnections();

            Thread.Sleep(1000);

            Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo(ServerSettings.Instance.Culture);

            ShokoService.AnidbProcessor.Init(ServerSettings.Instance.AniDb.Username, ServerSettings.Instance.AniDb.Password,
                ServerSettings.Instance.AniDb.ServerAddress,
                ServerSettings.Instance.AniDb.ServerPort, ServerSettings.Instance.AniDb.ClientPort);

            if (ShokoService.AnidbProcessor.Login())
            {
                ShokoService.AnidbProcessor.ForceLogout();
                return APIStatus.OK();
            }

            return APIStatus.Unauthorized();
        }

        /// <summary>
        /// Return login/password/port of used AniDB
        /// </summary>
        /// <returns></returns>
        [HttpGet("anidb/get")]
        public Credentials GetAniDB()
        {
            return new Credentials
            {
                login = ServerSettings.Instance.AniDb.Username,
                password = ServerSettings.Instance.AniDb.Password,
                port = ServerSettings.Instance.AniDb.ClientPort
            };
        }

        /// <summary>
        /// Sync votes bettween Local and AniDB and only upload to MAL
        /// </summary>
        /// <returns></returns>
        [HttpGet("anidb/votes/sync")]
        public ActionResult SyncAniDBVotes()
        {
            //TODO APIv2: Command should be split into AniDb/MAL sepereate
            CommandQueue.Queue.Instance.Add(new CmdAniDBSyncMyVotes());
            return APIStatus.OK();
        }

        /// <summary>
        /// Sync AniDB List
        /// </summary>
        /// <returns></returns>
        [HttpGet("anidb/list/sync")]
        public ActionResult SyncAniDBList()
        {
            ShokoServer.SyncMyList();
            return APIStatus.OK();
        }

        /// <summary>
        /// Update all series infromation from AniDB
        /// </summary>
        /// <returns></returns>
        [HttpGet("anidb/update")]
        public ActionResult UpdateAllAniDB()
        {
            Importer.RunImport_UpdateAllAniDB();
            return APIStatus.OK();
        }

        [HttpGet("anidb/updatemissingcache")]
        public ActionResult UpdateMissingAniDBXML()
        {
            try
            {
                var allAnime = Repo.Instance.AniDB_Anime.GetAll().Select(a => a.AnimeID).OrderBy(a => a).ToList();
                logger.Info($"Starting the check for {allAnime.Count} anime XML files");
                int updatedAnime = 0;
                for (var i = 0; i < allAnime.Count; i++)
                {
                    var animeID = allAnime[i];
                    if (i % 10 == 1) logger.Info($"Checking anime {i + 1}/{allAnime.Count} for XML file");

                    var xml = APIUtils.LoadAnimeHTTPFromFile(animeID);
                    if (xml == null)
                    {
                        CommandQueue.Queue.Instance.Add(new CmdAniDBGetAnimeHTTP(animeID, true, false));
                        updatedAnime++;
                        continue;
                    }

                    var rawAnime = AniDBHTTPHelper.ProcessAnimeDetails(xml, animeID);
                    if (rawAnime == null)
                    {
                        CommandQueue.Queue.Instance.Add(new CmdAniDBGetAnimeHTTP(animeID, true, false));
                        updatedAnime++;
                    }
                }
                logger.Info($"Updating {updatedAnime} anime");
            }
            catch (Exception e)
            {
                logger.Error($"Error checking and queuing AniDB XML Updates: {e}");
                return APIStatus.InternalError(e.Message);
            }
            return APIStatus.OK();
        }

        #endregion

        #region 04.Trakt

        /// <summary>
        /// Get Trakt code and url
        /// </summary>
        /// <returns></returns>
        [HttpGet("trakt/code")]
        public ActionResult<Dictionary<string, object>> GetTraktCode()
        {
            var code = new ShokoServiceImplementation().GetTraktDeviceCode();
            if (code.UserCode == string.Empty)
                return APIStatus.InternalError("Trakt code doesn't exist on the server");

            Dictionary<string, object> result = new Dictionary<string, object>();
            result.Add("usercode", code.UserCode);
            result.Add("url", code.VerificationUrl);
            return result;
        }

        /// <summary>
        /// Return trakt authtoken
        /// </summary>
        /// <returns></returns>
        [HttpGet("trakt/get")]
        public ActionResult<Credentials> GetTrakt()
        {
            return new Credentials
            {
                token = ServerSettings.Instance.TraktTv.AuthToken,
                refresh_token = ServerSettings.Instance.TraktTv.RefreshToken
            };
        }

        /// <summary>
        /// Sync Trakt Collection
        /// </summary>
        /// <returns></returns>
        [HttpGet("trakt/sync")]
        public ActionResult SyncTrakt()
        {
            if (ServerSettings.Instance.TraktTv.Enabled && !string.IsNullOrEmpty(ServerSettings.Instance.TraktTv.AuthToken))
            {
                CommandQueue.Queue.Instance.Add(new CmdTraktSyncCollection(true));
                return APIStatus.OK();
            }

            return new APIMessage(204, "Trakt is not enabled or you are missing the authtoken");
        }

        /// <summary>
        /// Scan Trakt
        /// </summary>
        /// <returns></returns>
        [HttpGet("trakt/scan")]
        public ActionResult ScanTrakt()
        {
            Importer.RunImport_ScanTrakt();
            return APIStatus.OK();
        }

        [HttpPost("trakt/set")]
        [HttpGet("trakt/create")]
        public ActionResult TraktNotImplemented() => APIStatus.NotImplemented();

        #endregion

        #region 05.TvDB

        /// <summary>
        /// Scan TvDB
        /// </summary>
        /// <returns></returns>
        [HttpGet("tvdb/update")]
        public ActionResult ScanTvDB()
        {
            Importer.RunImport_ScanTvDB();
            return APIStatus.OK();
        }

        [HttpGet("tvdb/regenlinks")]
        public ActionResult RegenerateAllEpisodeLinks()
        {
            try
            {
                using (var upd = Repo.Instance.CrossRef_AniDB_Provider.BeginBatchUpdate(() => Repo.Instance.CrossRef_AniDB_Provider.GetByType(Shoko.Models.Enums.CrossRefType.TvDB)))
                {
                    foreach(SVR_CrossRef_AniDB_Provider p in upd)
                    {
                        p.EpisodesList.DeleteAllUnverifiedLinks();
                        if (p.EpisodesList.NeedPersitance)
                        {
                            p.EpisodesList.Persist();
                            upd.Update(p);
                        }
                    }
                    upd.Commit();
                }
                Repo.Instance.AnimeSeries.GetAll().ToList().AsParallel().ForAll(animeseries => LinkingHelper.GenerateEpisodeMatches(animeseries.AniDB_ID, Shoko.Models.Enums.CrossRefType.TvDB, true));
            }
            catch (Exception e)
            {
                logger.Error(e);
                return APIStatus.InternalError(e.Message);
            }

            return APIStatus.OK();
        }

        public class EpisodeMatchComparison
        {
            public string Anime { get; set; }
            public int AnimeID { get; set; }
            public IEnumerable<(AniEpSummary AniDB, TvDBEpSummary TvDB)> Current { get; set; }
            public IEnumerable<(AniEpSummary AniDB, TvDBEpSummary TvDB)> Calculated { get; set; }
        }

        public class AniEpSummary
        {
            public int AniDBEpisodeType { get; set; }
            public int AniDBEpisodeNumber { get; set; }
            public string AniDBEpisodeName { get; set; }

            protected bool Equals(AniEpSummary other)
            {
                return AniDBEpisodeType == other.AniDBEpisodeType && AniDBEpisodeNumber == other.AniDBEpisodeNumber && string.Equals(AniDBEpisodeName, other.AniDBEpisodeName);
            }

            public override bool Equals(object obj)
            {
                if (ReferenceEquals(null, obj)) return false;
                if (ReferenceEquals(this, obj)) return true;
                if (obj.GetType() != this.GetType()) return false;
                return Equals((AniEpSummary) obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    var hashCode = AniDBEpisodeType;
                    hashCode = (hashCode * 397) ^ AniDBEpisodeNumber;
                    hashCode = (hashCode * 397) ^ (AniDBEpisodeName != null ? AniDBEpisodeName.GetHashCode() : 0);
                    return hashCode;
                }
            }
        }

        public class TvDBEpSummary
        {
            public int TvDBSeason { get; set; }
            public int TvDBEpisodeNumber { get; set; }
            public string TvDBEpisodeName { get; set; }

            protected bool Equals(TvDBEpSummary other)
            {
                return TvDBSeason == other.TvDBSeason && TvDBEpisodeNumber == other.TvDBEpisodeNumber && string.Equals(TvDBEpisodeName, other.TvDBEpisodeName);
            }

            public override bool Equals(object obj)
            {
                if (ReferenceEquals(null, obj)) return false;
                if (ReferenceEquals(this, obj)) return true;
                if (obj.GetType() != this.GetType()) return false;
                return Equals((TvDBEpSummary) obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    var hashCode = TvDBSeason;
                    hashCode = (hashCode * 397) ^ TvDBEpisodeNumber;
                    hashCode = (hashCode * 397) ^ (TvDBEpisodeName != null ? TvDBEpisodeName.GetHashCode() : 0);
                    return hashCode;
                }
            }
        }

        [HttpGet("tvdb/checklinks")]
        public ActionResult<List<EpisodeMatchComparison>> CheckAllEpisodeLinksAgainstCurrent()
        {
            try
            {
                // This is for testing changes in the algorithm. It will be slow.
                var list = Repo.Instance.AnimeSeries.GetAll().Select(a => a.GetAnime())
                    .Where(a => !string.IsNullOrEmpty(a?.MainTitle)).OrderBy(a => a.MainTitle).ToList();
                var result = new List<EpisodeMatchComparison>();
                foreach (var animeseries in list)
                {
                    List<SVR_CrossRef_AniDB_Provider> tvxrefs =
                        Repo.Instance.CrossRef_AniDB_Provider.GetByAnimeIDAndType(animeseries.AnimeID,Shoko.Models.Enums.CrossRefType.TvDB);
                    int tvdbID = int.Parse(tvxrefs.FirstOrDefault()?.CrossRefID ?? "0");
                    var matches = LinkingHelper.GetEpisodeMatches(animeseries.AnimeID, tvdbID.ToString(), Shoko.Models.Enums.CrossRefType.TvDB).Select(a => (
                        AniDB: new AniEpSummary
                        {
                            AniDBEpisodeType = a.AniDB.EpisodeType,
                            AniDBEpisodeNumber = a.AniDB.EpisodeNumber,
                            AniDBEpisodeName = a.AniDB.GetEnglishTitle()
                        },
                        TvDB: a.Cross == null ? null : new TvDBEpSummary
                        {
                            TvDBSeason = a.Cross.Season,
                            TvDBEpisodeNumber = a.Cross.Number,
                            TvDBEpisodeName = a.Cross.Title
                        })).OrderBy(a => a.AniDB.AniDBEpisodeType).ThenBy(a => a.AniDB.AniDBEpisodeNumber).ToList();
                    var currentMatches = Repo.Instance.CrossRef_AniDB_Provider.GetByAnimeIDAndType(animeseries.AnimeID, Shoko.Models.Enums.CrossRefType.TvDB).SelectMany(a=>a.Episodes)
                        .Select(a =>
                        {
                            var AniDB = Repo.Instance.AniDB_Episode.GetByEpisodeID(a.AniDBEpisodeID);
                            var TvDB = Repo.Instance.TvDB_Episode.GetByTvDBID(int.Parse(a.ProviderEpisodeID));
                            return (AniDB: new AniEpSummary
                                {
                                    AniDBEpisodeType = AniDB.EpisodeType,
                                    AniDBEpisodeNumber = AniDB.EpisodeNumber,
                                    AniDBEpisodeName = AniDB.GetEnglishTitle()
                                },
                                TvDB: TvDB == null ? null : new TvDBEpSummary
                                {
                                    TvDBSeason = TvDB.SeasonNumber,
                                    TvDBEpisodeNumber = TvDB.EpisodeNumber,
                                    TvDBEpisodeName = TvDB.EpisodeName
                                });
                        }).OrderBy(a => a.AniDB.AniDBEpisodeType).ThenBy(a => a.AniDB.AniDBEpisodeNumber).ToList();
                    if (!currentMatches.SequenceEqual(matches))
                    {
                        result.Add(new EpisodeMatchComparison
                        {
                            Anime = animeseries.MainTitle,
                            AnimeID = animeseries.AnimeID,
                            Current = currentMatches,
                            Calculated = matches,
                        });
                    }
                }

                return result;
            }
            catch (Exception e)
            {
                logger.Error(e);
                return APIStatus.InternalError(e.Message);
            }
        }

        #endregion

        #region 06.MovieDB

        /// <summary>
        /// Scan MovieDB
        /// </summary>
        /// <returns></returns>
        [HttpGet("moviedb/update")]
        public ActionResult ScanMovieDB()
        {
            Importer.RunImport_ScanMovieDB();
            return APIStatus.OK();
        }

        #endregion

        #region 07.User

        /// <summary>
        /// return Dictionary int = id, string = username
        /// </summary>
        /// <returns></returns>
        [HttpGet("user/list")]
        public ActionResult<Dictionary<int, string>> GetUsers()
        {
            return new CommonImplementation().GetUsers();
        }

        /// <summary>
        /// Create user from Contract_JMMUser
        /// </summary>
        /// <returns></returns>
        [Authorize("admin")]
        [HttpPost("user/create")]
        public ActionResult CreateUser([FromBody] JMMUser user)
        {
            user.Password = Digest.Hash(user.Password);
            user.HideCategories = string.Empty;
            user.PlexUsers = string.Empty;
            return new ShokoServiceImplementation().SaveUser(user) == string.Empty
                ? APIStatus.OK()
                : APIStatus.InternalError();
        }

        /// <summary>
        ///  change current user password
        /// </summary>
        /// <returns></returns>
        [HttpPost("user/password")]
        public ActionResult ChangePassword([FromBody] JMMUser user)
        {
            return new ShokoServiceImplementation().ChangePassword(user.JMMUserID, user.Password) == string.Empty
                    ? APIStatus.OK()
                    : APIStatus.InternalError();
        }

        /// <summary>
        /// change given user (by uid) password
        /// </summary>
        /// <returns></returns>
        [HttpPost("user/password/{uid}")]
        [Authorize("admin")]
        public ActionResult ChangePassword(int uid, [FromBody] JMMUser user)
        {
            return new ShokoServiceImplementation().ChangePassword(uid, user.Password) == string.Empty
                ? APIStatus.OK()
                : APIStatus.InternalError();
        }

        /// <summary>
        /// Delete user from his ID
        /// </summary>
        /// <returns></returns>
        [HttpPost("user/delete")]
        [Authorize("admin")]
        public ActionResult DeleteUser([FromBody] JMMUser user)
        {
            return new ShokoServiceImplementation().DeleteUser(user.JMMUserID) == string.Empty
                ? APIStatus.OK()
                : APIStatus.InternalError();
        }

        #endregion

        #region 8.OS-based operations

        /// <summary>
        /// Return OSFolder object that is a folder from which jmmserver is running
        /// </summary>
        /// <returns></returns>
        [HttpGet("os/folder/base")]
        public OSFolder GetOSBaseFolder()
        {
            OSFolder dir = new OSFolder
            {
                full_path = Environment.CurrentDirectory
            };
            DirectoryInfo dir_info = new DirectoryInfo(dir.full_path);
            dir.dir = dir_info.Name;
            dir.subdir = new List<OSFolder>();

            foreach (DirectoryInfo info in dir_info.GetDirectories())
            {
                OSFolder subdir = new OSFolder
                {
                    full_path = info.FullName,
                    dir = info.Name
                };
                dir.subdir.Add(subdir);
            }
            return dir;
        }

        /// <summary>
        /// Return OSFolder object of directory that was given via
        /// </summary>
        /// <param name="folder"></param>
        /// <returns></returns>
        [HttpPost("/os/folder")]
        public ActionResult<OSFolder> GetOSFolder([FromQuery] string folder, [FromBody] OSFolder dir)
        {
            if (!string.IsNullOrEmpty(dir.full_path))
            {
                DirectoryInfo dir_info = new DirectoryInfo(dir.full_path);
                dir.dir = dir_info.Name;
                dir.subdir = new List<OSFolder>();

                foreach (DirectoryInfo info in dir_info.GetDirectories())
                {
                    OSFolder subdir = new OSFolder
                    {
                        full_path = info.FullName,
                        dir = info.Name
                    };
                    dir.subdir.Add(subdir);
                }
                return dir;
            }

            return new APIMessage(400, "full_path missing");
        }

        /// <summary>
        /// Return OSFolder with subdirs as every driver on local system
        /// </summary>
        /// <returns></returns>
        [HttpGet("os/drives")]
        public OSFolder GetOSDrives()
        {
            string[] drives = Directory.GetLogicalDrives();
            OSFolder dir = new OSFolder
            {
                dir = "/",
                full_path = "/",
                subdir = new List<OSFolder>()
            };
            foreach (string str in drives)
            {
                OSFolder driver = new OSFolder
                {
                    dir = str,
                    full_path = str
                };
                dir.subdir.Add(driver);
            }

            return dir;
        }

        #endregion

        #region 10. Logs

        /// <summary>
        /// Run LogRotator with current settings
        /// </summary>
        /// <returns></returns>
        [HttpGet("log/get")]
        public ActionResult StartRotateLogs()
        {
            ShokoServer.logrotator.Start();
            return APIStatus.OK();
        }

        /// <summary>
        /// Set settings for LogRotator
        /// </summary>
        /// <returns></returns>
        [HttpPost("log/rotate")]
        [Authorize("admin")]
        public ActionResult SetRotateLogs([FromBody] Logs rotator)
        {
            ServerSettings.Instance.LogRotator.Enabled = rotator.rotate;
            ServerSettings.Instance.LogRotator.Zip = rotator.zip;
            ServerSettings.Instance.LogRotator.Delete = rotator.delete;
            ServerSettings.Instance.LogRotator.Delete_Days = rotator.days.ToString();

            return APIStatus.OK();
        }

        /// <summary>
        /// Get settings for LogRotator
        /// </summary>
        /// <returns></returns>
        [HttpGet("log/rotate")]
        public ActionResult<Logs> GetRotateLogs()
        {
            Logs rotator = new Logs
            {
                rotate = ServerSettings.Instance.LogRotator.Enabled,
                zip = ServerSettings.Instance.LogRotator.Zip,
                delete = ServerSettings.Instance.LogRotator.Delete
            };
            int day = 0;
            if (!String.IsNullOrEmpty(ServerSettings.Instance.LogRotator.Delete_Days))
            {
                int.TryParse(ServerSettings.Instance.LogRotator.Delete_Days, out day);
            }
            rotator.days = day;

            return rotator;
        }

        /// <summary>
        /// return int position - current position
        /// return string[] lines - lines from current log file
        /// </summary>
        /// <param name="lines">max lines to return</param>
        /// <param name="position">position to seek</param>
        /// <returns></returns>
        [HttpGet("log/get/{lines?}/{position?}")]
        public ActionResult<Dictionary<string, object>> GetLog(int lines = 10, int position = 0)
        {
            string log_file = LogRotator.GetCurrentLogFile();
            if (string.IsNullOrEmpty(log_file))
            {
                return APIStatus.NotFound("Could not find current log name. Sorry");
            }

            if (!System.IO.File.Exists(log_file))
            {
                return APIStatus.NotFound();
            }

            Dictionary<string, object> result = new Dictionary<string, object>();
            FileStream fs = System.IO.File.OpenRead(log_file);

            if (position >= fs.Length)
            {
                result.Add("position", fs.Length);
                result.Add("lines", new string[] { });
                return result;
            }

            List<string> logLines = new List<string>();

            LogReader reader = new LogReader(fs, position);
            for (int i = 0; i < lines; i++)
            {
                string line = reader.ReadLine();
                if (line == null) break;
                logLines.Add(line);
            }
            result.Add("position", reader.Position);
            result.Add("lines", logLines.ToArray());
            return result;
        }

        #endregion

        #region 11. Image Actions

        [HttpGet("images/update")]
        public ActionResult UpdateImages()
        {
            Importer.RunImport_UpdateTvDB(true);
            ShokoServer.Instance.DownloadAllImages();

            return APIStatus.OK();
        }

        #endregion
    }
}
