#!/usr/bin/env bash
# THE TWIN — the three-environment crossover rehearsal driver
# (PROVING_SURFACE_DESIGN §5.2's acceptance gate, run as one command).
#
# Brings the warm SQL container up (the environment copies and the merge's
# trunk binding ride it; the twin's own container stays fixture-owned), then
# runs the Docker-pool rehearsal end to end: fabricate Dev/QA/UAT copies with
# divergent dirt → capture ×3 → crossover merge with per-winner attribution →
# mint from the merged pack → execute the witness pair (failures = 0) → the
# per-environment fidelity audit (zero blocking failures) → block-equivalence
# live (the FK-add refuses Msg 547 on UAT's orphans; the unique-add refuses
# Msg 1505 on QA's duplicate). The teed log is the rehearsal's dated evidence;
# it lands out of repo (proving-ground/logs is gitignored).
#
# Never run concurrently with the pure pool (the OOM survival rule).
set -euo pipefail

cd "$(dirname "$0")/.."

eval "$(bash scripts/warm-sql.sh start)"
export PROJECTION_MSSQL_CONN_STR

LOG_DIR="ssdt-agent/proving-ground/logs"
mkdir -p "$LOG_DIR"
LOG="$LOG_DIR/crossover-rehearsal-$(date -u +%Y%m%dT%H%M%SZ).log"

dotnet test tests/Twin.Tests.Integration/Twin.Tests.Integration.fsproj \
  --filter "FullyQualifiedName~TwinCrossoverRehearsal" \
  --logger "console;verbosity=normal" \
  2>&1 | tee "$LOG"

echo
echo "rehearsal evidence: $LOG"
