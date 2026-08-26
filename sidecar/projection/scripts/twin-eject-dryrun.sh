#!/usr/bin/env bash
# THE TWIN — the ejection dry-run (THE_TWIN.md §8).
#
# Proves the standalone-binary shape of the peel: publish the `twin`
# executable out of this solution, stand it in a scratch SSDT-shaped
# repository that has never seen the projection codebase, and drive its
# no-container surface (help, init, config refusals). The full
# materialize/mint loop is proven by the Twin-Docker pool
# (`twin-test.sh docker`); this script proves the BINARY travels.
set -euo pipefail

cd "$(dirname "$0")/.."
REPO="$PWD"

SCRATCH="$(mktemp -d /tmp/twin-eject-dryrun.XXXXXX)"
PUBLISH="$SCRATCH/tool"
ESTATE="$SCRATCH/estate"
trap 'rm -rf "$SCRATCH"' EXIT

echo "— publishing the twin executable"
dotnet publish src/Twin.Cli/Twin.Cli.fsproj -c Release -o "$PUBLISH" --nologo -v q

echo "— staging a scratch SSDT-shaped repository"
mkdir -p "$ESTATE/Tables"
cat > "$ESTATE/Tables/dbo.Thing.sql" << 'SQL'
CREATE TABLE [dbo].[Thing] ([Id] INT NOT NULL, CONSTRAINT [PK_Thing] PRIMARY KEY ([Id]));
SQL

cd "$ESTATE"

# The published app is framework-dependent (the dotnet-tool distribution
# shape); point the apphost at the host's runtime when it lives outside
# the default install locations.
DOTNET_BIN="$(command -v dotnet)"
export DOTNET_ROOT="$(dirname "$(readlink -f "$DOTNET_BIN")")"

echo "— twin --help (exit 0)"
"$PUBLISH/twin" --help > /dev/null

echo "— twin status without twin.json refuses with exit 6"
set +e
"$PUBLISH/twin" status > /dev/null 2>&1
CODE=$?
set -e
[ "$CODE" -eq 6 ] || { echo "expected exit 6 (config missing), got $CODE"; exit 1; }

echo "— twin init writes a starter twin.json"
"$PUBLISH/twin" init > /dev/null
[ -f twin.json ] || { echo "twin.json was not written"; exit 1; }

echo "— a malformed twin.json refuses with exit 6, located"
echo '{ "estate": { "tables": "Tables/*.sql" }, "containr": {} }' > twin.json
set +e
OUT="$("$PUBLISH/twin" status 2>&1)"
CODE=$?
set -e
[ "$CODE" -eq 6 ] || { echo "expected exit 6 (unknown key), got $CODE"; exit 1; }
echo "$OUT" | grep -q 'containr' || { echo "the refusal did not name the path"; exit 1; }

# The peel's distribution shape (B6): the same surface through the PACKED
# dotnet tool — pack, install to a tool path, and re-drive the probes.
echo "— packing the dotnet-tool rendition (Twin.Tool)"
dotnet pack "$REPO/src/Twin.Cli/Twin.Cli.fsproj" -c Release -o "$SCRATCH/nupkg" --nologo -v q
dotnet tool install Twin.Tool --tool-path "$SCRATCH/toolpath" --add-source "$SCRATCH/nupkg" > /dev/null
TOOL="$SCRATCH/toolpath/twin"

echo "— the installed tool answers the same surface"
"$TOOL" --help > /dev/null
set +e
"$TOOL" status > /dev/null 2>&1
CODE=$?
set -e
[ "$CODE" -eq 6 ] || { echo "expected exit 6 from the installed tool over the malformed config, got $CODE"; exit 1; }
rm -f twin.json
"$TOOL" init > /dev/null
[ -f twin.json ] || { echo "the installed tool's init wrote nothing"; exit 1; }

echo "The ejection dry-run holds: the binary travels, the packed tool installs and answers, the surface refuses in place."
