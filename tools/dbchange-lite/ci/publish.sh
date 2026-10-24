#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
out="$root/dist/dbchange-lite"
dacfx="$(sed -n 's/.*<PackageVersion Include="Microsoft.SqlServer.DacFx" Version="\([^"]*\)".*/\1/p' "$root/Directory.Packages.props")"
downloaded() { sed -n 's/.*<PackageDownload Include="'"$1"'" Version="\[\([^]]*\)\]".*/\1/p' "$root/src/DbChangeLite.csproj"; }
reference_assemblies="$(downloaded Microsoft.NETFramework.ReferenceAssemblies.net472)"
master="$(downloaded Microsoft.SqlServer.Dacpacs.Master)"
msdb="$(downloaded Microsoft.SqlServer.Dacpacs.Msdb)"
if [ -z "$dacfx" ] || [ -z "$reference_assemblies" ] || [ -z "$master" ] || [ -z "$msdb" ]; then
  echo "ci/publish.sh: the DacFx pin (Directory.Packages.props) or a PackageDownload pin (src/DbChangeLite.csproj) was not found" >&2
  exit 2
fi

rm -rf "$out"
dotnet publish "$root/src/DbChangeLite.csproj" -c Release -f net10.0 --no-self-contained -o "$out" -nologo -v q

packages="$(dotnet nuget locals global-packages --list | sed -n 's/^global-packages: *//p' | tr -d '\r')"
packages="${packages%[/\\]}"
cp "$packages/microsoft.sqlserver.dacfx/$dacfx/lib/net10.0/"Microsoft.Data.Tools.Schema.SqlTasks*.targets "$out/"
from="$packages/microsoft.netframework.referenceassemblies.net472/$reference_assemblies/build/.NETFramework/v4.7.2"
mkdir -p "$out/refasm/.NETFramework/v4.7.2/RedistList"
cp "$from/mscorlib.dll" "$out/refasm/.NETFramework/v4.7.2/"
cp "$from/RedistList/FrameworkList.xml" "$out/refasm/.NETFramework/v4.7.2/RedistList/"
schemas="$out/Extensions/Microsoft/SQLDB/Extensions/SqlServer/160/SqlSchemas"
mkdir -p "$schemas"
cp "$packages/microsoft.sqlserver.dacpacs.master/$master/tools/master.dacpac" "$schemas/"
cp "$packages/microsoft.sqlserver.dacpacs.msdb/$msdb/tools/msdb.dacpac" "$schemas/"

echo "dist/dbchange-lite: $(find "$out" -type f | wc -l | tr -d ' ') files, $(du -sm "$out" | cut -f1) MB (DacFx $dacfx, reference assemblies $reference_assemblies, master $master, msdb $msdb)"
echo "run it as dotnet dist/dbchange-lite/dbchange-lite.dll <verb>"
