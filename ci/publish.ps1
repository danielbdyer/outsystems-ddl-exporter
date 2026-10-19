#Requires -Version 7.0
# The published tool folder, dist/dbchange/ (V3_MILESTONES.md WP 0.7, section 1 fact 1): dbchange published framework-dependent
# on net10.0 with DacFx and every dependency beside it, DacFx's net10.0 SqlTasks targets, and the reference assemblies a classic
# .sqlproj build needs, and SQL Server 2022's (160) master.dacpac and msdb.dacpac where the targets' $(DacPacRootPath) names them.
# Every pin is read where it is declared, never restated. ci/publish.sh takes the same steps.
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true

$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root 'dist/dbchange'
$dacfx = (Select-Xml -Path (Join-Path $root 'Directory.Packages.props') -XPath "//PackageVersion[@Include='Microsoft.SqlServer.DacFx']").Node.Version
function Downloaded([string] $package) {
    $node = (Select-Xml -Path (Join-Path $root 'cli/cli.csproj') -XPath "//PackageDownload[@Include='$package']").Node
    if ($node) { $node.Version.Trim('[', ']') }
}
$referenceAssemblies = Downloaded 'Microsoft.NETFramework.ReferenceAssemblies.net472'
$master = Downloaded 'Microsoft.SqlServer.Dacpacs.Master'
$msdb = Downloaded 'Microsoft.SqlServer.Dacpacs.Msdb'
if (-not $dacfx -or -not $referenceAssemblies -or -not $master -or -not $msdb) {
    throw "ci/publish.ps1: the DacFx pin (Directory.Packages.props) or a PackageDownload pin (cli/cli.csproj) was not found"
}

if (Test-Path $out) { Remove-Item -Recurse -Force $out }
dotnet publish (Join-Path $root 'cli/cli.csproj') -c Release -f net10.0 --no-self-contained -o $out -nologo -v q

# The restore that publish ran put both packages in the global packages folder; ask NuGet where that is.
$packages = ((dotnet nuget locals global-packages --list) -replace '^global-packages:\s*', '').Trim()
Copy-Item (Join-Path $packages "microsoft.sqlserver.dacfx/$dacfx/lib/net10.0/Microsoft.Data.Tools.Schema.SqlTasks*.targets") $out
$from = Join-Path $packages "microsoft.netframework.referenceassemblies.net472/$referenceAssemblies/build/.NETFramework/v4.7.2"
$refasm = New-Item -ItemType Directory -Force (Join-Path $out 'refasm/.NETFramework/v4.7.2/RedistList')
Copy-Item (Join-Path $from 'mscorlib.dll') $refasm.Parent.FullName
Copy-Item (Join-Path $from 'RedistList/FrameworkList.xml') $refasm.FullName
# Microsoft.Data.Tools.Schema.SqlTasks.targets sets DacPacRootPath to NETCoreTargetsPath under NetCoreBuild, and SSDT's system
# database reference reads $(DacPacRootPath)\Extensions\Microsoft\SQLDB\Extensions\SqlServer\160\SqlSchemas\master.dacpac.
$schemas = New-Item -ItemType Directory -Force (Join-Path $out 'Extensions/Microsoft/SQLDB/Extensions/SqlServer/160/SqlSchemas')
Copy-Item (Join-Path $packages "microsoft.sqlserver.dacpacs.master/$master/tools/master.dacpac") $schemas.FullName
Copy-Item (Join-Path $packages "microsoft.sqlserver.dacpacs.msdb/$msdb/tools/msdb.dacpac") $schemas.FullName

$files = Get-ChildItem -Recurse -File $out
$megabytes = [int][Math]::Round(($files | Measure-Object Length -Sum).Sum / 1MB)
"dist/dbchange: $($files.Count) files, $megabytes MB (DacFx $dacfx, reference assemblies $referenceAssemblies, master $master, msdb $msdb)"
# The launcher (dist/dbchange/dbchange.exe) finds .NET only machine-wide or through DOTNET_ROOT; dotnet itself runs the dll anywhere.
'run it as dotnet dist/dbchange/dbchange.dll <verb>; dist/dbchange/dbchange.exe needs .NET installed machine-wide, or DOTNET_ROOT naming a per-user install'
