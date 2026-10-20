#!/usr/bin/env bash
# The published tool folder, dist/dbchange/ (V3_MILESTONES.md WP 0.7, section 1 fact 1): dbchange published framework-dependent
# on net10.0 with DacFx and every dependency beside it, DacFx's net10.0 SqlTasks targets, and the reference assemblies a classic
# .sqlproj build needs, and SQL Server 2022's (160) master.dacpac and msdb.dacpac where the targets' $(DacPacRootPath) names them.
# Every pin is read where it is declared, never restated. ci/publish.ps1 takes the same steps.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
out="$root/dist/dbchange"
dacfx="$(sed -n 's/.*<PackageVersion Include="Microsoft.SqlServer.DacFx" Version="\([^"]*\)".*/\1/p' "$root/Directory.Packages.props")"
downloaded() { sed -n 's/.*<PackageDownload Include="'"$1"'" Version="\[\([^]]*\)\]".*/\1/p' "$root/cli/cli.csproj"; }
reference_assemblies="$(downloaded Microsoft.NETFramework.ReferenceAssemblies.net472)"
master="$(downloaded Microsoft.SqlServer.Dacpacs.Master)"
msdb="$(downloaded Microsoft.SqlServer.Dacpacs.Msdb)"
if [ -z "$dacfx" ] || [ -z "$reference_assemblies" ] || [ -z "$master" ] || [ -z "$msdb" ]; then
  echo "ci/publish.sh: the DacFx pin (Directory.Packages.props) or a PackageDownload pin (cli/cli.csproj) was not found" >&2
  exit 2
fi

rm -rf "$out"
dotnet publish "$root/cli/cli.csproj" -c Release -f net10.0 --no-self-contained -o "$out" -nologo -v q

# The restore that publish ran put the four packages this script copies from (DacFx, the .NET Framework 4.7.2 reference
# assemblies, Microsoft.SqlServer.Dacpacs.Master and Microsoft.SqlServer.Dacpacs.Msdb) in the global packages folder; ask NuGet
# where that is.
packages="$(dotnet nuget locals global-packages --list | sed -n 's/^global-packages: *//p' | tr -d '\r')"
packages="${packages%[/\\]}"
cp "$packages/microsoft.sqlserver.dacfx/$dacfx/lib/net10.0/"Microsoft.Data.Tools.Schema.SqlTasks*.targets "$out/"
from="$packages/microsoft.netframework.referenceassemblies.net472/$reference_assemblies/build/.NETFramework/v4.7.2"
mkdir -p "$out/refasm/.NETFramework/v4.7.2/RedistList"
cp "$from/mscorlib.dll" "$out/refasm/.NETFramework/v4.7.2/"
cp "$from/RedistList/FrameworkList.xml" "$out/refasm/.NETFramework/v4.7.2/RedistList/"
# Microsoft.Data.Tools.Schema.SqlTasks.targets sets DacPacRootPath to NETCoreTargetsPath under NetCoreBuild, and SSDT's system
# database reference reads $(DacPacRootPath)\Extensions\Microsoft\SQLDB\Extensions\SqlServer\160\SqlSchemas\master.dacpac.
schemas="$out/Extensions/Microsoft/SQLDB/Extensions/SqlServer/160/SqlSchemas"
mkdir -p "$schemas"
cp "$packages/microsoft.sqlserver.dacpacs.master/$master/tools/master.dacpac" "$schemas/"
cp "$packages/microsoft.sqlserver.dacpacs.msdb/$msdb/tools/msdb.dacpac" "$schemas/"

files="$(find "$out" -type f | wc -l | tr -d ' ')"
megabytes="$(du -sm "$out" | cut -f1)"
echo "dist/dbchange: $files files, $megabytes MB (DacFx $dacfx, reference assemblies $reference_assemblies, master $master, msdb $msdb)"
# The launcher (dist/dbchange/dbchange) finds .NET only machine-wide or through DOTNET_ROOT; dotnet itself runs the dll anywhere.
echo "run it as dotnet dist/dbchange/dbchange.dll <verb>; dist/dbchange/dbchange needs .NET installed machine-wide, or DOTNET_ROOT naming a per-user install"
