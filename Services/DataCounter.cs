using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;
using System.Threading;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

namespace SolutionAnalyzerFieldHealthChecker.Services
{
    public class CountResult
    {
        public long Total;
        public Dictionary<string, long> Filled = new Dictionary<string, long>();
        public void Add(CountResult r)
        {
            Total += r.Total;
            foreach (var kv in r.Filled) Filled[kv.Key] = (Filled.TryGetValue(kv.Key, out var v) ? v : 0) + kv.Value;
        }
    }

    /// <summary>
    /// Counts total rows and non-null rows per column with ONE aggregate FetchXML per table.
    /// If the 50,000 aggregate limit is hit, the createdon range is bisected recursively;
    /// as a last resort it falls back to paged retrieval.
    /// </summary>
    public static class DataCounter
    {
        public static CountResult Count(IOrganizationService svc, EntityMetadata e, IList<string> cols, CancellationToken ct)
        {
            bool hasCreated = e.Attributes.Any(a => a.LogicalName == "createdon");
            if (!hasCreated) return Bisect(svc, e, cols, null, null, 0, ct);
            return Bisect(svc, e, cols, new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc), DateTime.UtcNow.AddDays(1), 0, ct);
        }

        static CountResult Bisect(IOrganizationService svc, EntityMetadata e, IList<string> cols, DateTime? from, DateTime? to, int depth, CancellationToken ct)
        {
            try { return Aggregate(svc, e, cols, from, to, ct); }
            catch (FaultException<OrganizationServiceFault> ex) when (Util.IsAggregateLimit(ex))
            {
                if (from == null || depth >= 40 || (to.Value - from.Value).TotalSeconds < 2)
                    return Paged(svc, e, cols, from, to, ct);
                var mid = from.Value.AddTicks((to.Value - from.Value).Ticks / 2);
                var left = Bisect(svc, e, cols, from, mid, depth + 1, ct);
                left.Add(Bisect(svc, e, cols, mid, to, depth + 1, ct));
                return left;
            }
        }

        static string F(DateTime? d) => d.Value.ToString("yyyy-MM-ddTHH:mm:ssZ");

        static CountResult Aggregate(IOrganizationService svc, EntityMetadata e, IList<string> cols, DateTime? from, DateTime? to, CancellationToken ct)
        {
            var attrs = $"<attribute name='{e.PrimaryIdAttribute}' alias='total' aggregate='count'/>" +
                        string.Concat(cols.Select((c, i) => $"<attribute name='{c}' alias='c{i}' aggregate='countcolumn'/>"));
            var filter = from == null ? "" :
                $"<filter><condition attribute='createdon' operator='ge' value='{F(from)}'/><condition attribute='createdon' operator='lt' value='{F(to)}'/></filter>";
            var xml = $"<fetch aggregate='true' no-lock='true'><entity name='{e.LogicalName}'>{attrs}{filter}</entity></fetch>";

            var row = Util.WithRetry(() => svc.RetrieveMultiple(new FetchExpression(xml)), ct).Entities.FirstOrDefault();
            long Get(string alias)
            {
                if (row == null || !row.Contains(alias)) return 0;
                var v = row[alias]; if (v is AliasedValue av) v = av.Value;
                return v == null ? 0 : Convert.ToInt64(v);
            }
            var res = new CountResult { Total = Get("total") };
            for (int i = 0; i < cols.Count; i++) res.Filled[cols[i]] = Get("c" + i);
            return res;
        }

        static CountResult Paged(IOrganizationService svc, EntityMetadata e, IList<string> cols, DateTime? from, DateTime? to, CancellationToken ct)
        {
            var res = new CountResult();
            foreach (var c in cols) res.Filled[c] = 0;
            var q = new QueryExpression(e.LogicalName) { ColumnSet = new ColumnSet(cols.ToArray()), NoLock = true };
            if (from != null)
            {
                q.Criteria.AddCondition("createdon", ConditionOperator.GreaterEqual, from.Value);
                q.Criteria.AddCondition("createdon", ConditionOperator.LessThan, to.Value);
            }
            q.PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 };
            EntityCollection r;
            do
            {
                r = Util.WithRetry(() => svc.RetrieveMultiple(q), ct);
                res.Total += r.Entities.Count;
                foreach (var ent in r.Entities)
                    foreach (var c in cols)
                        if (ent.Contains(c) && ent[c] != null && !(ent[c] is string s && s.Length == 0)) res.Filled[c]++;
                q.PageInfo.PageNumber++;
                q.PageInfo.PagingCookie = r.PagingCookie;
            } while (r.MoreRecords);
            return res;
        }
    }
}
