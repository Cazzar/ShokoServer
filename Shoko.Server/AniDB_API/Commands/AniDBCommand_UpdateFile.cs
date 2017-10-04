﻿using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Shoko.Commons.Utils;
using Shoko.Models.Interfaces;
using Shoko.Server;

namespace AniDBAPI.Commands
{
    public class AniDBCommand_UpdateFile : AniDBUDPCommand, IAniDBUDPCommand
    {
        public IHash FileData;
        public bool IsWatched;

        public string GetKey()
        {
            return "AniDBCommand_UpdateFile" + FileData.ED2KHash;
        }

        public virtual enHelperActivityType GetStartEventType()
        {
            return enHelperActivityType.UpdatingFile;
        }

        public virtual enHelperActivityType Process(ref Socket soUDP,
            ref IPEndPoint remoteIpEndPoint, string sessionID, Encoding enc)
        {
            ProcessCommand(ref soUDP, ref remoteIpEndPoint, sessionID, enc);

            // handle 555 BANNED and 598 - UNKNOWN COMMAND
            switch (ResponseCode)
            {
                case 598: return enHelperActivityType.UnknownCommand_598;
                case 555: return enHelperActivityType.Banned_555;
            }

            if (errorOccurred) return enHelperActivityType.NoSuchFile;

            string sMsgType = socketResponse.Substring(0, 3);
            switch (sMsgType)
            {
                case "210": return enHelperActivityType.FileAdded;
                case "310": return enHelperActivityType.FileAlreadyExists;
                case "311": return enHelperActivityType.UpdatingFile;
                case "320": return enHelperActivityType.NoSuchFile;
                case "411": return enHelperActivityType.NoSuchMyListFile;

                case "502": return enHelperActivityType.LoginFailed;
                case "501": return enHelperActivityType.LoginRequired;
            }

            return enHelperActivityType.FileDoesNotExist;
        }

        public AniDBCommand_UpdateFile()
        {
            commandType = enAniDBCommandType.UpdateFile;
        }

        public void Init(IHash fileData, bool watched, DateTime? watchedDate, bool isEdit, AniDBFile_State? fileState)
        {
            FileData = fileData;
            IsWatched = watched;

            commandID = fileData.Info;

            commandText = "MYLISTADD size=" + fileData.FileSize;
            commandText += "&ed2k=" + fileData.ED2KHash;
            commandText += "&viewed=" + (IsWatched ? "1" : "0"); //viewed
            if (fileState.HasValue)
                commandText += "&state=" + (int) fileState;
            if (watchedDate.HasValue)
                commandText += "&viewdate=" + AniDB.GetAniDBDateAsSeconds(watchedDate.Value);
            if (isEdit)
                commandText += "&edit=1";
        }

        public void Init(int animeID, int episodeNumber, bool watched, bool isEdit)
        {
            IsWatched = watched;

            commandText = "MYLISTADD aid=" + animeID;
            commandText += "&generic=1";
            commandText += "&epno=" + episodeNumber;
            commandText += "&state=" + (int) ServerSettings.AniDB_MyList_StorageState;
            commandText += "&viewed=" + (IsWatched ? "1" : "0"); //viewed
            if (isEdit)
                commandText += "&edit=1";
        }
    }
}