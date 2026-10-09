# Solution Analyzer & Field Health Checker (XrmToolBox plugin)

**Author:** Shoaib Khan

Read-only data-model quality reports for Dataverse / Dynamics 365:

1. **Similar fields + fill %** - msemr_subject vs xyz_subject (prefix stripped, same display name, fuzzy match), with % of records that have data. Flags *Merge candidate* / *Empty*.
2. **Duplicate option sets** - same name on a table, similar local sets across tables (Jaccard on labels), local sets that match an existing global option set.
3. **Field length analyzer** - configured MaxLength vs actual max / avg / P95 length used (single + multi-line text).
4. **Unused custom fields** - custom columns with 0 data, dependencies (RetrieveDependenciesForDelete) and text scan of forms, views, charts, workflows, business rules, plug-in step filtering attributes. Status = SAFE TO DELETE or REVIEW.

## Build
1. Open the folder in Visual Studio 2022 (or `dotnet build`), target net48.
2. To move to a newer XrmToolBox, update the `XrmToolBoxPackage` version in the .csproj and the `XrmToolBox` dependency in the .nuspec together.
3. On Windows, every build automatically copies `SolutionAnalyzerFieldHealthChecker.dll` into `%APPDATA%\MscrmTools\XrmToolBox\Plugins` (`AppData\Roaming\MscrmTools\XrmToolBox\Plugins`) - restart XrmToolBox to load it. Otherwise copy `bin/Debug/net48/SolutionAnalyzerFieldHealthChecker.dll` into XrmToolBox's `Plugins` folder manually (or use the XrmToolBox Plugin Template + `Publish` flow for the Plugin Store / .nupkg).

## Release to the XrmToolBox Tool Library
Publishing uses NuGet **Trusted Publishing** from GitHub Actions (`.github/workflows/release.yml`) - no API key is stored.

One-time setup on nuget.org (Account > Trusted Publishing > Create):
- Package owner `Shoaib_khan`, CI/CD provider **GitHub Actions**
- Repository owner `shoaib1-OxDh`, repository `Solution_Analyzer`, workflow file `release.yml`, environment empty
- Scope: Push new packages and package versions; package pattern `ShoaibKhan.XrmToolBox.SolutionAnalyzer`

To release: `git tag v1.1 && git push origin v1.1` (or run *Release to NuGet* from the Actions tab). The workflow builds on Windows, runs `dotnet pack` (package layout in `SolutionAnalyzerFieldHealthChecker.nuspec`: DLL in `lib\net48\Plugins`, dependency on `XrmToolBox`) and pushes to nuget.org. The package version is the build version (`1.<year>.<MMdd>.<HHmm>`).

First release only: submit the package on xrmtoolbox.com so it is validated and listed in the Tool Library. Later versions are picked up as updates.

## Notes & limits
- Aggregate queries hit the 50,000 limit -> automatic createdon bisection, then paged fallback.
- Length analysis reads records (FetchXML has no LEN). Use the "max records/table" setting on big tables. TDS-endpoint mode is NOT implemented.
- Dataverse cannot reduce MaxLength of an existing column - report 3 is for design review.
- Not scanned for references: web resource JS, Power Automate cloud flows, canvas apps, PCF. Check these manually before deleting.
- Solution scope: tables added as whole components include all their custom columns.
- Delete-script generation writes a .ps1 with every command commented out. The plugin never deletes anything.
- Code has not been compiled against your exact XrmToolBox version - expect minor fixes.
