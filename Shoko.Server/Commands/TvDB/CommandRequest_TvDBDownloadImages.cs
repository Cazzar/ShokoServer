﻿using System;
using System.Xml;
using Shoko.Commons.Queue;
using Shoko.Models.Queue;
using Shoko.Server.Providers.TvDB;

namespace Shoko.Server.Commands
{
    [Serializable]
    public class CommandRequest_TvDBDownloadImages : CommandRequest_TvDBBase
    {
        public virtual int TvDBSeriesID { get; set; }
        public virtual bool ForceRefresh { get; set; }

        public override CommandRequestPriority DefaultPriority => CommandRequestPriority.Priority8;

        public override QueueStateStruct PrettyDescription => new QueueStateStruct
        {
            queueState = QueueStateEnum.DownloadTvDBImages,
            extraParams = new[] {TvDBSeriesID.ToString()}
        };


        public CommandRequest_TvDBDownloadImages()
        {
        }


        public CommandRequest_TvDBDownloadImages(int tvDBSeriesID, bool forced)
        {
            TvDBSeriesID = tvDBSeriesID;
            ForceRefresh = forced;
            CommandType = (int) CommandRequestType.TvDB_DownloadImages;
            Priority = (int) DefaultPriority;

            GenerateCommandID();
        }


        public override void ProcessCommand()
        {
            logger.Info("Processing CommandRequest_TvDBDownloadImages: {0}", TvDBSeriesID);

            try
            {
                TvDBApiHelper.DownloadAutomaticImages(TvDBSeriesID, ForceRefresh);
            }
            catch (Exception ex)
            {
                logger.Error("Error processing CommandRequest_TvDBDownloadImages: {0} - {1}", TvDBSeriesID,
                    ex);
            }
        }

        public override void GenerateCommandID()
        {
            CommandID = $"CommandRequest_TvDBDownloadImages_{TvDBSeriesID}";
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
                TvDBSeriesID =
                    int.Parse(TryGetProperty(docCreator, "CommandRequest_TvDBDownloadImages", "TvDBSeriesID"));
                ForceRefresh =
                    bool.Parse(TryGetProperty(docCreator, "CommandRequest_TvDBDownloadImages", "ForceRefresh"));
            }

            return true;
        }
    }
}