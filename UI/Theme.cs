using System.Drawing;

namespace SolutionAnalyzerFieldHealthChecker.UI
{
    /// <summary>How urgent a result row is. Drives the colour of status cells, summary cards and the colour key.</summary>
    public enum Severity { Cleanup, Review, Suggestion, Healthy, NoData }

    /// <summary>Shared fonts and colours for the plugin UI.</summary>
    public static class Theme
    {
        public static readonly Font Base = new Font("Segoe UI", 9F);
        public static readonly Font Bold = new Font("Segoe UI", 9F, FontStyle.Bold);
        public static readonly Font Small = new Font("Segoe UI", 8.25F);
        public static readonly Font Section = new Font("Segoe UI Semibold", 10.5F);
        public static readonly Font Title = new Font("Segoe UI Semibold", 13F);
        public static readonly Font Mono = new Font("Consolas", 8.5F);

        public static readonly Color Brand = Color.FromArgb(31, 78, 121);
        public static readonly Color BrandSoft = Color.FromArgb(222, 236, 249);
        public static readonly Color PanelBack = Color.FromArgb(243, 246, 250);
        public static readonly Color Text = Color.FromArgb(32, 31, 30);
        public static readonly Color Muted = Color.FromArgb(96, 94, 92);
        public static readonly Color GridLine = Color.FromArgb(225, 223, 221);
        public static readonly Color AltRow = Color.FromArgb(248, 250, 253);
        public static readonly Color Selection = Color.FromArgb(204, 228, 247);
        public static readonly Color Bar = Color.FromArgb(168, 205, 235);
        public static readonly Color Danger = Color.FromArgb(164, 38, 44);

        public static Color Back(Severity s)
        {
            switch (s)
            {
                case Severity.Cleanup: return Color.FromArgb(253, 226, 225);
                case Severity.Review: return Color.FromArgb(255, 244, 206);
                case Severity.Suggestion: return Color.FromArgb(222, 236, 249);
                case Severity.Healthy: return Color.FromArgb(223, 246, 221);
                default: return Color.FromArgb(237, 235, 233);
            }
        }

        public static Color Fore(Severity s)
        {
            switch (s)
            {
                case Severity.Cleanup: return Color.FromArgb(164, 38, 44);
                case Severity.Review: return Color.FromArgb(138, 90, 0);
                case Severity.Suggestion: return Color.FromArgb(0, 78, 140);
                case Severity.Healthy: return Color.FromArgb(16, 124, 16);
                default: return Color.FromArgb(96, 94, 92);
            }
        }

        public static string Name(Severity s)
        {
            switch (s)
            {
                case Severity.Cleanup: return "Clean-up candidate";
                case Severity.Review: return "Needs review";
                case Severity.Suggestion: return "Suggestion";
                case Severity.Healthy: return "Healthy";
                default: return "No data";
            }
        }

        public static string Meaning(Severity s)
        {
            switch (s)
            {
                case Severity.Cleanup: return "Not used - can probably be removed";
                case Severity.Review: return "Worth a closer look before acting";
                case Severity.Suggestion: return "Design improvement to consider";
                case Severity.Healthy: return "Nothing to do";
                default: return "No records to judge yet";
            }
        }
    }
}
