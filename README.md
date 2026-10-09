# Solution Analyzer (XrmToolBox plugin)

Read-only data-model quality reports for Dataverse / Dynamics 365:

1. **Similar fields + fill %** - msemr_subject vs xyz_subject (prefix stripped, same display name, fuzzy match), with % of records that have data. Flags *Merge candidate* / *Empty*.
2. **Duplicate option sets** - same name on a table, similar local sets across tables (Jaccard on labels), local sets that match an existing global option set.
3. **Field length analyzer** - configured MaxLength vs actual max / avg / P95 length used (single + multi-line text).
4. **Unused custom fields** - custom columns with 0 data, dependencies (RetrieveDependenciesForDelete) and text scan of forms, views, charts, workflows, business rules, plug-in step filtering attributes. Status = SAFE TO DELETE or REVIEW.

## Build
1. Open the folder in Visual Studio 2022 (or `dotnet build`), target net48.
2. Update the `XrmToolBoxPackage` version in the .csproj to the latest on NuGet.
3. Copy `bin/Debug/net48/SolutionAnalyzer.dll` into XrmToolBox's `Plugins` folder (or use the XrmToolBox Plugin Template + `Publish` flow for the Plugin Store / .nupkg).

## Notes & limits
- Aggregate queries hit the 50,000 limit -> automatic createdon bisection, then paged fallback.
- Length analysis reads records (FetchXML has no LEN). Use the "max records/table" setting on big tables. TDS-endpoint mode is NOT implemented.
- Dataverse cannot reduce MaxLength of an existing column - report 3 is for design review.
- Not scanned for references: web resource JS, Power Automate cloud flows, canvas apps, PCF. Check these manually before deleting.
- Solution scope: tables added as whole components include all their custom columns.
- Delete-script generation writes a .ps1 with every command commented out. The plugin never deletes anything.
- Code has not been compiled against your exact XrmToolBox version - expect minor fixes.
