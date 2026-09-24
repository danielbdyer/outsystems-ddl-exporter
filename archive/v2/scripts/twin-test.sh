#!/usr/bin/env bash
# THE TWIN — test runner. Mirrors scripts/test.sh's pool discipline for the
# Twin's two assemblies: `fast` is the pure pool (Twin.Tests), `docker` is
# the container pool (Twin.Tests.Integration, its own named containers on
# ports 21533/21633 — never the warm projection container), `all` runs them
# SEQUENTIALLY (never concurrently — the OOM survival rule, CLAUDE.md §4).
set -euo pipefail

cd "$(dirname "$0")/.."

MODE="${1:-fast}"

run_fast() {
  dotnet test tests/Twin.Tests/Twin.Tests.fsproj "$@"
}

run_docker() {
  dotnet test tests/Twin.Tests.Integration/Twin.Tests.Integration.fsproj "$@"
}

case "$MODE" in
  fast)   shift || true; run_fast "$@" ;;
  docker) shift || true; run_docker "$@" ;;
  all)    shift || true; run_fast "$@"; run_docker "$@" ;;
  focus)
    shift
    PATTERN="${1:?usage: twin-test.sh focus <pattern>}"
    shift || true
    dotnet test tests/Twin.Tests/Twin.Tests.fsproj --filter "FullyQualifiedName~${PATTERN}" "$@" \
      || dotnet test tests/Twin.Tests.Integration/Twin.Tests.Integration.fsproj --filter "FullyQualifiedName~${PATTERN}" "$@"
    ;;
  *)
    echo "usage: twin-test.sh [fast|docker|all|focus <pattern>]" >&2
    exit 1
    ;;
esac
