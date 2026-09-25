#Requires -Version 7.0
# The published tool folder, dist/estate/ (V3_MILESTONES.md WP 0.7, section 1 fact 1): estate published framework-dependent
# on net10.0 with DacFx and every dependency beside it, DacFx's net10.0 SqlTasks targets, and the reference assemblies a classic
# .sqlproj build needs. Both pins are read where they are declared, never restated. ci/publish.sh takes the same steps.
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true

$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root 'dist/estate'
$dacfx = (Select-Xml -Path (Join-Path $root 'Directory.Packages.props') -XPath "//PackageVersion[@Include='Microsoft.SqlServer.DacFx']").Node.Version
$referenceAssemblies = (Select-Xml -Path (Join-Path $root 'cli/cli.csproj') -XPath "//PackageDownload[@Include='Microsoft.NETFramework.ReferenceAssemblies.net472']").Node.Version.Trim('[', ']')
if (-not $dacfx -or -not $referenceAssemblies) {
    throw "ci/publish.ps1: the DacFx pin (Directory.Packages.props) or the reference assemblies' pin (cli/cli.csproj) was not found"
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

$files = Get-ChildItem -Recurse -File $out
$megabytes = [int][Math]::Round(($files | Measure-Object Length -Sum).Sum / 1MB)
"dist/estate: $($files.Count) files, $megabytes MB (DacFx $dacfx, reference assemblies $referenceAssemblies)"
# The launcher (dist/estate/estate.exe) finds .NET only machine-wide or through DOTNET_ROOT; dotnet itself runs the dll anywhere.
'run it as dotnet dist/estate/estate.dll <verb>; dist/estate/estate.exe needs .NET installed machine-wide, or DOTNET_ROOT naming a per-user install'
