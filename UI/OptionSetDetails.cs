using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using SolutionAnalyzerFieldHealthChecker.Services;

namespace SolutionAnalyzerFieldHealthChecker.UI
{
    /// <summary>
    /// Report 2 details: the options of the selected field's option set side by side with the option set(s)
    /// it was matched with. Rows = option labels, columns = option sets, cells = option value (or "-" if missing).
    /// </summary>
    public class OptionSetDetails : Panel
    {
        const string Missing = "-";
        readonly System.Windows.Forms.Label title;
        readonly DataGridView grid;

        public OptionSetDetails()
        {
            Dock = DockStyle.Fill;
            BackColor = Color.White;
            grid = new DataGridView
            {
                Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false,
                RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, BackgroundColor = Color.White,
                BorderStyle = BorderStyle.None, CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal, GridColor = Theme.GridLine,
                EnableHeadersVisualStyles = false, ColumnHeadersHeight = 30, ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells
            };
            grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Theme.BrandSoft, ForeColor = Theme.Brand, Font = Theme.Bold, SelectionBackColor = Theme.BrandSoft, SelectionForeColor = Theme.Brand
            };
            grid.DefaultCellStyle = new DataGridViewCellStyle { Font = Theme.Base, ForeColor = Theme.Text, SelectionBackColor = Theme.Selection, SelectionForeColor = Theme.Text };
            grid.RowTemplate.Height = 24;
            grid.CellFormatting += FormatCell;

            title = new System.Windows.Forms.Label
            {
                Dock = DockStyle.Top, Height = 26, Font = Theme.Bold, ForeColor = Theme.Brand, BackColor = Theme.PanelBack,
                TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(6, 0, 0, 0), UseMnemonic = false, AutoEllipsis = true,
                Text = "Click a row above to see and compare its option set values."
            };
            Controls.Add(grid);
            Controls.Add(title);
        }

        public void Clear()
        {
            grid.DataSource = null;
            title.Text = "Click a row above to see and compare its option set values.";
        }

        /// <summary>Shows the options for one report-2 row (columns Table, Field, Option set, Match with).</summary>
        public void ShowRow(MetadataCache cache, DataRowView row)
        {
            if (cache == null || row == null) { Clear(); return; }
            string table = Convert.ToString(row["Table"]), field = Convert.ToString(row["Field"]), match = Convert.ToString(row["Match with"]);

            var sets = new List<KeyValuePair<string, List<KeyValuePair<int?, string>>>>();
            var own = FieldOptions(cache, table, field);
            if (own != null) sets.Add(Set($"{table}.{field}", own));

            foreach (var m in (match ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()))
            {
                List<KeyValuePair<int?, string>> opts;
                if (m.Contains("."))   // other table: entity.field
                {
                    var p = m.Split('.');
                    opts = FieldOptions(cache, p[0], p[1]);
                    if (opts != null) sets.Add(Set(m, opts));
                }
                else if ((opts = GlobalOptions(cache, m)) != null) sets.Add(Set($"{m} (global)", opts));
                else if ((opts = FieldOptions(cache, table, m)) != null) sets.Add(Set($"{table}.{m}", opts));   // same table
            }

            if (sets.Count == 0) { Clear(); title.Text = $"No option values found for {table}.{field}."; return; }

            // rows = every label (first set's order first), columns = option sets, cells = value or "-"
            var dt = new DataTable();
            dt.Columns.Add("Option label");
            foreach (var s in sets) dt.Columns.Add(UniqueName(dt, s.Key));
            var labels = sets.SelectMany(s => s.Value.Select(o => o.Value)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            int diffs = 0;
            foreach (var label in labels)
            {
                var r = dt.NewRow();
                r[0] = label;
                bool missing = false;
                for (int c = 0; c < sets.Count; c++)
                {
                    var hit = sets[c].Value.FirstOrDefault(o => string.Equals(o.Value, label, StringComparison.OrdinalIgnoreCase));
                    bool found = hit.Value != null;
                    r[c + 1] = found ? Convert.ToString(hit.Key) : Missing;
                    missing |= !found;
                }
                if (missing) diffs++;
                dt.Rows.Add(r);
            }

            grid.DataSource = dt;
            foreach (DataGridViewColumn col in grid.Columns) col.SortMode = DataGridViewColumnSortMode.NotSortable;
            string summary = sets.Count == 1 ? $"{labels.Count} option(s)"
                           : diffs == 0 ? "✔ same labels in all option sets"
                           : $"{diffs} label(s) not in every option set (highlighted)";
            title.Text = $"Option values of {table}.{field}" + (sets.Count > 1 ? $" compared with {sets.Count - 1} other option set(s)" : "") + $"  ·  {summary}  ·  cell = option value";
        }

        void FormatCell(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0) return;
            var row = grid.Rows[e.RowIndex];
            bool rowHasGap = row.Cells.Cast<DataGridViewCell>().Skip(1).Any(c => Convert.ToString(c.Value) == Missing);
            if (rowHasGap) e.CellStyle.BackColor = Theme.Back(Severity.Review);
            if (e.ColumnIndex > 0 && Convert.ToString(e.Value) == Missing)
            {
                e.CellStyle.ForeColor = Theme.Fore(Severity.Cleanup);
                e.CellStyle.Font = Theme.Bold;
            }
            if (e.ColumnIndex == 0) e.CellStyle.Font = Theme.Bold;
        }

        static KeyValuePair<string, List<KeyValuePair<int?, string>>> Set(string name, List<KeyValuePair<int?, string>> opts) =>
            new KeyValuePair<string, List<KeyValuePair<int?, string>>>(name, opts);

        static string UniqueName(DataTable dt, string name)
        {
            var n = name; int k = 2;
            while (dt.Columns.Contains(n)) n = $"{name} ({k++})";
            return n;
        }

        static List<KeyValuePair<int?, string>> FieldOptions(MetadataCache cache, string table, string field)
        {
            var a = cache.Entities.FirstOrDefault(e => e.LogicalName == table)?.Attributes?.FirstOrDefault(x => x.LogicalName == field);
            if (a is EnumAttributeMetadata ea && ea.OptionSet != null) return Options(ea.OptionSet);
            if (a is BooleanAttributeMetadata ba && ba.OptionSet != null) return Options(ba.OptionSet);
            return null;
        }

        static List<KeyValuePair<int?, string>> GlobalOptions(MetadataCache cache, string name)
        {
            var g = cache.GlobalOptionSets.FirstOrDefault(o => string.Equals(o.Name, name, StringComparison.OrdinalIgnoreCase));
            return g == null ? null : Options(g);
        }

        static List<KeyValuePair<int?, string>> Options(OptionSetMetadataBase os)
        {
            var list = new List<KeyValuePair<int?, string>>();
            if (os is OptionSetMetadata m)
                foreach (var o in m.Options) list.Add(new KeyValuePair<int?, string>(o.Value, LabelText(o.Label)));
            else if (os is BooleanOptionSetMetadata b)
            {
                if (b.TrueOption != null) list.Add(new KeyValuePair<int?, string>(b.TrueOption.Value, LabelText(b.TrueOption.Label)));
                if (b.FalseOption != null) list.Add(new KeyValuePair<int?, string>(b.FalseOption.Value, LabelText(b.FalseOption.Label)));
            }
            return list.Where(o => o.Value.Length > 0).ToList();
        }

        static string LabelText(Microsoft.Xrm.Sdk.Label l) => (l?.UserLocalizedLabel?.Label ?? l?.LocalizedLabels?.FirstOrDefault()?.Label ?? "").Trim();
    }
}
