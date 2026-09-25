#!/usr/bin/env bash
# The published tool folder, dist/estate/ (V3_MILESTONES.md WP 0.7, section 1 fact 1): estate published framework-dependent
# on net10.0 with DacFx and every dependency beside it, DacFx's net10.0 SqlTasks targets, and the reference assemblies a classic
# .sqlproj build needs. Both pins are read where they are declared, never restated. ci/publish.ps1 takes the same steps.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
out="$root/dist/estate"
dacfx="$(sed -n 's/.*<PackageVersion Include="Microsoft.SqlServer.DacFx" Version="\([^"]*\)".*/\1/p' "$root/Directory.Packages.props")"
reference_assemblies="$(sed -n 's/.*<PackageDownload Include="Microsoft.NETFramework.ReferenceAssemblies.net472" Version="\[\([^]]*\)\]".*/\1/p' "$root/cli/cli.csproj")"
if [ -z "$dacfx" ] || [ -z "$reference_assemblies" ]; then
  echo "ci/publish.sh: the DacFx pin (Directory.Packages.props) or the reference assemblies' pin (cli/cli.csproj) was not found" >&2
  exit 2
fi

rm -rf "$out"
dotnet publish "$root/cli/cli.csproj" -c Release -f net10.0 --no-self-contained -o "$out" -nologo -v q

# The restore that publish ran put both packages in the global packages folder; ask NuGet where that is.
packages="$(dotnet nuget locals global-packages --list | sed -n 's/^global-packages: *//p' | tr -d '\r')"
packages="${packages%[/\\]}"
cp "$packages/microsoft.sqlserver.dacfx/$dacfx/lib/net10.0/"Microsoft.Data.Tools.Schema.SqlTasks*.targets "$out/"
from="$packages/microsoft.netframework.referenceassemblies.net472/$reference_assemblies/build/.NETFramework/v4.7.2"
mkdir -p "$out/refasm/.NETFramework/v4.7.2/RedistList"
cp "$from/mscorlib.dll" "$out/refasm/.NETFramework/v4.7.2/"
cp "$from/RedistList/FrameworkList.xml" "$out/refasm/.NETFramework/v4.7.2/RedistList/"

files="$(find "$out" -type f | wc -l | tr -d ' ')"
megabytes="$(du -sm "$out" | cut -f1)"
echo "dist/estate: $files files, $megabytes MB (DacFx $dacfx, reference assemblies $reference_assemblies)"
# The launcher (dist/estate/estate) finds .NET only machine-wide or through DOTNET_ROOT; dotnet itself runs the dll anywhere.
echo "run it as dotnet dist/estate/estate.dll <verb>; dist/estate/estate needs .NET installed machine-wide, or DOTNET_ROOT naming a per-user install"
