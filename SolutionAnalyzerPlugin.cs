using System.ComponentModel.Composition;
using XrmToolBox.Extensibility;
using XrmToolBox.Extensibility.Interfaces;

namespace SolutionAnalyzer
{
    [Export(typeof(IXrmToolBoxPlugin)),
     ExportMetadata("Name", "Solution Analyzer"),
     ExportMetadata("Description", "Data-model quality reports: similar fields, duplicate option sets, field length usage, unused custom fields."),
     ExportMetadata("SmallImageBase64", null),
     ExportMetadata("BigImageBase64", null),
     ExportMetadata("BackgroundColor", "Lavender"),
     ExportMetadata("PrimaryFontColor", "Black"),
     ExportMetadata("SecondaryFontColor", "Gray")]
    public class SolutionAnalyzerPlugin : PluginBase
    {
        public override IXrmToolBoxPluginControl GetControl() => new MainControl();
    }
}
