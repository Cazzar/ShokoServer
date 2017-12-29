﻿using System.Collections.Generic;
using Shoko.Models.Server;
using NHibernate;
using NHibernate.Criterion;
using Shoko.Server.Databases;
using Shoko.Server.Repositories.NHibernate;

namespace Shoko.Server.Repositories.Direct
{
    public class AniDB_Anime_RelationRepository : BaseDirectRepository<AniDB_Anime_Relation, int>
    {
        private AniDB_Anime_RelationRepository()
        {
        }

        public static AniDB_Anime_RelationRepository Create()
        {
            return new AniDB_Anime_RelationRepository();
        }

        public AniDB_Anime_Relation GetByAnimeIDAndRelationID(int animeid, int relatedanimeid)
        {
            using (var session = DatabaseFactory.SessionFactory.OpenSession())
            {
                return GetByAnimeIDAndRelationID(session, animeid, relatedanimeid);
            }
        }

        public AniDB_Anime_Relation GetByAnimeIDAndRelationID(ISession session, int animeid, int relatedanimeid)
        {
            AniDB_Anime_Relation cr = session
                .CreateCriteria(typeof(AniDB_Anime_Relation))
                .Add(Restrictions.Eq("AnimeID", animeid))
                .Add(Restrictions.Eq("RelatedAnimeID", relatedanimeid))
                .UniqueResult<AniDB_Anime_Relation>();
            return cr;
        }

        public List<AniDB_Anime_Relation> GetByAnimeID(int id)
        {
            using (var session = DatabaseFactory.SessionFactory.OpenSession())
            {
                return GetByAnimeID(session.Wrap(), id);
            }
        }

        public List<AniDB_Anime_Relation> GetByAnimeID(ISessionWrapper session, int id)
        {
            var cats = session
                .CreateCriteria(typeof(AniDB_Anime_Relation))
                .Add(Restrictions.Eq("AnimeID", id))
                .List<AniDB_Anime_Relation>();

            return new List<AniDB_Anime_Relation>(cats);
        }
    }
}