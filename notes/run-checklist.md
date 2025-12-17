# Deterministic Run & Verification Checklist

> Use this quick checklist before starting implementation work to confirm the pipeline can build and test end-to-end on the current machine or CI agent.

1. **Validate tooling availability**
   - `dotnet --info` (ensure .NET 9 SDK—or aligned global.json version—is installed and selected).
   - `dotnet --list-sdks` (optional sanity: confirm expected SDK version is discoverable).
   - The solution pins `<LangVersion>preview</LangVersion>` across all projects so the compiler runs with the C# 13 preview feature set. Verify the selected SDK supports the preview compiler if builds fail with language version errors.

2. **Restore solution dependencies**
   - `dotnet restore OutSystemsModelToSql.sln`

3. **Compile once from a clean slate**
   - `dotnet build OutSystemsModelToSql.sln -c Release --no-restore`

4. **Run the full automated test suite**
   - `dotnet test OutSystemsModelToSql.sln -c Release --no-build`
   - The SQL Server integration suites (`tests/Osm.Etl.Integration.Tests` and `tests/Osm.Pipeline.Integration.Tests`) spin up containers via DotNet.Testcontainers. Ensure Docker is available locally before running these projects; if it is not, skip them and record the limitation in your status notes.
   - **If you have trouble running the test suite:** Running tests in Codex CLI will require named pipes, which will require a pipe server inside your sandbox. Ensure you always run those commands with `with_escalated_permissions: true` on the `shell` tool call and include a one-sentence justification (e.g., "Need network access for npm install/build"). Ensure to include the `with_escalated_permissions` for all builds, restores, migrations, installs, tests, etc where network access is required otherwise the command will hang.

5. **(Optional) Smoke the CLI with fixtures**
   - `dotnet run --project src/Osm.Cli -- --in tests/Fixtures/model.edge-case.json` *(uses the edge-case fixture to exercise ingestion summary).* 

> Re-run the restore → build → test steps whenever dependencies change, new projects are added, or before publishing a PR to guarantee deterministic behavior.
