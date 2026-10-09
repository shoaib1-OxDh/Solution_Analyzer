using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using SolutionAnalyzerFieldHealthChecker.Models;

namespace SolutionAnalyzerFieldHealthChecker.Services
{
    /// <summary>
    /// Report 3: configured MaxLength vs actual longest value for Single-line and Multi-line text.
    /// FetchXML cannot compute LEN(), so records are paged and measured in memory (one pass per table).
    /// </summary>
    public static class FieldLengthService
    {
        class Stat { public long NonEmpty; public int Max; public long Sum; public Dictionary<int, long> Hist = new Dictionary<int, long>(); }

        public static DataTable Run(IOrganizationService svc, MetadataCache cache, Scope scope, AnalyzerOptions o, Action<string> log, CancellationToken ct)
        {
            var dt = Util.Table("Field length", "Table", "Field", "Type", "Configured max:l", "Actual max used:l", "Avg used:d",
                                "P95 used:l", "Utilisation %:d", "Records analysed:l", "Non-empty:l", "Recommendation");

            foreach (var e in cache.Entities.Where(scope.Entity).OrderBy(x => x.LogicalName))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var fields = e.Attributes.Where(a => scope.Attr(e, a) && a.IsValidForRead == true &&
                                                         (a is StringAttributeMetadata || a is MemoAttributeMetadata)).ToList();
                    if (fields.Count == 0) continue;
                    log($"{e.LogicalName}: measuring {fields.Count} text field(s)...");

                    var stats = fields.ToDictionary(f => f.LogicalName, f => new Stat());
                    long analysed = 0;

                    var q = new QueryExpression(e.LogicalName) { ColumnSet = new ColumnSet(fields.Select(f => f.LogicalName).ToArray()), NoLock = true };
                    q.PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 };
                    EntityCollection r;
                    do
                    {
                        r = Util.WithRetry(() => svc.RetrieveMultiple(q), ct);
                        foreach (var ent in r.Entities)
                        {
                            analysed++;
                            foreach (var f in fields)
                            {
                                if (!(ent.Contains(f.LogicalName) && ent[f.LogicalName] is string s) || s.Length == 0) continue;
                                var st = stats[f.LogicalName];
                                st.NonEmpty++; st.Sum += s.Length; if (s.Length > st.Max) st.Max = s.Length;
                                st.Hist[s.Length] = (st.Hist.TryGetValue(s.Length, out var c) ? c : 0) + 1;
                            }
                        }
                        q.PageInfo.PageNumber++; q.PageInfo.PagingCookie = r.PagingCookie;
                    } while (r.MoreRecords && (o.SampleLimit == 0 || analysed < o.SampleLimit));

                    foreach (var f in fields)
                    {
                        var st = stats[f.LogicalName];
                        int cfg = (f as StringAttributeMetadata)?.MaxLength ?? (f as MemoAttributeMetadata)?.MaxLength ?? 0;
                        double util = cfg == 0 ? 0 : Math.Round(100.0 * st.Max / cfg, 1);
                        string rec = st.NonEmpty == 0 ? "No data yet"
                                   : util < o.LengthUtilThreshold ? "Over-provisioned (note: Dataverse can't reduce max length of an existing column - use for design review)"
                                   : "OK";
                        dt.Rows.Add(e.LogicalName, f.LogicalName, f.AttributeType.ToString(), cfg, st.Max,
                                    st.NonEmpty == 0 ? 0 : Math.Round((double)st.Sum / st.NonEmpty, 1),
                                    P95(st), util, analysed, st.NonEmpty, rec);
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { log($"SKIPPED {e.LogicalName}: {ex.Message}"); }
            }
            return dt;
        }

        static long P95(Stat st)
        {
            if (st.NonEmpty == 0) return 0;
            long target = (long)Math.Ceiling(st.NonEmpty * 0.95), run = 0;
            foreach (var kv in st.Hist.OrderBy(k => k.Key)) { run += kv.Value; if (run >= target) return kv.Key; }
            return st.Max;
        }
    }
}
