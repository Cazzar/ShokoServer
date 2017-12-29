﻿using System;
using System.Xml;
using Shoko.Commons.Queue;
using Shoko.Models.Queue;
using Shoko.Server.Models;
using Shoko.Server.Providers.TraktTV;
using Shoko.Server.Repositories;

namespace Shoko.Server.Commands
{
    [Serializable]
    public class CommandRequest_TraktHistoryEpisode : CommandRequest
    {
        public virtual int AnimeEpisodeID { get; set; }
        public virtual int Action { get; set; }

        public virtual TraktSyncAction ActionEnum => (TraktSyncAction) Action;

        public override CommandRequestPriority DefaultPriority => CommandRequestPriority.Priority9;

        public override QueueStateStruct PrettyDescription => new QueueStateStruct
        {
            queueState = QueueStateEnum.TraktAddHistory,
            extraParams = new[] {AnimeEpisodeID.ToString()}
        };

        public CommandRequest_TraktHistoryEpisode()
        {
        }

        public CommandRequest_TraktHistoryEpisode(int animeEpisodeID, TraktSyncAction action)
        {
            AnimeEpisodeID = animeEpisodeID;
            Action = (int) action;
            CommandType = (int) CommandRequestType.Trakt_EpisodeHistory;
            Priority = (int) DefaultPriority;

            GenerateCommandID();
        }

        public override void ProcessCommand()
        {
            logger.Info("Processing CommandRequest_TraktHistoryEpisode: {0}-{1}", AnimeEpisodeID, Action);

            try
            {
                if (!ServerSettings.Trakt_IsEnabled || string.IsNullOrEmpty(ServerSettings.Trakt_AuthToken)) return;

                SVR_AnimeEpisode ep = RepoFactory.AnimeEpisode.GetByID(AnimeEpisodeID);
                if (ep != null)
                {
                    TraktSyncType syncType = TraktSyncType.HistoryAdd;
                    if (ActionEnum == TraktSyncAction.Remove) syncType = TraktSyncType.HistoryRemove;
                    TraktTVHelper.SyncEpisodeToTrakt(ep, syncType);
                }
            }
            catch (Exception ex)
            {
                logger.Error("Error processing CommandRequest_TraktHistoryEpisode: {0} - {1}", AnimeEpisodeID,
                    ex);
            }
        }

        /// <summary>
        /// This should generate a unique key for a command
        /// It will be used to check whether the command has already been queued before adding it
        /// </summary>
        public override void GenerateCommandID()
        {
            CommandID = $"CommandRequest_TraktHistoryEpisode{AnimeEpisodeID}-{Action}";
        }

        public override bool InitFromDB(CommandRequest cq)
        {
            CommandID = cq.CommandID;
            CommandRequestID = cq.CommandRequestID;
            CommandType = cq.CommandType;
            Priority = cq.Priority;
            CommandDetails = cq.CommandDetails;
            DateTimeUpdated = cq.DateTimeUpdated;

            // read xml to get parameters
            if (CommandDetails.Trim().Length > 0)
            {
                XmlDocument docCreator = new XmlDocument();
                docCreator.LoadXml(CommandDetails);

                // populate the fields
                AnimeEpisodeID =
                    int.Parse(TryGetProperty(docCreator, "CommandRequest_TraktHistoryEpisode", "AnimeEpisodeID"));
                Action = int.Parse(TryGetProperty(docCreator, "CommandRequest_TraktHistoryEpisode", "Action"));
            }

            return true;
        }
    }
}