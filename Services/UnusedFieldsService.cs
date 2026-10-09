using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using SolutionAnalyzer.Models;

namespace SolutionAnalyzer.Services
{
    /// <summary>Report 4: custom fields with zero data + dependency analysis. READ-ONLY.</summary>
    public static class UnusedFieldsService
    {
        const string SAFE = "SAFE TO DELETE", REVIEW = "REVIEW";

        static readonly Dictionary<int, string> TypeNames = new Dictionary<int, string>
        {
            {1,"Table"},{2,"Column"},{9,"Option set"},{10,"Relationship"},{14,"Alternate key"},{26,"View"},{29,"Process/Flow"},
            {59,"Chart"},{60,"Form"},{61,"Web resource"},{62,"Site map"},{80,"Model-driven app"},{91,"Plug-in assembly"},
            {92,"Plug-in step"},{300,"Canvas app"}
        };
        static readonly Dictionary<int, string> TypeTables = new Dictionary<int, string>
        {
            {26,"savedquery"},{29,"workflow"},{59,"savedqueryvisualization"},{60,"systemform"},{61,"webresource"},{92,"sdkmessageprocessingstep"}
        };

        public static DataTable Run(IOrganizationService svc, MetadataCache cache, Scope scope, AnalyzerOptions o, Action<string> log, CancellationToken ct)
        {
            var dt = Util.Table("Unused fields", "Table", "Field", "Display name", "Type", "Total records:l", "Records with data:l",
                                "Managed", "Review notes", "Dependencies:l", "Dependency details", "Possible references", "Status");

            // 1) find custom fields with no data
            var unused = new List<(EntityMetadata e, AttributeMetadata a, long total)>();
            foreach (var e in cache.Entities.Where(scope.Entity).OrderBy(x => x.LogicalName))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var cands = e.Attributes.Where(a => a.IsCustomAttribute == true && scope.Attr(e, a) && Util.IsCountable(a) && a.IsPrimaryName != true).ToList();
                    if (cands.Count == 0) continue;
                    log($"{e.LogicalName}: counting data in {cands.Count} custom field(s)...");
                    var total = new CountResult();
                    foreach (var chunk in cands.Select(a => a.LogicalName).Select((n, i) => new { n, i }).GroupBy(x => x.i / 40).Select(g => g.Select(x => x.n).ToList()))
                    {
                        var r = DataCounter.Count(svc, e, chunk, ct);
                        total.Total = r.Total; foreach (var kv in r.Filled) total.Filled[kv.Key] = kv.Value;
                    }
                    foreach (var a in cands)
                        if (total.Filled.TryGetValue(a.LogicalName, out var f) && f == 0) unused.Add((e, a, total.Total));
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { log($"SKIPPED {e.LogicalName}: {ex.Message}"); }
            }
            log($"{unused.Count} custom field(s) with no data. Checking dependencies...");

            // 2) text scan for references the dependency API can miss
            var scanner = ReferenceScanner.Load(svc, unused.Select(u => u.e.LogicalName).Distinct().ToList(), log, ct);
            var nameCache = new Dictionary<string, (string name, bool? managed)>();

            foreach (var (e, a, total) in unused)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var deps = new List<string>();
                    var resp = (RetrieveDependenciesForDeleteResponse)Util.WithRetry(() => svc.Execute(new RetrieveDependenciesForDeleteRequest
                    {
                        ObjectId = a.MetadataId.Value,
                        ComponentType = 2
                    }), ct);
                    foreach (var d in resp.EntityCollection.Entities)
                    {
                        int type = d.GetAttributeValue<OptionSetValue>("dependentcomponenttype")?.Value ?? 0;
                        var id = d.GetAttributeValue<Guid>("dependentcomponentobjectid");
                        var tname = TypeNames.TryGetValue(type, out var tn) ? tn : "Type " + type;
                        var (nm, managed) = Resolve(svc, type, id, nameCache, ct);
                        deps.Add($"{tname}: {nm ?? id.ToString()}{(managed == true ? " [managed]" : "")}");
                    }

                    var refs = scanner.Find(e.LogicalName, a.LogicalName);
                    var reasons = Reasons(a);
                    string status = (deps.Count == 0 && refs.Count == 0 && reasons.Count == 0) ? SAFE : REVIEW;

                    dt.Rows.Add(e.LogicalName, a.LogicalName, Util.Display(a), a.AttributeType.ToString(), total, 0,
                                a.IsManaged == true ? "Managed" : "Unmanaged", string.Join("; ", reasons),
                                deps.Count, string.Join(" | ", deps), string.Join(" | ", refs), status);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { log($"SKIPPED {e.LogicalName}.{a.LogicalName}: {ex.Message}"); }
            }
            return dt;
        }

        static List<string> Reasons(AttributeMetadata a)
        {
            var r = new List<string>();
            var rl = a.RequiredLevel?.Value;
            if (rl == AttributeRequiredLevel.SystemRequired || rl == AttributeRequiredLevel.ApplicationRequired) r.Add("Required field");
            if (a.SourceType == 1 || a.SourceType == 2) r.Add("Calculated/rollup");
            if (a is LookupAttributeMetadata) r.Add("Lookup - relationship will be affected");
            if (a is PicklistAttributeMetadata p && p.DefaultFormValue.HasValue && p.DefaultFormValue.Value >= 0) r.Add("Has default value");
            if (a is BooleanAttributeMetadata b && b.DefaultValue.HasValue) r.Add("Has default value");
            if (a.IsManaged == true) r.Add("Managed component (cannot be deleted directly)");
            return r;
        }

        static (string, bool?) Resolve(IOrganizationService svc, int type, Guid id, Dictionary<string, (string, bool?)> cache, CancellationToken ct)
        {
            if (!TypeTables.TryGetValue(type, out var table)) return (null, null);
            var key = table + id;
            if (cache.TryGetValue(key, out var hit)) return hit;
            try
            {
                var ent = Util.WithRetry(() => svc.Retrieve(table, id, new ColumnSet("name", "ismanaged")), ct);
                var res = (ent.GetAttributeValue<string>("name"), ent.GetAttributeValue<bool?>("ismanaged"));
                cache[key] = res; return res;
            }
            catch { cache[key] = (null, null); return (null, null); }
        }
    }

    /// <summary>
    /// Text search in forms, views, charts, classic workflows/business rules and plug-in step filtering attributes.
    /// Not scanned: web resource JS, Power Automate cloud flows, canvas apps, PCF configs - review those manually.
    /// </summary>
    public class ReferenceScanner
    {
        class Doc { public string Kind, Name, Text; }
        readonly Dictionary<string, List<Doc>> byEntity = new Dictionary<string, List<Doc>>();
        readonly List<Doc> anyEntity = new List<Doc>();

        void AddDoc(string entity, string kind, string name, params string[] texts)
        {
            var d = new Doc { Kind = kind, Name = name, Text = string.Join("\n", texts.Where(t => t != null)) };
            if (string.IsNullOrEmpty(entity) || entity == "none") { anyEntity.Add(d); return; }
            if (!byEntity.TryGetValue(entity, out var l)) byEntity[entity] = l = new List<Doc>();
            l.Add(d);
        }

        public static ReferenceScanner Load(IOrganizationService svc, List<string> entities, Action<string> log, CancellationToken ct)
        {
            var s = new ReferenceScanner();
            if (entities.Count == 0) return s;
            var ents = entities.Cast<object>().ToArray();
            log("Scanning forms, views, charts, processes and plug-in steps for field references...");

            var f = new QueryExpression("systemform") { ColumnSet = new ColumnSet("name", "objecttypecode", "formxml") };
            f.Criteria.AddCondition("objecttypecode", ConditionOperator.In, ents);
            foreach (var r in Util.RetrieveAll(svc, f, ct)) s.AddDoc(r.GetAttributeValue<string>("objecttypecode"), "Form", r.GetAttributeValue<string>("name"), r.GetAttributeValue<string>("formxml"));

            var v = new QueryExpression("savedquery") { ColumnSet = new ColumnSet("name", "returnedtypecode", "fetchxml", "layoutxml") };
            v.Criteria.AddCondition("returnedtypecode", ConditionOperator.In, ents);
            foreach (var r in Util.RetrieveAll(svc, v, ct)) s.AddDoc(r.GetAttributeValue<string>("returnedtypecode"), "View", r.GetAttributeValue<string>("name"), r.GetAttributeValue<string>("fetchxml"), r.GetAttributeValue<string>("layoutxml"));

            var c = new QueryExpression("savedqueryvisualization") { ColumnSet = new ColumnSet("name", "primaryentitytypecode", "datadescription", "presentationdescription") };
            c.Criteria.AddCondition("primaryentitytypecode", ConditionOperator.In, ents);
            foreach (var r in Util.RetrieveAll(svc, c, ct)) s.AddDoc(r.GetAttributeValue<string>("primaryentitytypecode"), "Chart", r.GetAttributeValue<string>("name"), r.GetAttributeValue<string>("datadescription"), r.GetAttributeValue<string>("presentationdescription"));

            var w = new QueryExpression("workflow") { ColumnSet = new ColumnSet("name", "primaryentity", "xaml", "category") };
            w.Criteria.AddCondition("type", ConditionOperator.Equal, 1);
            w.Criteria.AddCondition("category", ConditionOperator.In, 0, 2); // 0 = classic workflow, 2 = business rule
            foreach (var r in Util.RetrieveAll(svc, w, ct)) s.AddDoc(r.GetAttributeValue<string>("primaryentity"), r.GetAttributeValue<OptionSetValue>("category")?.Value == 2 ? "Business rule" : "Workflow", r.GetAttributeValue<string>("name"), r.GetAttributeValue<string>("xaml"));

            var p = new QueryExpression("sdkmessageprocessingstep") { ColumnSet = new ColumnSet("name", "filteringattributes") };
            p.Criteria.AddCondition("filteringattributes", ConditionOperator.NotNull);
            foreach (var r in Util.RetrieveAll(svc, p, ct)) s.AddDoc(null, "Plug-in step", r.GetAttributeValue<string>("name"), r.GetAttributeValue<string>("filteringattributes"));
            return s;
        }

        public List<string> Find(string entity, string field)
        {
            var rx = new Regex("(?<![A-Za-z0-9_])" + Regex.Escape(field) + "(?![A-Za-z0-9_])", RegexOptions.IgnoreCase);
            var docs = (byEntity.TryGetValue(entity, out var l) ? l : new List<Doc>()).Concat(anyEntity);
            return docs.Where(d => rx.IsMatch(d.Text)).Select(d => $"{d.Kind}: {d.Name}").Distinct().ToList();
        }
    }

    public static class DeleteScriptBuilder
    {
        /// <summary>Generates a PowerShell script with every DELETE commented out. Nothing is executed by the plugin.</summary>
        public static string Build(DataTable results)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Generated by Solution Analyzer. REVIEW EVERY LINE. Back up / export solutions first.");
            sb.AppendLine("# All delete commands are commented out on purpose. Uncomment one at a time after verifying.");
            sb.AppendLine("$org   = 'https://YOURORG.crm.dynamics.com'");
            sb.AppendLine("$token = '<bearer token>'   # e.g. from: az account get-access-token --resource $org");
            sb.AppendLine("$h = @{ Authorization = \"Bearer $token\"; 'Content-Type' = 'application/json' }");
            sb.AppendLine();
            foreach (DataRow r in results.Rows)
            {
                if ((string)r["Status"] != "SAFE TO DELETE") continue;
                sb.AppendLine($"# {r["Table"]}.{r["Field"]}  ({r["Display name"]})");
                sb.AppendLine($"# Invoke-RestMethod -Method Delete -Headers $h -Uri \"$org/api/data/v9.2/EntityDefinitions(LogicalName='{r["Table"]}')/Attributes(LogicalName='{r["Field"]}')\"");
            }
            return sb.ToString();
        }
    }
}
