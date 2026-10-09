using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace SolutionAnalyzerFieldHealthChecker.UI
{
    /// <summary>Maps a status value (matched by prefix) to a friendly label and severity.</summary>
    public class StatusRule
    {
        public string Prefix, Label;
        public Severity Severity;
        public StatusRule(string prefix, string label, Severity severity) { Prefix = prefix; Label = label; Severity = severity; }
    }

    /// <summary>Plain-English description of one report and how to colour its results.</summary>
    public class ReportDef
    {
        public string Number, Name, RunHint, Summary, HowToRead, StatusColumn;
        public Color Accent;
        public StatusRule[] Rules;

        public string TabText => $"{Number}  {Name}";
        public string RunText => $"▶   {Number}  ·  {Name}";

        public StatusRule Match(string value) =>
            Rules.FirstOrDefault(r => (value ?? "").StartsWith(r.Prefix, StringComparison.OrdinalIgnoreCase));
    }

    public static class Reports
    {
        public static readonly ReportDef[] All =
        {
            new ReportDef
            {
                Number = "1", Name = "Similar fields + fill %", Accent = Color.FromArgb(0, 120, 130), StatusColumn = "Flag",
                RunHint = "Look-alike columns and how much data each one holds.",
                Summary = "Finds columns on the same table that look like copies of each other (for example msemr_subject vs xyz_subject) and shows how many records actually use each one.",
                HowToRead = "Rows with the same Group key are one set of look-alike fields.  Merge candidate = more than one field in the set holds data, consider keeping just one.  Empty = never filled in, candidate for removal.  Fill % = share of records that have a value.",
                Rules = new[]
                {
                    new StatusRule("Empty", "Empty - delete candidate", Severity.Cleanup),
                    new StatusRule("Merge", "Merge candidate", Severity.Review),
                    new StatusRule("OK", "OK", Severity.Healthy),
                }
            },
            new ReportDef
            {
                Number = "2", Name = "Duplicate option sets", Accent = Color.FromArgb(107, 79, 160), StatusColumn = "Check",
                RunHint = "Choice fields that repeat each other or a global option set.",
                Summary = "Finds choice (option set) fields that repeat each other: the same name twice on one table, local option sets with nearly identical labels on different tables, and local sets that copy an existing global option set.",
                HowToRead = "Similarity % = share of option labels the two sets have in common.  Matches global = switch the field to that global option set so the values stay in sync.  Similar local = consider one shared global option set for both fields.",
                Rules = new[]
                {
                    new StatusRule("Same name", "Same name on table", Severity.Review),
                    new StatusRule("Similar local", "Similar local option sets", Severity.Suggestion),
                    new StatusRule("Matches global", "Matches a global option set", Severity.Suggestion),
                }
            },
            new ReportDef
            {
                Number = "3", Name = "Field length usage", Accent = Color.FromArgb(196, 89, 17), StatusColumn = "Recommendation",
                RunHint = "Text columns sized far bigger than the data stored in them.",
                Summary = "Compares each text column's configured maximum length with the longest value actually stored in it.",
                HowToRead = "Utilisation % = longest value used ÷ configured max.  Over-provisioned = real data uses less than your threshold, size similar new columns smaller.  P95 = 95% of values are this long or shorter.  Note: Dataverse cannot shrink an existing column, so treat this as a design review.",
                Rules = new[]
                {
                    new StatusRule("Over", "Over-provisioned", Severity.Review),
                    new StatusRule("No data", "No data yet", Severity.NoData),
                    new StatusRule("OK", "OK", Severity.Healthy),
                }
            },
            new ReportDef
            {
                Number = "4", Name = "Unused fields", Accent = Color.FromArgb(0, 99, 177), StatusColumn = "Status",
                RunHint = "Empty custom columns, with dependency and reference checks.",
                Summary = "Lists custom columns that hold no data in any record, then checks their dependencies and whether forms, views, charts, workflows, business rules or plug-in steps mention them.",
                HowToRead = "Safe to delete = empty, no dependencies and no references found.  Review = empty, but something still points at it (see Review notes, Dependency details and Possible references).  JavaScript web resources, Power Automate flows, canvas apps and PCF controls are NOT scanned, check those yourself.",
                Rules = new[]
                {
                    new StatusRule("SAFE", "Safe to delete", Severity.Cleanup),
                    new StatusRule("REVIEW", "Review before deleting", Severity.Review),
                }
            },
        };

        /// <summary>Header tooltips explaining the less obvious columns.</summary>
        public static readonly Dictionary<string, string> ColumnTips = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Group key", "Field name without the publisher prefix. Rows with the same key are look-alike fields." },
            { "Total records", "Number of records in the table." },
            { "Records with data", "Number of records where this field has a value." },
            { "Fill %", "Share of records that have a value in this field." },
            { "Flag", "What to do with this field." },
            { "Check", "Which kind of duplication was found." },
            { "Is global", "Yes = uses a shared (global) option set." },
            { "Options", "Number of choices in the option set." },
            { "Match with", "The other field or global option set it resembles." },
            { "Similarity %", "Share of option labels the two option sets have in common." },
            { "Configured max", "Maximum length configured on the column." },
            { "Actual max used", "Longest value currently stored." },
            { "Avg used", "Average length of the non-empty values." },
            { "P95 used", "95% of values are this long or shorter." },
            { "Utilisation %", "Actual max used ÷ configured max." },
            { "Records analysed", "Records read for this table (limited by 'max records per table')." },
            { "Non-empty", "Analysed records that had a value." },
            { "Managed", "Managed columns can only be removed by uninstalling their solution." },
            { "Review notes", "Reasons to double-check before deleting." },
            { "Dependencies", "Number of solution components that depend on this column." },
            { "Dependency details", "The components that depend on this column." },
            { "Possible references", "Forms, views, charts, workflows, business rules or plug-in steps whose definition mentions this column." },
            { "Status", "Safe to delete or Review." },
            { "Recommendation", "What the result means for this field." },
        };
    }
}
