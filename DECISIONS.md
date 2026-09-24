# Decisions — one line each; the reasoning is in the pull request
2026-09-23 · v3 is C#; v2's F# is the specification it is ported from · #703
2026-09-23 · the lifecycle comes first (M1 to M6); the wing (`emit`, `decide`, `move` and the full OSSYS reader) waits for a consumer · #703
2026-09-23 · `predict` joins the lifecycle's verbs and `knowledge` is counted among them; `read --from ossys` moves to the wing · #703
2026-09-23 · git is the only store: every artifact is derived locally from committed inputs and cached by fingerprint, and `twin bake`, `twin restore` and `twin gc` do not exist · #703
2026-09-23 · `net10.0` throughout; the tool is published framework-dependent, and a machine that builds a `.sqlproj` needs the .NET 10 SDK · #703
2026-09-23 · DacFx 170.5.96 is the committed engine until the Octopus step's engine is pinned (S7); every receipt stamps it · #703
2026-09-23 · Docker with the SQL Server image pinned by tag and digest is the substrate; LocalDB is the fallback where Docker is absent, and CDC there reads not provable · #703
2026-09-23 · the SessionStart hook starts Docker and the SQL Server container in remote sessions only, an exception to "no hook starts a daemon" · #703
2026-09-23 · `global.json` pins the 10.0.4xx SDK band with `rollForward: latestPatch` (v1 and v2 pin 9.0.314 with roll-forward disabled) · #704
2026-09-23 · ScriptDom 180.102.0 and Microsoft.Data.SqlClient 6.1.5 are pinned to DacFx 170.5.96's own dependencies · #704
2026-09-23 · `NuGet.config` stays at the root, shared by v1, v2 and v3 · #704
2026-09-23 · `archive/` stays readable until M8, because v2 is the specification ports read; editing it is denied · #704
2026-09-23 · the v2 worktrees under the old sidecar path moved with the archive · #704
2026-09-23 · `Refusal` is a sealed record class, not a struct, because a struct's default would be a refusal with no remedy · #704
2026-09-23 · `Name` compares ordinally with case, so the kernel never loses a case-only difference; a case-insensitive match is the caller's explicit choice · #704
2026-09-23 · `Fingerprint.Of(string)` hashes canonical text (a leading BOM dropped, CRLF and CR to LF, UTF-8); `Of(bytes)` hashes bytes as given · #704
2026-09-23 · `Kernel.Tests` alone runs with invariant globalization off, so a test can show the kernel ignores culture · #704
2026-09-23 · JsonSchema.Net validates the CLI contract's schemas, in tests only · #704
2026-09-23 · `ci/budgets.json` counts physical lines by include globs, `bin/` and `obj/` never; planned figures are data and never fail a build · #704
