﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Shoko.Models.Server;
using NHibernate;
using NutzCode.InMemoryIndex;
using Shoko.Server.Databases;
using Shoko.Server.Models;
using Shoko.Server.PlexAndKodi;

namespace Shoko.Server.Repositories.Cached
{
    public class AnimeEpisodeRepository : BaseCachedRepository<SVR_AnimeEpisode, int>
    {
        private PocoIndex<int, SVR_AnimeEpisode, int> Series;
        private PocoIndex<int, SVR_AnimeEpisode, int> EpisodeIDs;

        private AnimeEpisodeRepository()
        {
            EndDeleteCallback = (cr) =>
            {
                RepoFactory.AnimeEpisode_User.Delete(
                    RepoFactory.AnimeEpisode_User.GetByEpisodeID(cr.AnimeEpisodeID));
            };
        }

        public static AnimeEpisodeRepository Create()
        {
            return new AnimeEpisodeRepository();
        }

        protected override int SelectKey(SVR_AnimeEpisode entity)
        {
            return entity.AnimeEpisodeID;
        }

        public override void PopulateIndexes()
        {
            Series = Cache.CreateIndex(a => a.AnimeSeriesID);
            EpisodeIDs = Cache.CreateIndex(a => a.AniDB_EpisodeID);
        }

        public override void RegenerateDb()
        {
        }


        private void UpdatePlexContract(SVR_AnimeEpisode e)
        {
            e.PlexContract = Helper.GenerateVideoFromAnimeEpisode(e);
        }


        public override void Save(IReadOnlyCollection<SVR_AnimeEpisode> objs)
        {
            foreach (SVR_AnimeEpisode ep in objs)
                Save(ep);
        }

        public override void Save(SVR_AnimeEpisode obj)
        {
            lock (obj)
            {
                if (obj.AnimeEpisodeID == 0)
                {
                    obj.PlexContract = null;
                    base.Save(obj);
                }
                UpdatePlexContract(obj);
                base.Save(obj);
            }
        }


        public List<SVR_AnimeEpisode> GetBySeriesID(int seriesid)
        {
            return Series.GetMultiple(seriesid);
        }


        public SVR_AnimeEpisode GetByAniDBEpisodeID(int epid)
        {
            //AniDB_Episode may not unique for the series, Example with Toriko Episode 1 and One Piece 492, same AniDBEpisodeID in two shows.
            return EpisodeIDs.GetOne(epid);
        }


        /// <summary>
        /// Get the AnimeEpisode 
        /// </summary>
        /// <param name="name">The filename of the anime to search for.</param>
        /// <returns>the AnimeEpisode given the file information</returns>
        public SVR_AnimeEpisode GetByFilename(string name)
        {
            return RepoFactory.VideoLocalPlace.GetAll()
                .Where(v => name.Equals(v.FilePath.Split('\\').LastOrDefault(),
                    StringComparison.InvariantCultureIgnoreCase))
                .Select(a => a.VideoLocal.GetAnimeEpisodes())
                .FirstOrDefault()
                ?.FirstOrDefault();
        }


        /// <summary>
        /// Get all the AnimeEpisode records associate with an AniDB_File record
        /// AnimeEpisode.AniDB_EpisodeID -> AniDB_Episode.EpisodeID
        /// AniDB_Episode.EpisodeID -> CrossRef_File_Episode.EpisodeID
        /// CrossRef_File_Episode.Hash -> VideoLocal.Hash
        /// </summary>
        /// <param name="hash"></param>
        /// <returns></returns>
        public List<SVR_AnimeEpisode> GetByHash(string hash)
        {
            return RepoFactory.CrossRef_File_Episode.GetByHash(hash)
                .Select(a => GetByAniDBEpisodeID(a.EpisodeID))
                .Where(a => a != null)
                .ToList();
            /*
            return
                session.CreateQuery(
                    "Select ae.AnimeEpisodeID FROM AnimeEpisode as ae, CrossRef_File_Episode as xref WHERE ae.AniDB_EpisodeID = xref.EpisodeID AND xref.Hash= :Hash")
                    .SetParameter("Hash", hash)
                    .List<int>().Select(GetByID).Where(a => a != null).ToList();*/
        }

        public List<SVR_AnimeEpisode> GetEpisodesWithMultipleFiles(bool ignoreVariations)
        {
            string ignoreVariationsQuery =
                @"SELECT ani.EpisodeID FROM VideoLocal AS vl JOIN CrossRef_File_Episode ani ON vl.Hash = ani.Hash WHERE vl.IsVariation = 0 AND vl.Hash != '' GROUP BY ani.EpisodeID HAVING COUNT(ani.EpisodeID) > 1";
            string countVariationsQuery =
                @"SELECT ani.EpisodeID FROM VideoLocal AS vl JOIN CrossRef_File_Episode ani ON vl.Hash = ani.Hash WHERE vl.Hash != '' GROUP BY ani.EpisodeID HAVING COUNT(ani.EpisodeID) > 1";
            using (var session = DatabaseFactory.SessionFactory.OpenSession())
            {
                IList<int> ids = ignoreVariations
                    ? session.CreateSQLQuery(ignoreVariationsQuery).List<int>()
                    : session.CreateSQLQuery(countVariationsQuery).List<int>();
                return ids.Select(GetByAniDBEpisodeID).ToList();
            }
            /*

            using (var session = JMMService.SessionFactory.OpenSession())
            {
                //FROM AnimeEpisode x WHERE x.AniDB_EpisodeID IN (Select xref.EpisodeID FROM CrossRef_File_Episode xref WHERE xref.Hash IN (Select vl.Hash from VideoLocal vl) GROUP BY xref.EpisodeID HAVING COUNT(xref.EpisodeID) > 1)


                //FROM AnimeEpisode x INNER JOIN (select xref.EpisodeID as EpisodeID from CrossRef_File_Episode xref inner join VideoLocal vl ON xref.Hash = vl.Hash group by xref.EpisodeID  having count(xref.EpisodeID)>1) g ON g.EpisodeID = x.AniDB_EpisodeID

                if (ServerSettings.DatabaseType.Trim()
                    .Equals(Constants.DatabaseType.MySQL, StringComparison.InvariantCultureIgnoreCase))
                {
                    // work around for MySQL performance issue when handling sub queries
                    List<AnimeEpisode> epList = new List<AnimeEpisode>();
                    string sql = "Select x.AnimeEpisodeID " +
                                 "FROM AnimeEpisode x " +
                                 "INNER JOIN  " +
                                 "(select xref.EpisodeID as EpisodeID " +
                                 "from CrossRef_File_Episode xref " +
                                 "inner join VideoLocal vl ON xref.Hash = vl.Hash ";

                    if (ignoreVariations)
                        sql += " where IsVariation = 0 ";

                    sql += "group by xref.EpisodeID  having count(xref.EpisodeID)>1) " +
                           "g ON g.EpisodeID = x.AniDB_EpisodeID " +
                           " ";
                    ArrayList results = DatabaseExtensions.Instance.GetData(sql);

                    foreach (object[] res in results)
                    {
                        int animeEpisodeID = int.Parse(res[0].ToString());
                        AnimeEpisode ep = GetByID(animeEpisodeID);
                        if (ep != null)
                            epList.Add(ep);
                    }

                    return epList;
                }
                else
                {
                    string sql = "SELECT x.AnimeEpisodeID FROM AnimeEpisode x WHERE x.AniDB_EpisodeID IN " +
                                 "(Select xref.EpisodeID FROM CrossRef_File_Episode xref WHERE xref.Hash IN " +
                                 "(Select vl.Hash from VideoLocal vl ";

                    if (ignoreVariations)
                        sql += " where IsVariation = 0 ";

                    sql += ") GROUP BY xref.EpisodeID HAVING COUNT(xref.EpisodeID) > 1)";

                    return session.CreateQuery(sql).List<int>().Select(GetByID).Where(a => a != null).ToList();
                    ;
                }

            }*/
        }

        public List<SVR_AnimeEpisode> GetUnwatchedEpisodes(int seriesid, int userid)
        {
            List<int> eps =
                RepoFactory.AnimeEpisode_User.GetByUserIDAndSeriesID(userid, seriesid)
                    .Where(a => a.WatchedDate.HasValue)
                    .Select(a => a.AnimeEpisodeID)
                    .ToList();
            return GetBySeriesID(seriesid).Where(a => !eps.Contains(a.AnimeEpisodeID)).ToList();
            /*
            using (var session = JMMService.SessionFactory.OpenSession())
            {
                return
                    session.CreateQuery(
                        "SELECT x.AnimeEpisodeID FROM AnimeEpisode x WHERE x.AnimeEpisodeID NOT IN (SELECT AnimeEpisodeID FROM AnimeEpisode_User WHERE AnimeSeriesID = :AnimeSeriesID AND JMMUserID = :JMMUserID) AND x.AnimeSeriesID = :AnimeSeriesID")
                        .SetParameter("AnimeSeriesID", seriesid)
                        .SetParameter("JMMUserID", userid)
                        .List<int>().Select(GetByID).Where(a => a != null).ToList();
            }*/
        }

        public List<SVR_AnimeEpisode> GetMostRecentlyAdded(int seriesID)
        {
            return GetBySeriesID(seriesID).OrderByDescending(a => a.DateTimeCreated).ToList();
        }
    }
}