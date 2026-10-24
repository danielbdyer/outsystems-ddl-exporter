#!/usr/bin/env bash
# v2's read side, built from the archive, as the oracle of WP 3.1's differential (V3_MILESTONES.md §9; DECISIONS.md, 2026-10-19).
# v2's CLI writes no read-side catalog, so this builds the library pair v2's own capture composes: ReadSide.readSchema
# (archive/v2/src/Projection.Adapters.Sql/ReadSide.fs), which reads a database's tables, columns, keys, references, indexes,
# checks and triggers, then CatalogCodec.serialize (archive/v2/src/Projection.Targets.Json/CatalogCodec.fs), which writes them as
# JSON. A short F# program calls the two; it reads the connection from DBCHANGE_READSIDE_CONNECTION, never prints it, and
# writes the catalog to the path its one argument names, exiting 2 with each error's code and message when the read fails.
#   bash ci/readside.sh   build it with the .NET 9.0.314 SDK v2 pins, and print the program's path, the value of DBCHANGE_READSIDE
# The program's sources are written to a temporary folder outside the repository, so neither v3's Directory.Build.props nor its
# central package versions reach the build, and no committed MSBuild file names archive/ (Budgets.Tests: ArchiveExclusion). Every
# bin/ and obj/, the archive's projects' included, goes under .dbchange/readside/ through --artifacts-path, so the archive stays
# as committed. Restoring v2's packages needs nuget.org.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
out="$root/.dbchange/readside"
src="$(mktemp -d)"
trap 'rm -rf "$src"' EXIT

cat > "$src/global.json" <<'EOF'
{ "sdk": { "version": "9.0.314", "rollForward": "disable" } }
EOF
cp "$root/NuGet.config" "$src/NuGet.config"
cat > "$src/ReadSide.fsproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework></PropertyGroup>
  <ItemGroup><Compile Include="Program.fs" /></ItemGroup>
  <ItemGroup>
    <ProjectReference Include="$root/archive/v2/src/Projection.Adapters.Sql/Projection.Adapters.Sql.fsproj" />
    <ProjectReference Include="$root/archive/v2/src/Projection.Targets.Json/Projection.Targets.Json.fsproj" />
  </ItemGroup>
</Project>
EOF
cat > "$src/Program.fs" <<'EOF'
open Microsoft.Data.SqlClient
open Projection.Adapters.Sql
open Projection.Targets.Json

[<EntryPoint>]
let main argv =
    let connection = System.Environment.GetEnvironmentVariable "DBCHANGE_READSIDE_CONNECTION"
    if isNull connection || argv.Length <> 1 then
        eprintfn "Set DBCHANGE_READSIDE_CONNECTION and name the file to write the catalog to."
        64
    else
        use cnn = new SqlConnection(connection)
        cnn.Open()
        match (ReadSide.readSchema cnn).GetAwaiter().GetResult() with
        | Ok catalog ->
            System.IO.File.WriteAllText(argv.[0], CatalogCodec.serialize catalog)
            0
        | Error errors ->
            for e in errors do eprintfn "%s: %s" e.Code e.Message
            2
EOF

(cd "$src" && dotnet build -c Release -nologo -v quiet --artifacts-path "$out") >&2
dll="$out/bin/ReadSide/release/ReadSide.dll"
[ -f "$dll" ] || { echo "ci/readside.sh: the build wrote no $dll" >&2; exit 1; }
echo "$dll"
