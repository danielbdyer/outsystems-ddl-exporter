#!/usr/bin/env bash
# Runs the custom FSharp.Analyzers.SDK analyzers from
# `Projection.Analyzers` against the projection sidecar's projects.
# Opt-in (not yet on CI per chapter-5 slice ν scoping).
#
# Per chapter 5 slice ν (CHAPTER_5_OPEN.md): one analyzer ships today
# (Projection001NoUnsafeTimeInCore). Recon #6 rewrote it onto the TYPED
# tree (symbol resolution, not name suffixes): it bans the impurity
# CAPABILITIES in src/Projection.Core/ — clock members (DateTime/
# DateTimeOffset .Now/.UtcNow/.Today, Guid.NewGuid), Random, Stopwatch,
# Environment, System.IO.*, Task/Async — by resolved full name, so a
# constructor (`new Random()`) and a CE are caught while the project's
# own same-named types (e.g. `Projection.Core.Environment`) are not.
# `Bench` (Stopwatch) and `PinnedWriting` (System.IO) are allowlisted.
# Future analyzers earn their place when false-negatives surface.

set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/.."

# Build the analyzer assembly + the consuming projects first so
# fsharp-analyzers has fresh DLLs + .deps.json to load.
dotnet build src/Projection.Analyzers/Projection.Analyzers.fsproj --nologo --verbosity quiet
dotnet build Projection.sln --nologo --verbosity quiet

# Register the tool if it's not already installed in this checkout.
dotnet tool restore --tool-manifest .config/dotnet-tools.json >/dev/null

ANALYZERS_PATH="$(pwd)/src/Projection.Analyzers/bin/Debug/net8.0"

# Run against Projection.Core — the only project that today's
# analyzer (PRJ001) targets. As the analyzer set grows, extend this
# loop to additional projects.
dotnet fsharp-analyzers \
    --analyzers-path "$ANALYZERS_PATH" \
    --project src/Projection.Core/Projection.Core.fsproj \
    --code-root . \
    --verbosity n
