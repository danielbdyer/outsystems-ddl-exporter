# Agent Guidelines

## Supreme Operating Discipline (V2 sidecar / `sidecar/projection/`)

If you are working in `sidecar/projection/` (the F# V2 sidecar), the
canonical first-read is `sidecar/projection/KICKOFF.md`. The supreme
operating discipline lives at the top of `sidecar/projection/DECISIONS.md`
as seven pillars. Read the supreme discipline section in full before
adopting any pattern.

The pillars in shortest form:
1. **Data-structure-oriented over string-parsing** — typed values flow through; strings emerge ONLY at the absolute terminal BCL writer boundary.
2. **Avoid string concatenation aggressively** — every `String.concat` / `String.Concat` / `String.Format` / `sprintf` / `+` / interpolated string is flagged; per-site analysis required.
3. **Built-in obligation** — when a BCL or vendor SDK emits the structure being emitted, agents are obliged to use it (`Sql160ScriptGenerator`, `XmlWriter`, `Utf8JsonWriter`, `UuidV5`, etc.).
4. **Promised land of FP** — ≥95% pure functions; ≤5% mutation isolated, reified, exhaustively tested.
5. **Coding-style commitments** — deep DDD, point-free composition, hexagonal architecture, hardcore FP (closed DUs, smart constructors, no `null`, monadic composition), OOP only at boundary code where BCL forces it, deep separation of concerns, verifiable + observable to the nth degree.
6. **No V2-internal back-compat paths** — refactor fully at time of insight, no exceptions.
7. **Gold-standard library precedence** — use-case-specific library FIRST; typed data structures SECOND; `StructuredString` THIRD; documented `LINT-ALLOW` LAST. Plus the perf-clause: every refactor cites perf implications; every hot-path function has `Bench.scope`; every loop flows through `Bench` iterators; every counter via `Bench.recordSample`.

### LINT-ALLOW substantive-rationale (the failure mode named "performance-of-compliance")

Pillar 7 amendment, codified 2026-05-10 chapter 3.7 sidebar. **Every
`LINT-ALLOW` marker on a string-composition or built-in-substitute
site MUST embody the four-question analysis BEFORE the marker is
committed:**

1. **Use-case-specific library for THIS output structure?** Name it
   explicitly (module + type + function). If a vendor SDK or BCL
   primitive emits the structure you're composing, the discipline
   says use it.
2. **Already in the codebase** (or available as a non-V2-back-compat
   dep)? If yes, name the existing consumer site so the precedent is
   visible. If no, name the package + version that would land it.
3. **Cost of using it here?** Visibility lift (LOC), perf class
   (zero / O(1) / O(N) / O(N log N) / O(N²) per-call delta + bench
   label), dep weight (transitive package size). The cost analysis
   IS the perf-clause cash-out at this site.
4. **Structural reason it doesn't apply?**
   - **NO** → there is no shortcut; do the work (lift visibility,
     add the helper, refactor the call site).
   - **YES** → the marker text MUST name the SPECIFIC reason — not
     generic vocabulary alone ("typed segments", "boundary" without
     naming WHICH boundary, etc.).

**Performance-of-compliance is the named failure mode**: a marker
with the SHAPE of an audit trail but without the substance. The lint
passes, the vocabulary fits, the tests are green — and the
structural commitment is unmet. The asymmetry is structural: V2 is
the trust anchor for the high-stakes cutover; every shortcut
introduces a runtime-only invariant that future drift forces are
waiting to surface. The cost of doing the work is paid once; the
cost of carrying the shortcut compounds across every reader.

**Worked counterfactual** (DECISIONS 2026-05-10): slice-β added four
`String.Concat` LINT-ALLOWs in `Render.columnSqlType` reading "terminal
SQL DDL emission boundary; both segments are typed (closed-DU dispatch
+ literal)" — discipline vocabulary without the analysis. Operator
caught it on review. Slice-β' lifted `ScriptDomBuild.dataTypeReference`
from `private` to public, added `generateDataType : DataTypeReference
-> string`, made Render delegate. Cost: 87 LOC across 3 files; output
byte-identical; perf-gate clean. The "do the work" path was trivial
compared to the structural drift the shortcut would have introduced.

See `sidecar/projection/PLAYBOOK.md` decision tree
"When you reach for a string-composition primitive" for the executable
form.

## Pre-flight & Alignment
- Begin every working session by executing the `notes/run-checklist.md` steps (install/verify .NET 9 SDK, restore, build, test, optional CLI smoke) so the environment is proven green before coding; document any deviations.
- **If a tool appears missing (e.g., `which dotnet` empty, `docker` unreachable), check `.claude/hooks/session-start.sh` and `$HOME/.claude-projection-hook-status` BEFORE reaching for `apt install` / curl-based installs.** The session-start hook is the canonical setup routine for web sessions; re-running it is the supported recovery path. The status log records prior runs so agents can detect silent failures across sessions.
- Always consult `tasks.md` at the start of each task to stay aligned with the current execution plan; update it when new work items emerge.
- Revisit `architecture-guardrails.md` whenever making design or implementation decisions to ensure architectural guardrails are upheld.
- Reference the checklist, task backlog, and guardrails in status updates or planning summaries so stakeholders can trace work back to the roadmap and guardrails.

## Navigation Aids (use these before spelunking)
- `notes/meta/directory-map.md` – one-page map of repo layers and where key responsibilities live.
- `notes/meta/rg-signposts.md` – copy/paste ripgrep commands for the policy surface, SMO emission code, evidence cache, and CLI wiring.
- `notes/meta/toggle-surface.md` – table of tightening toggle keys, meanings, and extension steps for new flags.
- `notes/meta/test-matrix.md` – project-by-project test command cheatsheet (unit + integration + CLI smoke) so you can cite exact coverage in PRs.

Keep these references handy whenever you need to cite locations, tests, or toggles; doing so avoids wasting tokens on exploratory shell work.
