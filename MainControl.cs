using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using SolutionAnalyzerFieldHealthChecker.Models;
using SolutionAnalyzerFieldHealthChecker.Services;
using SolutionAnalyzerFieldHealthChecker.UI;
using XrmToolBox.Extensibility;

namespace SolutionAnalyzerFieldHealthChecker
{
    public class MainControl : PluginControlBase
    {
        class SolItem { public Guid Id; public string Text; public override string ToString() => Text; }

        const int LeftWidth = 290;

        MetadataCache cache;
        CancellationTokenSource cts;

        SplitContainer mainSplit, rightSplit;
        CheckedListBox clbSolutions;
        CheckBox chkCustomOnly, chkSystemFields, chkEnableScript;
        NumericUpDown nudFuzzy, nudOptSet, nudUtil, nudSample;
        Button btnCancel;
        System.Windows.Forms.Label lblLoadStatus;
        TabControl tabs;
        TextBox txtLog;
        readonly ToolTip tip = new ToolTip { AutoPopDelay = 20000, InitialDelay = 300 };

        static readonly ReportDef[] defs = Reports.All;
        readonly DataGridView[] grids = new DataGridView[4];
        readonly DataTable[] results = new DataTable[4];
        readonly TextBox[] searches = new TextBox[4];
        readonly FlowLayoutPanel[] cardPanels = new FlowLayoutPanel[4];
        readonly System.Windows.Forms.Label[] counts = new System.Windows.Forms.Label[4];
        readonly System.Windows.Forms.Label[] emptyStates = new System.Windows.Forms.Label[4];
        readonly string[] cardFilters = new string[4];
        readonly bool[] columnsReady = new bool[4];

        public MainControl()
        {
            Dock = DockStyle.Fill;
            Font = Theme.Base;
            BackColor = Color.White;
            BuildUi();
            Load += (s, e) => SetSplitters();
            ConnectionUpdated += (sender, e) =>
            {
                cache = null;
                clbSolutions.Items.Clear();
                SetLoadStatus("Not loaded yet. Click the button above (the reports also load it automatically).", Theme.Muted);
            };
        }

        // ---------------- UI ----------------
        void BuildUi()
        {
            mainSplit = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1, SplitterWidth = 6, BackColor = Theme.GridLine };
            mainSplit.Panel1.BackColor = Theme.PanelBack;
            mainSplit.Panel2.BackColor = Color.White;
            Controls.Add(mainSplit);
            Controls.Add(BuildBanner());

            mainSplit.Panel1.Controls.Add(BuildLeftPanel());

            rightSplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, FixedPanel = FixedPanel.Panel2, SplitterWidth = 6, BackColor = Theme.GridLine };
            rightSplit.Panel1.BackColor = Color.White;
            mainSplit.Panel2.Controls.Add(rightSplit);

            tabs = new TabControl
            {
                Dock = DockStyle.Fill, DrawMode = TabDrawMode.OwnerDrawFixed, SizeMode = TabSizeMode.Fixed,
                ItemSize = new Size(210, 32), Padding = new Point(12, 4)
            };
            tabs.DrawItem += DrawTab;
            tabs.SelectedIndexChanged += (s, e) => tabs.Invalidate();
            for (int i = 0; i < defs.Length; i++) tabs.TabPages.Add(BuildReportPage(i));
            rightSplit.Panel1.Controls.Add(tabs);

            rightSplit.Panel2.Controls.Add(BuildLogPanel());
        }

        Control BuildBanner()
        {
            var banner = new Panel { Dock = DockStyle.Top, Height = 58, BackColor = Theme.Brand, Padding = new Padding(14, 6, 14, 6) };
            var sub = new System.Windows.Forms.Label
            {
                Dock = DockStyle.Top, AutoSize = false, Height = 20, ForeColor = Color.FromArgb(214, 228, 242), Font = Theme.Base,
                Text = "Read-only health check of your Dataverse data model. Nothing in your environment is changed."
            };
            var title = new System.Windows.Forms.Label
            {
                Dock = DockStyle.Top, AutoSize = false, Height = 28, ForeColor = Color.White, Font = Theme.Header,
                Text = "Solution Analyzer & Field Health Checker"
            };
            banner.Controls.Add(sub);
            banner.Controls.Add(title);
            return banner;
        }

        Control BuildLeftPanel()
        {
            var left = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true,
                Padding = new Padding(10, 4, 10, 10), BackColor = Theme.PanelBack
            };

            left.Controls.Add(Section("1", "Load your environment"));
            left.Controls.Add(ColorButton("Load metadata && solutions", Theme.Brand, (s, e) => ExecuteMethod(LoadAll)));
            lblLoadStatus = Hint("Not loaded yet. Click the button above (the reports also load it automatically).");
            left.Controls.Add(lblLoadStatus);

            left.Controls.Add(Section("2", "Choose what to analyse"));
            left.Controls.Add(Hint("Tick solutions to limit the scope. Leave all unticked to scan the whole environment."));
            clbSolutions = new CheckedListBox { Width = LeftWidth, Height = 140, CheckOnClick = true, BorderStyle = BorderStyle.FixedSingle, IntegralHeight = false };
            left.Controls.Add(clbSolutions);
            chkCustomOnly = Check("Custom tables only", true, "Skip out-of-the-box Microsoft tables.");
            chkSystemFields = Check("Include system (non-custom) fields", false, "Also analyse standard columns, not just custom ones.");
            left.Controls.Add(chkCustomOnly);
            left.Controls.Add(chkSystemFields);

            left.Controls.Add(Section("3", "Fine-tune (optional)"));
            nudFuzzy = Nud(0.5m, 1m, 0.85m, 2, 0.05m);
            nudOptSet = Nud(0.5m, 1m, 0.80m, 2, 0.05m);
            nudUtil = Nud(1, 100, 50, 0, 5);
            nudSample = Nud(0, 5000000, 50000, 0, 10000);
            AddSetting(left, "Field-name match strictness (report 1)", nudFuzzy, "0.50 - 1.00. Higher = only very similar names are grouped.");
            AddSetting(left, "Option-set match strictness (report 2)", nudOptSet, "Share of identical labels needed to call two option sets similar.");
            AddSetting(left, "Flag text fields using less than (%) (report 3)", nudUtil, "Fields whose longest value uses less than this % of the max length are flagged.");
            AddSetting(left, "Max records read per table (report 3)", nudSample, "0 = read everything. Lower is faster on big tables.");

            left.Controls.Add(Section("4", "Run a report"));
            for (int i = 0; i < defs.Length; i++)
            {
                int idx = i;
                var b = ColorButton(defs[i].RunText, defs[i].Accent, (s, e) => ExecuteMethod(() => Run(idx)));
                b.TextAlign = ContentAlignment.MiddleLeft;
                tip.SetToolTip(b, defs[i].Summary);
                left.Controls.Add(b);
                var h = Hint(defs[i].RunHint);
                h.Margin = new Padding(3, 0, 3, 6);
                left.Controls.Add(h);
            }
            btnCancel = ColorButton("■   Cancel running report", Theme.Danger, (s, e) => cts?.Cancel());
            btnCancel.Enabled = false;
            left.Controls.Add(btnCancel);

            left.Controls.Add(Section("5", "Export & clean-up"));
            var export = ColorButton("Export current view to CSV (Excel)", Color.FromArgb(16, 124, 16), (s, e) => Export());
            tip.SetToolTip(export, "Saves the rows currently shown on the selected tab, including any search or card filter.");
            left.Controls.Add(export);
            chkEnableScript = Check("Enable delete-script generation", false, "Safety switch for the button below.");
            left.Controls.Add(chkEnableScript);
            var script = ColorButton("Generate delete script (not executed)", Color.FromArgb(121, 119, 117), (s, e) => GenerateScript());
            tip.SetToolTip(script, "Writes a PowerShell file for the 'Safe to delete' rows of report 4. Every command is commented out; nothing is deleted.");
            left.Controls.Add(script);

            left.Controls.Add(Section("", "Colour key"));
            foreach (Severity sev in Enum.GetValues(typeof(Severity)))
                left.Controls.Add(LegendChip(sev));

            return left;
        }

        TabPage BuildReportPage(int i)
        {
            var d = defs[i];
            var page = new TabPage(d.TabText) { BackColor = Color.White, Padding = new Padding(0) };

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, BackColor = Color.White, Padding = new Padding(12, 8, 12, 6) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            page.Controls.Add(layout);

            // what this report is and how to read it
            var info = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0) };
            var title = new System.Windows.Forms.Label { Text = d.Name, Font = Theme.Title, ForeColor = d.Accent, AutoSize = true, Margin = new Padding(0, 0, 0, 2) };
            var summary = new System.Windows.Forms.Label { Text = d.Summary, AutoSize = true, MaximumSize = new Size(900, 0), ForeColor = Theme.Text, Margin = new Padding(0, 0, 0, 4) };
            var how = new System.Windows.Forms.Label
            {
                Text = "How to read it:  " + d.HowToRead, AutoSize = true, ForeColor = Theme.Text, BackColor = Theme.BrandSoft,
                Padding = new Padding(8, 6, 8, 6), Margin = new Padding(0, 0, 0, 6), MaximumSize = new Size(900, 0)
            };
            info.Controls.Add(title); info.Controls.Add(summary); info.Controls.Add(how);
            layout.Controls.Add(info, 0, 0);
            page.Resize += (s, e) =>
            {
                int w = Math.Max(200, page.ClientSize.Width - 30);
                summary.MaximumSize = new Size(w, 0);
                how.MaximumSize = new Size(w, 0);
            };

            // summary cards (filled after a run)
            cardPanels[i] = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Margin = new Padding(0, 0, 0, 4) };
            layout.Controls.Add(cardPanels[i], 0, 1);

            // search bar
            var bar = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0, 0, 0, 4) };
            bar.Controls.Add(new System.Windows.Forms.Label { Text = "Search:", AutoSize = true, Margin = new Padding(0, 6, 4, 0), ForeColor = Theme.Muted });
            searches[i] = new TextBox { Width = 280 };
            searches[i].TextChanged += (s, e) => ApplyFilter(i);
            tip.SetToolTip(searches[i], "Type any part of a table, field or value to filter the rows below.");
            bar.Controls.Add(searches[i]);
            counts[i] = new System.Windows.Forms.Label { AutoSize = true, Margin = new Padding(12, 6, 0, 0), ForeColor = Theme.Muted };
            bar.Controls.Add(counts[i]);
            layout.Controls.Add(bar, 0, 2);

            // grid + empty state
            var host = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0), BorderStyle = BorderStyle.FixedSingle };
            grids[i] = NewGrid(i);
            grids[i].Visible = false;
            emptyStates[i] = new System.Windows.Forms.Label
            {
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Theme.Muted, Font = Theme.Section,
                Text = $"No results yet.\n\nClick  \"{d.RunText.Trim()}\"  on the left to run this report."
            };
            host.Controls.Add(grids[i]);
            host.Controls.Add(emptyStates[i]);
            layout.Controls.Add(host, 0, 3);
            return page;
        }

        Control BuildLogPanel()
        {
            var panel = new Panel { Dock = DockStyle.Fill, BackColor = Theme.PanelBack };
            txtLog = new TextBox
            {
                Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.None, BackColor = Theme.PanelBack, ForeColor = Theme.Muted, Font = Theme.Mono
            };
            var head = new System.Windows.Forms.Label
            {
                Dock = DockStyle.Top, Height = 22, Text = "  Activity log", Font = Theme.Bold, ForeColor = Theme.Brand,
                TextAlign = ContentAlignment.MiddleLeft, BackColor = Theme.BrandSoft
            };
            panel.Controls.Add(txtLog);
            panel.Controls.Add(head);
            return panel;
        }

        void SetSplitters()
        {
            try
            {
                mainSplit.Panel1MinSize = 220;
                mainSplit.SplitterDistance = LeftWidth + 45;
                rightSplit.SplitterDistance = Math.Max(rightSplit.Panel1MinSize, rightSplit.Height - 150);
            }
            catch (InvalidOperationException) { } // control too small to honour the sizes - keep defaults
        }

        void DrawTab(object sender, DrawItemEventArgs e)
        {
            var d = defs[e.Index];
            bool sel = e.Index == tabs.SelectedIndex;
            using (var b = new SolidBrush(sel ? d.Accent : Theme.PanelBack)) e.Graphics.FillRectangle(b, e.Bounds);
            if (!sel) using (var p = new Pen(d.Accent, 3)) e.Graphics.DrawLine(p, e.Bounds.Left + 2, e.Bounds.Bottom - 2, e.Bounds.Right - 2, e.Bounds.Bottom - 2);
            var text = d.TabText + (results[e.Index] != null ? $"  ({results[e.Index].Rows.Count:N0})" : "");
            TextRenderer.DrawText(e.Graphics, text, sel ? Theme.Bold : Theme.Base, e.Bounds, sel ? Color.White : d.Accent,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        // ---------------- small UI helpers ----------------
        static System.Windows.Forms.Label Section(string number, string text) => new System.Windows.Forms.Label
        {
            Text = string.IsNullOrEmpty(number) ? text : $"Step {number}  ·  {text}",
            AutoSize = true, Font = Theme.Section, ForeColor = Theme.Brand, Margin = new Padding(3, 14, 3, 4)
        };

        static System.Windows.Forms.Label Hint(string text) => new System.Windows.Forms.Label
        {
            Text = text, AutoSize = true, MaximumSize = new Size(LeftWidth, 0), Font = Theme.Small, ForeColor = Theme.Muted, Margin = new Padding(3, 2, 3, 4)
        };

        static Button ColorButton(string text, Color back, EventHandler click)
        {
            var b = new Button
            {
                Text = text, Width = LeftWidth, Height = 32, FlatStyle = FlatStyle.Flat, BackColor = back, ForeColor = Color.White,
                Font = Theme.Bold, Cursor = Cursors.Hand, Padding = new Padding(6, 0, 6, 0), Margin = new Padding(3, 3, 3, 3)
            };
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = ControlPaint.Light(back, 0.25f);
            b.EnabledChanged += (s, e) => b.BackColor = b.Enabled ? back : Color.FromArgb(200, 198, 196);
            b.Click += click;
            return b;
        }

        CheckBox Check(string text, bool on, string help)
        {
            var c = new CheckBox { Text = text, Checked = on, AutoSize = true, Margin = new Padding(3, 4, 3, 0) };
            tip.SetToolTip(c, help);
            return c;
        }

        static NumericUpDown Nud(decimal min, decimal max, decimal val, int dp, decimal inc) =>
            new NumericUpDown { Minimum = min, Maximum = max, Value = val, DecimalPlaces = dp, Increment = inc, Width = 110, ThousandsSeparator = true };

        void AddSetting(FlowLayoutPanel parent, string label, NumericUpDown input, string help)
        {
            parent.Controls.Add(new System.Windows.Forms.Label { Text = label, AutoSize = true, MaximumSize = new Size(LeftWidth, 0), Margin = new Padding(3, 6, 3, 2) });
            parent.Controls.Add(input);
            parent.Controls.Add(Hint(help));
            tip.SetToolTip(input, help);
        }

        static Control LegendChip(Severity sev)
        {
            var l = new System.Windows.Forms.Label
            {
                Text = $"●  {Theme.Name(sev)}  -  {Theme.Meaning(sev)}", AutoSize = false, Width = LeftWidth, Height = 24,
                TextAlign = ContentAlignment.MiddleLeft, BackColor = Theme.Back(sev), ForeColor = Theme.Fore(sev),
                Font = Theme.Small, Margin = new Padding(3, 1, 3, 1), Padding = new Padding(6, 0, 0, 0)
            };
            return l;
        }

        void SetLoadStatus(string text, Color color)
        {
            if (InvokeRequired) { BeginInvoke(new Action<string, Color>(SetLoadStatus), text, color); return; }
            lblLoadStatus.Text = text;
            lblLoadStatus.ForeColor = color;
        }

        void Log(string s)
        {
            if (InvokeRequired) { BeginInvoke(new Action<string>(Log), s); return; }
            txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {s}{Environment.NewLine}");
        }

        // ---------------- grid ----------------
        DataGridView NewGrid(int i)
        {
            var g = new DataGridView
            {
                Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false,
                RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, BackgroundColor = Color.White,
                BorderStyle = BorderStyle.None, CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal, GridColor = Theme.GridLine,
                EnableHeadersVisualStyles = false, ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                ColumnHeadersHeight = 34, ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None
            };
            g.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = defs[i].Accent, ForeColor = Color.White, Font = Theme.Bold, SelectionBackColor = defs[i].Accent,
                SelectionForeColor = Color.White, Padding = new Padding(4, 0, 4, 0), WrapMode = DataGridViewTriState.False,
                Alignment = DataGridViewContentAlignment.MiddleLeft
            };
            g.DefaultCellStyle = new DataGridViewCellStyle
            {
                Font = Theme.Base, ForeColor = Theme.Text, BackColor = Color.White, SelectionBackColor = Theme.Selection,
                SelectionForeColor = Theme.Text, Padding = new Padding(4, 0, 4, 0)
            };
            g.AlternatingRowsDefaultCellStyle.BackColor = Theme.AltRow;
            g.RowTemplate.Height = 26;
            typeof(DataGridView).GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(g, true, null);

            g.DataBindingComplete += (s, e) => ConfigureColumns(i);
            g.CellFormatting += (s, e) => FormatStatusCell(i, e);
            g.CellPainting += PaintPercentBar;
            return g;
        }

        void ConfigureColumns(int i)
        {
            var g = grids[i];
            if (columnsReady[i] || g.Columns.Count == 0) return;
            columnsReady[i] = true;

            var status = g.Columns[defs[i].StatusColumn];
            if (status != null) { status.DisplayIndex = 0; status.HeaderText = "● " + status.HeaderText; }

            foreach (DataGridViewColumn c in g.Columns)
            {
                if (Reports.ColumnTips.TryGetValue(c.Name, out var t)) c.ToolTipText = t;
                if (c.ValueType == typeof(long)) { c.DefaultCellStyle.Format = "N0"; c.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight; }
                if (c.ValueType == typeof(double)) { c.DefaultCellStyle.Format = "0.#"; c.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight; }
                if (c.Name.EndsWith("%")) c.MinimumWidth = 90;
            }

            g.AutoResizeColumns(DataGridViewAutoSizeColumnsMode.DisplayedCells);
            foreach (DataGridViewColumn c in g.Columns)
            {
                c.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
                if (c.Width > 320) c.Width = 320;   // long text is still readable in the cell tooltip
            }
        }

        void FormatStatusCell(int i, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || grids[i].Columns[e.ColumnIndex].Name != defs[i].StatusColumn) return;
            var rule = defs[i].Match(Convert.ToString(e.Value));
            if (rule == null) return;
            e.CellStyle.BackColor = Theme.Back(rule.Severity);
            e.CellStyle.ForeColor = Theme.Fore(rule.Severity);
            e.CellStyle.SelectionBackColor = ControlPaint.Dark(Theme.Back(rule.Severity), 0.05f);
            e.CellStyle.SelectionForeColor = Theme.Fore(rule.Severity);
            e.CellStyle.Font = Theme.Bold;
            e.Value = "● " + rule.Label;
            e.FormattingApplied = true;
        }

        static void PaintPercentBar(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            var g = (DataGridView)sender;
            if (!g.Columns[e.ColumnIndex].Name.EndsWith("%") || !(e.Value is double v)) return;

            e.PaintBackground(e.ClipBounds, true);
            var r = e.CellBounds;
            r.Inflate(-5, -6);
            int w = (int)(r.Width * Math.Max(0, Math.Min(100, v)) / 100.0);
            using (var b = new SolidBrush(Theme.Bar)) e.Graphics.FillRectangle(b, r.X, r.Y, w, r.Height);
            e.PaintContent(e.ClipBounds);
            e.Handled = true;
        }

        // ---------------- summary cards ----------------
        void BuildCards(int i)
        {
            var panel = cardPanels[i];
            panel.SuspendLayout();
            panel.Controls.Clear();
            var dt = results[i];
            panel.Controls.Add(Card(i, dt.Rows.Count, "All results", Theme.Brand, Theme.BrandSoft, null, "Show every row."));

            var groups = dt.Rows.Cast<DataRow>()
                           .Select(r => defs[i].Match(Convert.ToString(r[defs[i].StatusColumn])))
                           .Where(r => r != null)
                           .GroupBy(r => r)
                           .OrderBy(g => g.Key.Severity);
            foreach (var g in groups)
                panel.Controls.Add(Card(i, g.Count(), g.Key.Label, Theme.Fore(g.Key.Severity), Theme.Back(g.Key.Severity), g.Key.Prefix,
                                        $"{Theme.Name(g.Key.Severity)}: {Theme.Meaning(g.Key.Severity)}.\nClick to show only these rows."));
            panel.ResumeLayout();
            HighlightCards(i);
        }

        Button Card(int i, int count, string label, Color fore, Color back, string filter, string help)
        {
            var b = new Button
            {
                Text = $"{count:N0}\n{label}", Tag = filter, Size = new Size(170, 54), FlatStyle = FlatStyle.Flat, BackColor = back,
                ForeColor = fore, Font = Theme.Bold, Cursor = Cursors.Hand, Margin = new Padding(0, 0, 8, 4)
            };
            b.FlatAppearance.BorderColor = fore;
            b.FlatAppearance.MouseOverBackColor = ControlPaint.Dark(back, 0.03f);
            b.Click += (s, e) => { cardFilters[i] = filter; HighlightCards(i); ApplyFilter(i); };
            tip.SetToolTip(b, help);
            return b;
        }

        void HighlightCards(int i)
        {
            foreach (Button b in cardPanels[i].Controls)
                b.FlatAppearance.BorderSize = Equals(b.Tag as string, cardFilters[i]) ? 3 : 1;
        }

        // ---------------- loading ----------------
        void LoadAll()
        {
            cts = new CancellationTokenSource();
            SetLoadStatus("Loading...", Theme.Brand);
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
                    if (a.Error != null)
                    {
                        SetLoadStatus("✖ Loading failed - see the activity log.", Theme.Danger);
                        Log("ERROR: " + a.Error.Message);
                        MessageBox.Show(a.Error.Message, "Error");
                        return;
                    }
                    var t = (Tuple<List<SolItem>, MetadataCache>)a.Result;
                    cache = t.Item2;
                    clbSolutions.Items.Clear();
                    foreach (var s in t.Item1) clbSolutions.Items.Add(s);
                    SetLoadStatus($"✔ Loaded {cache.Entities.Count:N0} tables and {t.Item1.Count:N0} solutions.", Theme.Fore(Severity.Healthy));
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
            tabs.SelectedIndex = idx;
            emptyStates[idx].Text = $"Running \"{defs[idx].Name}\"...\n\nProgress is shown in the activity log below.";
            Log($"Started {defs[idx].Number}. {defs[idx].Name}.");

            WorkAsync(new WorkAsyncInfo
            {
                Message = "Running " + defs[idx].Name + "...",
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
                    if (a.Error is OperationCanceledException) { Log("Cancelled."); ShowResults(idx); return; }
                    if (a.Error != null)
                    {
                        Log("ERROR: " + a.Error.Message);
                        emptyStates[idx].Text = "✖ The report failed.\n\n" + a.Error.Message;
                        MessageBox.Show(a.Error.Message, "Error");
                        return;
                    }
                    results[idx] = (DataTable)a.Result;
                    cardFilters[idx] = null;
                    searches[idx].Text = "";
                    columnsReady[idx] = false;
                    tabs.SelectedIndex = idx;
                    ShowResults(idx);   // make the grid visible before binding so its columns are generated
                    grids[idx].DataSource = results[idx];
                    ConfigureColumns(idx);
                    BuildCards(idx);
                    UpdateCount(idx);
                    tabs.Invalidate();
                    Log($"{defs[idx].Name} finished: {results[idx].Rows.Count:N0} row(s).");
                }
            });
        }

        void ShowResults(int i)
        {
            var dt = results[i];
            bool any = dt != null && dt.Rows.Count > 0;
            grids[i].Visible = any;
            emptyStates[i].Visible = !any;
            if (dt == null)
                emptyStates[i].Text = $"No results yet.\n\nClick  \"{defs[i].RunText.Trim()}\"  on the left to run this report.";
            else if (!any)
                emptyStates[i].Text = "✔ Nothing found - no issues of this kind in the selected scope.";
            UpdateCount(i);
        }

        void UpdateCount(int i)
        {
            var dt = results[i];
            counts[i].Text = dt == null ? "" : $"Showing {dt.DefaultView.Count:N0} of {dt.Rows.Count:N0} rows";
        }

        // ---------------- filter / export / script ----------------
        static string Like(string s) => s.Replace("'", "''").Replace("[", "[[]").Replace("%", "[%]").Replace("*", "[*]");

        void ApplyFilter(int i)
        {
            var dt = results[i];
            if (dt == null) return;
            var parts = new List<string>();
            if (cardFilters[i] != null) parts.Add($"[{defs[i].StatusColumn}] LIKE '{Like(cardFilters[i])}%'");
            var t = searches[i].Text.Trim();
            if (t.Length > 0)
                parts.Add("(" + string.Join(" OR ", dt.Columns.Cast<DataColumn>().Select(c => $"Convert([{c.ColumnName}], 'System.String') LIKE '%{Like(t)}%'")) + ")");
            dt.DefaultView.RowFilter = string.Join(" AND ", parts);
            UpdateCount(i);
        }

        void Export()
        {
            int i = tabs.SelectedIndex;
            var dt = results[i];
            if (dt == null) { MessageBox.Show($"Run \"{defs[i].Name}\" first."); return; }
            using (var d = new SaveFileDialog { Filter = "CSV|*.csv", FileName = $"{defs[i].Number}_{defs[i].Name.Replace(" + ", "_").Replace(" ", "_").Replace("%", "pct")}.csv" })
                if (d.ShowDialog() == DialogResult.OK) { Util.ToCsv(dt.DefaultView.ToTable(), d.FileName); Log("Exported " + d.FileName); }
        }

        void GenerateScript()
        {
            if (!chkEnableScript.Checked) { MessageBox.Show("Tick 'Enable delete-script generation' first."); return; }
            if (results[3] == null) { MessageBox.Show($"Run \"{defs[3].Name}\" (report 4) first."); return; }
            using (var d = new SaveFileDialog { Filter = "PowerShell|*.ps1", FileName = "delete-unused-fields.ps1" })
                if (d.ShowDialog() == DialogResult.OK) { File.WriteAllText(d.FileName, DeleteScriptBuilder.Build(results[3])); Log("Script saved (all commands commented out): " + d.FileName); }
        }
    }
}
