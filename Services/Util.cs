using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.ServiceModel;
using System.Text;
using System.Threading;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

namespace SolutionAnalyzer.Services
{
    public static class Util
    {
        // ---------- naming ----------
        public static string StripPrefix(AttributeMetadata a)
        {
            var n = a.LogicalName;
            if (a.IsCustomAttribute != true) return n;
            int i = n.IndexOf('_');
            return i > 0 ? n.Substring(i + 1) : n;
        }

        public static string Display(AttributeMetadata a) => a.DisplayName?.UserLocalizedLabel?.Label ?? "";

        public static double Similarity(string a, string b)
        {
            if (a == b) return 1;
            int max = Math.Max(a.Length, b.Length);
            if (max == 0) return 1;
            if (Math.Abs(a.Length - b.Length) > max * 0.5) return 0;
            return 1.0 - (double)Lev(a, b) / max;
        }

        static int Lev(string s, string t)
        {
            var prev = new int[t.Length + 1];
            var cur = new int[t.Length + 1];
            for (int j = 0; j <= t.Length; j++) prev[j] = j;
            for (int i = 1; i <= s.Length; i++)
            {
                cur[0] = i;
                for (int j = 1; j <= t.Length; j++)
                {
                    int cost = s[i - 1] == t[j - 1] ? 0 : 1;
                    cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
                }
                var tmp = prev; prev = cur; cur = tmp;
            }
            return prev[t.Length];
        }

        public static double Jaccard(HashSet<string> a, HashSet<string> b)
        {
            if (a.Count == 0 || b.Count == 0) return 0;
            int inter = a.Count(b.Contains);
            return (double)inter / (a.Count + b.Count - inter);
        }

        // ---------- attribute filters ----------
        static readonly HashSet<AttributeTypeCode> Countable = new HashSet<AttributeTypeCode>
        {
            AttributeTypeCode.String, AttributeTypeCode.Memo, AttributeTypeCode.Integer, AttributeTypeCode.BigInt,
            AttributeTypeCode.Decimal, AttributeTypeCode.Double, AttributeTypeCode.Money, AttributeTypeCode.DateTime,
            AttributeTypeCode.Picklist, AttributeTypeCode.Boolean, AttributeTypeCode.Lookup, AttributeTypeCode.Customer,
            AttributeTypeCode.Owner
        };

        public static bool IsCountable(AttributeMetadata a) =>
            a.AttributeOf == null && a.IsValidForRead == true && a.AttributeType.HasValue &&
            Countable.Contains(a.AttributeType.Value) && a.IsPrimaryId != true;

        // ---------- service protection / retry ----------
        public static T WithRetry<T>(Func<T> f, CancellationToken ct)
        {
            for (int i = 0; ; i++)
            {
                ct.ThrowIfCancellationRequested();
                try { return f(); }
                catch (FaultException<OrganizationServiceFault> ex) when (i < 5 && IsThrottle(ex.Detail.ErrorCode))
                {
                    var wait = TimeSpan.FromSeconds(5 * (i + 1));
                    if (ex.Detail.ErrorDetails != null && ex.Detail.ErrorDetails.TryGetValue("Retry-After", out var ra) && ra is TimeSpan ts) wait = ts;
                    Thread.Sleep(wait);
                }
            }
        }

        static bool IsThrottle(int c) => c == -2147015902 || c == -2147015903 || c == -2147015898;

        public static bool IsAggregateLimit(FaultException<OrganizationServiceFault> ex) =>
            ex.Detail.ErrorCode == -2147164125 ||
            (ex.Detail.Message ?? "").IndexOf("AggregateQueryRecordLimit", StringComparison.OrdinalIgnoreCase) >= 0;

        public static List<Entity> RetrieveAll(IOrganizationService svc, QueryExpression q, CancellationToken ct)
        {
            var list = new List<Entity>();
            q.PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 };
            EntityCollection r;
            do
            {
                r = WithRetry(() => svc.RetrieveMultiple(q), ct);
                list.AddRange(r.Entities);
                q.PageInfo.PageNumber++;
                q.PageInfo.PagingCookie = r.PagingCookie;
            } while (r.MoreRecords);
            return list;
        }

        // ---------- tables / export ----------
        /// <summary>spec: "Name" (string), "Name:d" (double), "Name:l" (long)</summary>
        public static DataTable Table(string name, params string[] spec)
        {
            var dt = new DataTable(name);
            foreach (var s in spec)
            {
                var p = s.Split(':');
                var t = p.Length > 1 ? (p[1] == "d" ? typeof(double) : typeof(long)) : typeof(string);
                dt.Columns.Add(p[0], t);
            }
            return dt;
        }

        public static void ToCsv(DataTable dt, string path)
        {
            var sb = new StringBuilder();
            sb.AppendLine(string.Join(",", dt.Columns.Cast<DataColumn>().Select(c => Q(c.ColumnName))));
            foreach (DataRow r in dt.Rows)
                sb.AppendLine(string.Join(",", r.ItemArray.Select(v => Q(Convert.ToString(v)))));
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }

        static string Q(string s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
    }
}
