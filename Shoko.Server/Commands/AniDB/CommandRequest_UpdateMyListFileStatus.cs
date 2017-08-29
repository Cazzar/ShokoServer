﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Xml;
using Shoko.Commons.Queue;
using Shoko.Models.Queue;
using Shoko.Server.Repositories.Cached;
using Shoko.Models.Server;
using Shoko.Server.Models;
using Shoko.Server.Extensions;
using Shoko.Server.Repositories;

namespace Shoko.Server.Commands
{
    [Serializable]
    public class CommandRequest_UpdateMyListFileStatus : CommandRequestImplementation, ICommandRequest
    {
        public string FullFileName { get; set; }
        public string Hash { get; set; }
        public bool Watched { get; set; }
        public bool UpdateSeriesStats { get; set; }
        public int WatchedDateAsSecs { get; set; }

        public CommandRequestPriority DefaultPriority
        {
            get { return CommandRequestPriority.Priority8; }
        }

        public QueueStateStruct PrettyDescription
        {
            get
            {
                return new QueueStateStruct()
                {
                    queueState = QueueStateEnum.UpdateMyListInfo,
                    extraParams = new[] {FullFileName}
                };
            }
        }

        public CommandRequest_UpdateMyListFileStatus()
        {
        }

        public CommandRequest_UpdateMyListFileStatus(string hash, bool watched, bool updateSeriesStats,
            int watchedDateSecs)
        {
            this.Hash = hash;
            this.Watched = watched;
            this.CommandType = (int) CommandRequestType.AniDB_UpdateWatchedUDP;
            this.Priority = (int) DefaultPriority;
            this.UpdateSeriesStats = updateSeriesStats;
            this.WatchedDateAsSecs = watchedDateSecs;

            GenerateCommandID();
            this.FullFileName = RepoFactory.FileNameHash.GetByHash(Hash).FirstOrDefault()?.FileName;
        }

        public override void ProcessCommand()
        {
            logger.Info("Processing CommandRequest_UpdateMyListFileStatus: {0}", Hash);


            try
            {
                // NOTE - we might return more than one VideoLocal record here, if there are duplicates by hash
                SVR_VideoLocal vid = RepoFactory.VideoLocal.GetByHash(this.Hash);
                if (vid != null)
                {
                    bool isManualLink = false;
                    List<CrossRef_File_Episode> xrefs = vid.EpisodeCrossRefs;
                    if (xrefs.Count > 0)
                        isManualLink = xrefs[0].CrossRefSource != (int) CrossRefSource.AniDB;

                    if (isManualLink)
                    {
                        ShokoService.AnidbProcessor.UpdateMyListFileStatus(xrefs[0].AnimeID,
                            xrefs[0].GetEpisode().EpisodeNumber, this.Watched);
                        logger.Info("Updating file list status (GENERIC): {0} - {1}", vid.ToString(), this.Watched);
                    }
                    else
                    {
                        if (WatchedDateAsSecs > 0)
                        {
                            DateTime? watchedDate = Commons.Utils.AniDB.GetAniDBDateAsDate(WatchedDateAsSecs);
                            ShokoService.AnidbProcessor.UpdateMyListFileStatus(vid, this.Watched, watchedDate);
                        }
                        else
                            ShokoService.AnidbProcessor.UpdateMyListFileStatus(vid, this.Watched, null);
                        logger.Info("Updating file list status: {0} - {1}", vid.ToString(), this.Watched);
                    }

                    if (UpdateSeriesStats)
                    {
                        // update watched stats
                        List<SVR_AnimeEpisode> eps = RepoFactory.AnimeEpisode.GetByHash(vid.ED2KHash);
                        if (eps.Count > 0)
                        {
                            // all the eps should belong to the same anime
                            eps[0].GetAnimeSeries().QueueUpdateStats();
                            //eps[0].AnimeSeries.TopLevelAnimeGroup.UpdateStatsFromTopLevel(true, true, false);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error("Error processing CommandRequest_UpdateMyListFileStatus: {0} - {1}", Hash, ex.ToString());
                return;
            }
        }

        /// <summary>
        /// This should generate a unique key for a command
        /// It will be used to check whether the command has already been queued before adding it
        /// </summary>
        public override void GenerateCommandID()
        {
            this.CommandID = string.Format("CommandRequest_UpdateMyListFileStatus_{0}_{1}", Hash,
                Guid.NewGuid().ToString());
        }

        public override bool LoadFromDBCommand(CommandRequest cq)
        {
            this.CommandID = cq.CommandID;
            this.CommandRequestID = cq.CommandRequestID;
            this.CommandType = cq.CommandType;
            this.Priority = cq.Priority;
            this.CommandDetails = cq.CommandDetails;
            this.DateTimeUpdated = cq.DateTimeUpdated;

            // read xml to get parameters
            if (this.CommandDetails.Trim().Length > 0)
            {
                XmlDocument docCreator = new XmlDocument();
                docCreator.LoadXml(this.CommandDetails);

                // populate the fields
                this.Hash = TryGetProperty(docCreator, "CommandRequest_UpdateMyListFileStatus", "Hash");
                this.Watched = bool.Parse(
                    TryGetProperty(docCreator, "CommandRequest_UpdateMyListFileStatus", "Watched"));

                string sUpStats = TryGetProperty(docCreator, "CommandRequest_UpdateMyListFileStatus",
                    "UpdateSeriesStats");
                if (bool.TryParse(sUpStats, out bool upStats))
                    UpdateSeriesStats = upStats;

                if (
                    int.TryParse(
                        TryGetProperty(docCreator, "CommandRequest_UpdateMyListFileStatus", "WatchedDateAsSecs"),
                        out int dateSecs))
                    WatchedDateAsSecs = dateSecs;
                this.FullFileName = RepoFactory.FileNameHash.GetByHash(Hash).FirstOrDefault()?.FileName;
            }

            if (this.Hash.Trim().Length > 0)
                return true;
            else
                return false;
        }

        public override CommandRequest ToDatabaseObject()
        {
            GenerateCommandID();

            CommandRequest cq = new CommandRequest
            {
                CommandID = this.CommandID,
                CommandType = this.CommandType,
                Priority = this.Priority,
                CommandDetails = this.ToXML(),
                DateTimeUpdated = DateTime.Now
            };
            return cq;
        }
    }
}