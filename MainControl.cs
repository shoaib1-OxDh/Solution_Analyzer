using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using SolutionAnalyzerFieldHealthChecker.Models;
using SolutionAnalyzerFieldHealthChecker.Services;
using XrmToolBox.Extensibility;

namespace SolutionAnalyzerFieldHealthChecker
{
    public class MainControl : PluginControlBase
    {
        class SolItem { public Guid Id; public string Text; public override string ToString() => Text; }

        MetadataCache cache;
        CancellationTokenSource cts;

        CheckedListBox clbSolutions;
        CheckBox chkCustomOnly, chkSystemFields, chkEnableScript;
        NumericUpDown nudFuzzy, nudOptSet, nudUtil, nudSample;
        Button btnCancel;
        TabControl tabs;
        TextBox txtLog, txtFilter;
        readonly string[] titles = { "1. Similar fields", "2. Option sets", "3. Field length", "4. Unused fields" };
        readonly DataGridView[] grids = new DataGridView[4];
        readonly DataTable[] results = new DataTable[4];

        public MainControl()
        {
            Dock = DockStyle.Fill;
            BuildUi();
            ConnectionUpdated += (sender, e) => { cache = null; clbSolutions.Items.Clear(); };
        }

        // ---------------- UI ----------------
        void BuildUi()
        {
            var split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1, SplitterDistance = 300 };
            Controls.Add(split);

            var left = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(6) };
            split.Panel1.Controls.Add(left);

            left.Controls.Add(Btn("Load metadata && solutions", (s, e) => ExecuteMethod(LoadAll)));
            left.Controls.Add(Lbl("Scope (none ticked = whole environment):"));
            clbSolutions = new CheckedListBox { Width = 270, Height = 130, CheckOnClick = true };
            left.Controls.Add(clbSolutions);
            chkCustomOnly = new CheckBox { Text = "Custom tables only", Checked = true, AutoSize = true };
            chkSystemFields = new CheckBox { Text = "Include system (non-custom) fields", AutoSize = true };
            left.Controls.Add(chkCustomOnly); left.Controls.Add(chkSystemFields);

            nudFuzzy = Nud(0.5m, 1m, 0.85m, 2, 0.05m);
            nudOptSet = Nud(0.5m, 1m, 0.80m, 2, 0.05m);
            nudUtil = Nud(1, 100, 50, 0, 5);
            nudSample = Nud(0, 5000000, 50000, 0, 10000);
            left.Controls.Add(Lbl("Field-name similarity threshold:")); left.Controls.Add(nudFuzzy);
            left.Controls.Add(Lbl("Option-set similarity threshold:")); left.Controls.Add(nudOptSet);
            left.Controls.Add(Lbl("Low length utilisation below (%):")); left.Controls.Add(nudUtil);
            left.Controls.Add(Lbl("Length analysis: max records/table (0 = all):")); left.Controls.Add(nudSample);

            left.Controls.Add(Btn("Run 1: Similar fields + fill %", (s, e) => ExecuteMethod(() => Run(0))));
            left.Controls.Add(Btn("Run 2: Duplicate option sets", (s, e) => ExecuteMethod(() => Run(1))));
            left.Controls.Add(Btn("Run 3: Field length analyzer", (s, e) => ExecuteMethod(() => Run(2))));
            left.Controls.Add(Btn("Run 4: Unused fields + dependencies", (s, e) => ExecuteMethod(() => Run(3))));
            btnCancel = Btn("Cancel", (s, e) => cts?.Cancel()); btnCancel.Enabled = false;
            left.Controls.Add(btnCancel);
            left.Controls.Add(Btn("Export current tab to CSV", (s, e) => Export()));
            chkEnableScript = new CheckBox { Text = "Enable delete-script generation", AutoSize = true };
            left.Controls.Add(chkEnableScript);
            left.Controls.Add(Btn("Generate delete script (not executed)", (s, e) => GenerateScript()));

            var right = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 450 };
            split.Panel2.Controls.Add(right);

            var top = new Panel { Dock = DockStyle.Fill };
            txtFilter = new TextBox { Dock = DockStyle.Top };
            txtFilter.TextChanged += (s, e) => ApplyFilter();
            tabs = new TabControl { Dock = DockStyle.Fill };
            for (int i = 0; i < 4; i++)
            {
                var pg = new TabPage(titles[i]);
                grids[i] = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells };
                pg.Controls.Add(grids[i]); tabs.TabPages.Add(pg);
            }
            top.Controls.Add(tabs); top.Controls.Add(txtFilter);
            right.Panel1.Controls.Add(top);

            txtLog = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
            right.Panel2.Controls.Add(txtLog);
        }

        static Button Btn(string t, EventHandler h) { var b = new Button { Text = t, Width = 270, Height = 28 }; b.Click += h; return b; }
        static System.Windows.Forms.Label Lbl(string t) => new System.Windows.Forms.Label { Text = t, AutoSize = true, Margin = new Padding(3, 8, 3, 0) };
        static NumericUpDown Nud(decimal min, decimal max, decimal val, int dp, decimal inc) =>
            new NumericUpDown { Minimum = min, Maximum = max, Value = val, DecimalPlaces = dp, Increment = inc, Width = 270 };

        void Log(string s)
        {
            if (InvokeRequired) { BeginInvoke(new Action<string>(Log), s); return; }
            txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {s}{Environment.NewLine}");
        }

        // ---------------- loading ----------------
        void LoadAll()
        {
            cts = new CancellationTokenSource();
            WorkAsync(new WorkAsyncInfo
            {
                Message = "Loading solutions and metadata...",
                Work = (w, a) =>
                {
                    var q = new QueryExpression("solution") { ColumnSet = new ColumnSet("friendlyname", "uniquename") };
                    q.Criteria.AddCondition("isvisible", ConditionOperator.Equal, true);
                    q.Orders.Add(new OrderExpression("friendlyname", OrderType.Ascending));
                    var sols = Util.RetrieveAll(Service, q, cts.Token)
                                   .Select(e => new SolItem { Id = e.Id, Text = $"{e.GetAttributeValue<string>("friendlyname")} ({e.GetAttributeValue<string>("uniquename")})" }).ToList();
                    var md = MetadataCache.Load(Service, Log, cts.Token);
                    a.Result = Tuple.Create(sols, md);
                },
                PostWorkCallBack = a =>
                {
                    if (a.Error != null) { MessageBox.Show(a.Error.Message, "Error"); return; }
                    var t = (Tuple<List<SolItem>, MetadataCache>)a.Result;
                    cache = t.Item2;
                    clbSolutions.Items.Clear();
                    foreach (var s in t.Item1) clbSolutions.Items.Add(s);
                }
            });
        }

        // ---------------- running reports ----------------
        AnalyzerOptions ReadOptions() => new AnalyzerOptions
        {
            SolutionIds = clbSolutions.CheckedItems.Cast<SolItem>().Select(s => s.Id).ToList(),
            CustomEntitiesOnly = chkCustomOnly.Checked,
            IncludeSystemFields = chkSystemFields.Checked,
            FuzzyThreshold = (double)nudFuzzy.Value,
            OptionSetThreshold = (double)nudOptSet.Value,
            LengthUtilThreshold = (double)nudUtil.Value,
            SampleLimit = (int)nudSample.Value
        };

        void Run(int idx)
        {
            var opts = ReadOptions();
            cts = new CancellationTokenSource();
            var ct = cts.Token;
            btnCancel.Enabled = true;

            WorkAsync(new WorkAsyncInfo
            {
                Message = "Running " + titles[idx] + "...",
                Work = (w, a) =>
                {
                    if (cache == null) cache = MetadataCache.Load(Service, Log, ct);
                    var scope = new Scope(Service, opts, ct);
                    switch (idx)
                    {
                        case 0: a.Result = SimilarFieldsService.Run(Service, cache, scope, opts, Log, ct); break;
                        case 1: a.Result = OptionSetService.Run(Service, cache, scope, opts, Log, ct); break;
                        case 2: a.Result = FieldLengthService.Run(Service, cache, scope, opts, Log, ct); break;
                        case 3: a.Result = UnusedFieldsService.Run(Service, cache, scope, opts, Log, ct); break;
                    }
                },
                PostWorkCallBack = a =>
                {
                    btnCancel.Enabled = false;
                    if (a.Error is OperationCanceledException) { Log("Cancelled."); return; }
                    if (a.Error != null) { Log("ERROR: " + a.Error.Message); MessageBox.Show(a.Error.Message, "Error"); return; }
                    results[idx] = (DataTable)a.Result;
                    grids[idx].DataSource = results[idx];
                    tabs.SelectedIndex = idx;
                    Log($"{titles[idx]} finished: {results[idx].Rows.Count} row(s).");
                }
            });
        }

        // ---------------- filter / export / script ----------------
        void ApplyFilter()
        {
            var dt = results[tabs.SelectedIndex];
            if (dt == null) return;
            var t = txtFilter.Text.Replace("'", "''").Replace("[", "[[]").Replace("%", "[%]");
            dt.DefaultView.RowFilter = string.IsNullOrWhiteSpace(t) ? "" :
                string.Join(" OR ", dt.Columns.Cast<DataColumn>().Select(c => $"Convert([{c.ColumnName}], 'System.String') LIKE '%{t}%'"));
        }

        void Export()
        {
            var dt = results[tabs.SelectedIndex];
            if (dt == null) { MessageBox.Show("Run the report first."); return; }
            using (var d = new SaveFileDialog { Filter = "CSV|*.csv", FileName = titles[tabs.SelectedIndex].Replace(". ", "_").Replace(" ", "_") + ".csv" })
                if (d.ShowDialog() == DialogResult.OK) { Util.ToCsv(dt, d.FileName); Log("Exported " + d.FileName); }
        }

        void GenerateScript()
        {
            if (!chkEnableScript.Checked) { MessageBox.Show("Tick 'Enable delete-script generation' first."); return; }
            if (results[3] == null) { MessageBox.Show("Run report 4 first."); return; }
            using (var d = new SaveFileDialog { Filter = "PowerShell|*.ps1", FileName = "delete-unused-fields.ps1" })
                if (d.ShowDialog() == DialogResult.OK) { File.WriteAllText(d.FileName, DeleteScriptBuilder.Build(results[3])); Log("Script saved (all commands commented out): " + d.FileName); }
        }
    }
}