using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using SolutionAnalyzer.Models;

namespace SolutionAnalyzer.Services
{
    public class MetadataCache
    {
        public List<EntityMetadata> Entities = new List<EntityMetadata>();
        public List<OptionSetMetadata> GlobalOptionSets = new List<OptionSetMetadata>();

        public static MetadataCache Load(IOrganizationService svc, Action<string> log, CancellationToken ct)
        {
            var c = new MetadataCache();
            log("Loading table + column metadata (this can take a minute)...");
            var resp = (RetrieveAllEntitiesResponse)Util.WithRetry(() => svc.Execute(new RetrieveAllEntitiesRequest
            {
                EntityFilters = EntityFilters.Entity | EntityFilters.Attributes,
                RetrieveAsIfPublished = true
            }), ct);
            c.Entities = resp.EntityMetadata.ToList();

            log("Loading global option sets...");
            var os = (RetrieveAllOptionSetsResponse)Util.WithRetry(() => svc.Execute(new RetrieveAllOptionSetsRequest { RetrieveAsIfPublished = true }), ct);
            c.GlobalOptionSets = os.OptionSetMetadata.OfType<OptionSetMetadata>().Where(o => o.IsGlobal == true).ToList();
            log($"Loaded {c.Entities.Count} tables, {c.GlobalOptionSets.Count} global option sets.");
            return c;
        }
    }

    /// <summary>Decides which tables/columns are analysed (solution filter, custom-only, system fields).</summary>
    public class Scope
    {
        readonly AnalyzerOptions o;
        readonly HashSet<Guid> entIds = new HashSet<Guid>();
        readonly HashSet<Guid> attrIds = new HashSet<Guid>();
        readonly bool filtered;

        public Scope(IOrganizationService svc, AnalyzerOptions o, CancellationToken ct)
        {
            this.o = o;
            if (o.SolutionIds.Count == 0) return;
            filtered = true;
            var q = new QueryExpression("solutioncomponent") { ColumnSet = new ColumnSet("objectid", "componenttype") };
            q.Criteria.AddCondition("solutionid", ConditionOperator.In, o.SolutionIds.Cast<object>().ToArray());
            q.Criteria.AddCondition("componenttype", ConditionOperator.In, 1, 2); // 1 = table, 2 = column
            foreach (var r in Util.RetrieveAll(svc, q, ct))
            {
                var id = r.GetAttributeValue<Guid>("objectid");
                if (r.GetAttributeValue<OptionSetValue>("componenttype").Value == 1) entIds.Add(id); else attrIds.Add(id);
            }
        }

        public bool Entity(EntityMetadata e)
        {
            if (e.IsIntersect == true) return false;
            if (o.CustomEntitiesOnly && e.IsCustomEntity != true) return false;
            if (filtered && !entIds.Contains(e.MetadataId ?? Guid.Empty) &&
                !e.Attributes.Any(a => attrIds.Contains(a.MetadataId ?? Guid.Empty))) return false;
            return true;
        }

        public bool Attr(EntityMetadata e, AttributeMetadata a)
        {
            if (a.AttributeOf != null) return false;
            if (!o.IncludeSystemFields && a.IsCustomAttribute != true) return false;
            if (filtered && !entIds.Contains(e.MetadataId ?? Guid.Empty) && !attrIds.Contains(a.MetadataId ?? Guid.Empty)) return false;
            return true;
        }
    }
}
