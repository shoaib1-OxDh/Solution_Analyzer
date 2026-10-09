using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using SolutionAnalyzer.Models;

namespace SolutionAnalyzer.Services
{
    /// <summary>Report 1: fields with the same/similar names on one table (msemr_subject vs xyz_subject) + fill %.</summary>
    public static class SimilarFieldsService
    {
        public static DataTable Run(IOrganizationService svc, MetadataCache cache, Scope scope, AnalyzerOptions o, Action<string> log, CancellationToken ct)
        {
            var dt = Util.Table("Similar fields", "Table", "Group key", "Field", "Display name", "Type",
                                "Total records:l", "Records with data:l", "Fill %:d", "Flag");

            foreach (var e in cache.Entities.Where(scope.Entity).OrderBy(x => x.LogicalName))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var attrs = e.Attributes.Where(a => scope.Attr(e, a) && Util.IsCountable(a)).ToList();
                    var groups = Cluster(attrs, o.FuzzyThreshold);
                    if (groups.Count == 0) continue;

                    log($"{e.LogicalName}: {groups.Count} group(s) of similar fields - counting data...");
                    var cols = groups.SelectMany(g => g).Select(a => a.LogicalName).Distinct().ToList();
                    var counts = DataCounter.Count(svc, e, cols, ct);

                    foreach (var g in groups)
                    {
                        var key = Util.StripPrefix(g[0]);
                        int withData = g.Count(a => counts.Filled[a.LogicalName] > 0);
                        foreach (var a in g)
                        {
                            long filled = counts.Filled[a.LogicalName];
                            double pct = counts.Total == 0 ? 0 : Math.Round(100.0 * filled / counts.Total, 1);
                            string flag = filled == 0 ? "Empty - delete candidate" : (withData > 1 ? "Merge candidate" : "");
                            dt.Rows.Add(e.LogicalName, key, a.LogicalName, Util.Display(a), a.AttributeType.ToString(),
                                        counts.Total, filled, pct, flag);
                        }
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { log($"SKIPPED {e.LogicalName}: {ex.Message}"); }
            }
            return dt;
        }

        // union-find clustering of attributes that look alike
        static List<List<AttributeMetadata>> Cluster(List<AttributeMetadata> attrs, double th)
        {
            int n = attrs.Count;
            var parent = Enumerable.Range(0, n).ToArray();
            int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }

            var norm = attrs.Select(a => Util.StripPrefix(a).ToLowerInvariant()).ToList();
            var disp = attrs.Select(a => Util.Display(a).Trim().ToLowerInvariant()).ToList();

            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                {
                    if (attrs[i].AttributeType != attrs[j].AttributeType) continue;
                    if (attrs[i].IsCustomAttribute != true && attrs[j].IsCustomAttribute != true) continue; // skip system-vs-system
                    bool same = norm[i] == norm[j] && norm[i].Length >= 3
                                || (disp[i].Length > 0 && disp[i] == disp[j])
                                || (norm[i].Length >= 4 && norm[j].Length >= 4 && Util.Similarity(norm[i], norm[j]) >= th);
                    if (same) parent[Find(i)] = Find(j);
                }

            return Enumerable.Range(0, n).GroupBy(Find).Where(g => g.Count() > 1)
                             .Select(g => g.Select(i => attrs[i]).ToList()).ToList();
        }
    }
}
