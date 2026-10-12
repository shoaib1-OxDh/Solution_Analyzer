using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using SolutionAnalyzerFieldHealthChecker.Models;

namespace SolutionAnalyzerFieldHealthChecker.Services
{
    /// <summary>Report 2: duplicate / similar option sets (same table, across tables, and vs global option sets).</summary>
    public static class OptionSetService
    {
        class Info
        {
            public string Entity, Field, Display, SetName, Type, Norm;
            public bool Global, Custom;
            public HashSet<string> Labels;
        }

        static string L(Label l) => (l?.UserLocalizedLabel?.Label ?? "").Trim().ToLowerInvariant();

        static HashSet<string> Labels(OptionSetMetadataBase os)
        {
            var h = new HashSet<string>();
            if (os is OptionSetMetadata m) foreach (var op in m.Options) h.Add(L(op.Label));
            else if (os is BooleanOptionSetMetadata b) { h.Add(L(b.TrueOption?.Label)); h.Add(L(b.FalseOption?.Label)); }
            h.Remove("");
            return h;
        }

        public static DataTable Run(IOrganizationService svc, MetadataCache cache, Scope scope, AnalyzerOptions o, Action<string> log, CancellationToken ct)
        {
            var dt = Util.Table("Option sets", "Check", "Table", "Field", "Option set", "Is global", "Options:l",
                                "Match with", "Similarity %:d", "Recommendation");
            var list = new List<Info>();

            foreach (var e in cache.Entities.Where(scope.Entity))
                foreach (var a in e.Attributes.Where(x => scope.Attr(e, x)))
                {
                    OptionSetMetadataBase os = null;
                    if (a is EnumAttributeMetadata ea) os = ea.OptionSet;            // Picklist, MultiSelect, State, Status
                    else if (a is BooleanAttributeMetadata ba) os = ba.OptionSet;    // Two options
                    if (os == null) continue;
                    list.Add(new Info
                    {
                        Entity = e.LogicalName, Field = a.LogicalName, Display = Util.Display(a).ToLowerInvariant(),
                        SetName = os.Name, Type = a.AttributeType.ToString(), Global = os.IsGlobal == true,
                        Custom = a.IsCustomAttribute == true, Norm = Util.StripPrefix(a).ToLowerInvariant(),
                        Labels = Labels(os)
                    });
                }
            log($"Analysing {list.Count} option-set fields...");
            var seen = new HashSet<string>();

            void Add(string check, Info i, string match, double sim, string rec)
            {
                if (!seen.Add($"{check}|{i.Entity}|{i.Field}|{match}")) return;
                dt.Rows.Add(check, i.Entity, i.Field, i.SetName, i.Global ? "Yes" : "No", i.Labels.Count, match, Math.Round(sim * 100, 1), rec);
            }

            // (a) same name / display name on the same table
            foreach (var g in list.Where(i => i.Custom).GroupBy(i => i.Entity))
            {
                foreach (var grp in g.GroupBy(i => i.Norm).Where(x => x.Count() > 1)
                          .Concat(g.Where(i => i.Display.Length > 0).GroupBy(i => i.Display).Where(x => x.Count() > 1)))
                    foreach (var i in grp)
                        Add("Same name on table", i, string.Join(", ", grp.Where(x => x != i).Select(x => x.Field)),
                            1, "Review - possibly redundant fields on the same table");
            }

            // (b) similar local option sets across tables
            var locals = list.Where(i => !i.Global && (i.Type == "Picklist" || i.Type == "Virtual") && i.Labels.Count >= 2).ToList();
            for (int x = 0; x < locals.Count; x++)
            {
                ct.ThrowIfCancellationRequested();
                for (int y = x + 1; y < locals.Count; y++)
                {
                    var a = locals[x]; var b = locals[y];
                    if (!a.Custom && !b.Custom) continue;
                    if (Math.Abs(a.Labels.Count - b.Labels.Count) > Math.Max(a.Labels.Count, b.Labels.Count) * 0.5) continue;
                    double sim = Util.Jaccard(a.Labels, b.Labels);
                    if (sim >= o.OptionSetThreshold)
                        Add("Similar local option sets", a, $"{b.Entity}.{b.Field}", sim,
                            "Consider one shared global option set");
                }
            }

            // (c) local option set vs existing global option set
            foreach (var l in list.Where(i => !i.Global && i.Custom && i.Labels.Count >= 2 && (i.Type == "Picklist" || i.Type == "Virtual")))
            {
                Microsoft.Xrm.Sdk.Metadata.OptionSetMetadata best = null; double bestSim = 0; bool nameHit = false;
                foreach (var g in cache.GlobalOptionSets)
                {
                    var gl = Labels(g);
                    double sim = Util.Jaccard(l.Labels, gl);
                    var gname = (g.Name ?? "");
                    int u = gname.IndexOf('_'); if (u > 0) gname = gname.Substring(u + 1);
                    bool byName = gname.ToLowerInvariant() == l.Norm;
                    if (sim > bestSim || (byName && !nameHit)) { if (sim >= o.OptionSetThreshold || byName) { best = g; bestSim = sim; nameHit = byName; } }
                }
                if (best != null)
                    Add(nameHit ? "Matches global (name)" : "Matches global (values)", l, best.Name, bestSim,
                        $"Consider switching to global option set '{best.Name}'");
            }
            return dt;
        }
    }
}
