# Opening prompt — build v3 to completion with workflows

**To start a session with this prompt, paste:**

> Use a workflow to build v3 to completion. Follow `V3_BUILD_PROMPT.md` (on `main` once pull
> request #703 merges; until then on branch `claude/v3-architecture-design-sl95mx`).

That sentence is the operator's opt-in to multi-agent orchestration. Everything below is for the
session that receives it.

---

## 0. The job, in one breath

Build v3, the lifecycle engine `V3_MILESTONES.md` specifies, in this repository, milestone by
milestone from M0 to M7. Run one workflow per wave of work packages, stay in the loop between
waves, and keep the state in the repository, never in a workflow's memory. You are done when
every exit that can run here is green on your branch and in CI, and every exit that needs the
estate is recorded with the command a person must run. M8 and the wing (W) wait for the operator.
The plan is complete and has been reviewed adversarially: execute it, do not re-plan it.

---

## 1. Read, in this order

1. `NEXT.md`, if it exists. It is the live state and overrides anything below about where you are.
   It does not exist until M0 lands; until then you are at the start of M0.
2. `V3_MILESTONES.md`: §0; §1 (the measured facts; do not re-derive them); §4 (what the plan
   changes in the design documents); §5 (the milestones and the lanes table, which is the build
   order); the milestone you are on (its work packages and its exits); Appendix F (the budgets).
3. For the work package at hand, and only for it: the sections of `V3_ARCHITECTURE.md` it cites
   (§6.6 for the C# encoding, §7 for the types in F# notation, §8 for the verbs, §13 for the
   laws), of `V3_INSTRUCTION_ARCHITECTURE.md` (§4 values, §6 drafts, §9 the agentic interface,
   §10 the tests), and the requirements of `LIFECYCLE_BACKPORT_PROMPT.md` its exits name.
4. For a port: the v2 files the work package names, under `sidecar/projection/src/` (under
   `archive/v2/src/` after M0), and their tests. v2 is the specification.

Where `V3_MILESTONES.md` §4 says it changes a design document, the plan wins. Everywhere else the
design documents stand.

If pull request #703 is not merged when you start, merge `origin/claude/v3-architecture-design-sl95mx`
into your branch first and say so in your pull request.

---

## 2. What is already true

- **The plan's §1.** Eleven facts, nine of them measured on DacFx 162.5.57 and again on 170.5.96:
  the classic build without Visual Studio, `Script` and `LoadFromDatabase` under a read-only login,
  the guard at `RAISERROR` state 127, the empty plan as the convergence oracle, the generic property
  walk, the profile as options, and foreign-key trust following `ScriptNewConstraintValidation`.
  Turn them into tests (WP 1.8 and the plan's §19); do not measure them again by hand.
- **The container** (this repository's cloud sessions, as of 2026-09-23):
  - 4 CPUs, so a workflow runs at most two agents at once; 15 GB of memory; about 24 GB of free
    disk. The checkout is about 110 MB, so worktrees are cheap.
  - `.claude/hooks/session-start.sh` installs .NET SDK 9.0.314, the version in
    `sidecar/projection/global.json`. No .NET 10 SDK is installed yet.
  - Docker is present, with `mcr.microsoft.com/mssql/server:2022-latest` cached. The hook starts a
    warm SQL Server on port 11433 through `sidecar/projection/scripts/warm-sql.sh`. If `docker ps`
    fails, re-run the hook before concluding anything; Docker comes back in seconds.
  - `api.nuget.org` and `builds.dotnet.microsoft.com` are reachable.
  - The NuGet cache holds DacFx 162.5.57 and 170.5.96. The 170.5.96 package ships `lib/net8.0` and
    `lib/net10.0`, and each has `Microsoft.Data.Tools.Schema.SqlTasks.targets` and
    `Microsoft.Data.Tools.Schema.Tasks.Sql.dll`.
- **Out of reach from here:** the corporate network, meaning the estate repository, Dev, QA, UAT,
  the metamodel, Octopus and the team's laptops. An exit that needs any of them is *pending the
  estate*, never passed and never failed (§6).
- **Windows** (LocalDB on `windows-latest`) is proven only in GitHub Actions. After each push, read
  the run with the GitHub tools.
- **The planning session's spike harness is gone.** Appendix B here and the plan's Appendix E
  rebuild it.

---

## 3. Rules every agent keeps

1. **Branches.** Push only the branch your session designates. Work-package branches (`wp/<id>`,
   `wp/<id>-r<n>`) are local and never pushed. Keep one pull request open for the build, with a
   body that is the milestone checklist, refreshed after every wave. The branch is green at every
   push. When the operator merges it, restart your branch from `main` as your session's git rules
   say, and open the next one.
2. **Tests first.** Every work package's *Done when* becomes a test before the code: it fails, then
   it passes. There is no `Skip`, and warnings are errors.
3. **Budgets are the brake.** `ci/budgets.json` carries the plan's Appendix F from M0. A work
   package that cannot meet its exit under its ceiling stops and asks for one decision line.
4. **Safety and privacy are not negotiable.**
   - The probe allowlist is exactly as WP 1.4 states it.
   - A `Copy` exists only through the registry.
   - A profile contributes deploy options and SQLCMD values only.
   - A failed probe against an environment classified real reports its error number only.
   - No real data appears anywhere. Tests use minted rows or hand-written synthetic ones.
   - No credential appears in any output.
5. **The kernel is pure.** It keeps the banned-symbol list in WP 0.2, and σ takes its generator and
   its week as parameters.
6. **The write budget.**
   - No new documents.
   - `NEXT.md` is rewritten after each wave, never appended to.
   - `DECISIONS.md` gets at most one line per decision.
   - `V3_MILESTONES.md` is corrected only to keep it true, one paragraph at a time, each correction
     with a decision line.
7. **Scope.**
   - Do not port the wing.
   - Do not start M8.
   - After M0's move, do not edit `archive/` except to keep its CI green.
8. **Decisions only the operator can make.** These are the plan's §17 items, a raised ceiling, and
   any change to the plan's §4.
   - An agent that meets one returns `needs-decision` with one question.
   - You record it under *Waiting on a person* in `NEXT.md` and continue with work that does not
     depend on it.
   - You ask the user once, and only when the critical path is blocked.
9. **Attribution and naming.** Use your session's commit and pull-request attribution lines. Put
   no model identifier in any commit, pull request, code or document.

---

## 4. The loop

```
until M7's exits are green here, or pending only on the estate:
    read NEXT.md and the current milestone
    form the next wave (the rule below); do single-path work inline
    run the wave workflow (Appendix A) with args { milestone, branch, final, wps }
    read its result; deal with what it held; refresh the pull request's checklist
    after the push, read CI on both operating systems; a red job is the next wave
```

- **The wave rule.** A wave is a set of work packages whose dependencies are already merged and
  whose files do not overlap, taken in the order of the plan's §5 lanes table. Size it to your
  session's workflow-size guideline. Under the medium guideline, that is two or three work packages
  with one or two verification lenses each. On a 4-CPU container two agents run at once, and larger
  waves only queue.
- **Inline, not in a workflow:**
  - WP 0.1: the archive move touches every path, so do it alone and first.
  - WP 0.2: the skeleton everything else builds on.
  - Any merge conflict the integrator could not resolve.
- **The first waves:**
  - M0: 0.1 inline, then 0.2 inline, then {0.3, 0.4, 0.5}, then {0.6, 0.7}.
  - M1: {1.1, 1.3, 1.8}, then {1.2, 1.5, 1.6}, then {1.4}, then {1.7}, and then M1's exit
    (`final: true`).
  - After that, derive waves the same way.
- **Effort, per work package:**
  - `max` for the kernel types (0.3), the contract (0.4), the executor and allowlist (1.4), profiles
    and credentials (1.5), the claims (2.1), the probes (2.3), σ (3.3), the proof (4.1), and the
    record and the gate (5.1, 5.2).
  - `low` for citation rewrites and moves (7.1).
  - The session default everywhere else.
  - Leave `model` unset unless you have a reason.
- **Verification lenses, per work package:**
  - `spec` for every one.
  - `safety` in addition for 1.4, 1.5, 2.2, 2.3, 2.5, 3.4, 3.7, 4.1, 5.2, 5.4 and 6.1.
  - `budgets` in addition for any work package that adds more than 300 lines.
- **Resume.** If a workflow is interrupted, relaunch it with its `scriptPath` and `resumeFromRunId`.
  If the session ends, the next one resumes from `NEXT.md` and the branch.

---

## 5. What the plan leaves for the first waves to settle

1. **The retired-vocabulary tests.** `Vocabulary` and `Register.Prose` must exclude the four design
   documents and this file until M8, because they have to name v1's and v2's retired terms (WP 0.6).
2. **This file's manifest row.** It needs one (reader: the build session; moment: until M8), in
   WP 0.5.
3. **The SessionStart hook.** Today it reads `sidecar/projection/global.json` and runs
   `sidecar/projection/scripts/warm-sql.sh`, and both move at WP 0.1. Either land WP 0.5's new hook
   in the same push as WP 0.1, or re-path the old hook first. The new hook does two things:
   - it installs .NET 10 (`dotnet-install.sh --channel 10.0`, or the version in the new root
     `global.json`);
   - in remote sessions only, it starts Docker and the SQL Server that `Io.Tests` use. Record that
     as a decision line: it is an exception to "no hook starts a daemon".
4. **`Io.Tests`' SQL fixture.** Use one shared SQL Server, with a registered database per test
   (`estate_<host>_<pid>_<rand>`) so concurrent verifier agents never collide:
   - the warm container here;
   - Testcontainers when none is running;
   - LocalDB on Windows CI.
5. **The build route's runtime.** MSBuild loads `SqlBuildTask` into its own runtime. A tool folder
   published for `net10.0` therefore carries a task assembly that only a .NET 10 SDK can load. If
   S6 finds a laptop or agent without one, the tool folder also needs DacFx's `net8.0` build
   assemblies as its targets path: a `build/` subfolder published from a small `net8.0` project, as
   in Appendix B. Measure this in WP 0.7 on both CI operating systems.
6. **Concurrency.** The plan's four lanes run two at a time here, so expect the calendar to stretch.
   Record actual dates in `NEXT.md`, not predictions.

---

## 6. Pending the estate, and the questions to send

Report every exit that needs the corporate network as
`pending: <exit> — <the exact command> — <who runs it>`. Collect the questions for the corporate
agent's `STATE.md` under *Waiting on a person* in `NEXT.md`, each with its command:

- S1's laptop half;
- S3;
- S5;
- S6;
- S7.

The estate adopts v3 through the vendoring pull request, as the plan's §15 describes. Nothing from
this repository reaches it any other way.

---

## 7. Done

- M0 to M7:
  - every exit that can run here is green on the designated branch and in CI on both operating
    systems;
  - every other exit is recorded as pending, with its command.
- Every budget is under its ceiling.
- These are green, as the plan's Appendix C restates them: laws 2′, 3′, 5′ and 6 to 12, and laws P
  and T.
- The instruction architecture's tests are green.
- `dist/estate/` publishes, and `estate knowledge vendor --to <a scratch estate>` produces the
  vendoring pull request's content.
- `NEXT.md` lists only *Waiting on a person*.
- The build pull request is ready for the operator, and is merged when they say so.

---

## 8. After each wave, report in this shape

```
MILESTONE  M<n>, wave <k>
LANDED     <work package> — <commit> — <the test that pins it>
HELD       <work package> — <why> — <next step>
PENDING    <exit> — <command> — <who>
CI         ubuntu <green | red> · windows <green | red> — <run link>
NEXT       <the next wave>
```

---

## Appendix A — The wave workflow

Adapt it, then pass it to the Workflow tool inline. `args.wps` is the wave: for each work package,
its `id`, `title`, `lenses` and `effort`. Set `final: true` on a milestone's last wave, so the exit
checks run.

```js
export const meta = {
  name: 'v3-wave',
  description: 'Build one wave of V3_MILESTONES.md work packages: implement in worktrees, verify, integrate, check the milestone exit',
  phases: [
    { title: 'Implement', detail: 'one agent per work package, test first, in its own worktree' },
    { title: 'Verify', detail: 'fresh skeptical agents per lens; fix and re-verify at most twice' },
    { title: 'Integrate', detail: 'merge passing branches in order, run the suites, push' },
    { title: 'Exit', detail: 'milestone exit checks, gaps, NEXT.md' },
  ],
}

// args: { milestone: 'M1', branch: '<designated branch>', final: false,
//         wps: [ { id: '1.4', title: 'io/SqlServer and a minimal io/Substrate', lenses: ['spec', 'safety'], effort: 'max' } ] }

const WP_RESULT = {
  type: 'object',
  properties: {
    id: { type: 'string' },
    status: { type: 'string', enum: ['done', 'blocked', 'needs-decision'] },
    branch: { type: 'string' },
    commit: { type: 'string' },
    tests: { type: 'array', items: { type: 'string' } },
    commands: { type: 'array', items: { type: 'string' } },
    notes: { type: 'string' },
    question: { type: 'string' },
  },
  required: ['id', 'status', 'branch', 'notes'],
}
const VERDICT = {
  type: 'object',
  properties: {
    pass: { type: 'boolean' },
    findings: {
      type: 'array',
      items: {
        type: 'object',
        properties: {
          severity: { type: 'string', enum: ['must-fix', 'should-fix', 'nit'] },
          where: { type: 'string' },
          evidence: { type: 'string' },
          fix: { type: 'string' },
        },
        required: ['severity', 'where', 'evidence', 'fix'],
      },
    },
  },
  required: ['pass', 'findings'],
}
const INTEGRATION = {
  type: 'object',
  properties: {
    merged: { type: 'array', items: { type: 'string' } },
    backedOut: { type: 'array', items: { type: 'string' } },
    pushedCommit: { type: 'string' },
    suites: { type: 'string' },
  },
  required: ['merged', 'backedOut', 'suites'],
}
const EXIT = {
  type: 'object',
  properties: {
    checks: {
      type: 'array',
      items: {
        type: 'object',
        properties: {
          exit: { type: 'string' },
          state: { type: 'string', enum: ['pass', 'fail', 'pending-estate'] },
          command: { type: 'string' },
          evidence: { type: 'string' },
        },
        required: ['exit', 'state', 'command'],
      },
    },
    gaps: { type: 'array', items: { type: 'string' } },
  },
  required: ['checks', 'gaps'],
}

const LENS = {
  spec: 'Does the branch do exactly what the work package\'s What and Done-when say in V3_MILESTONES.md, and do its tests assert the Done-when itself rather than something weaker?',
  safety: 'Can any path write to a named environment, publish Permissive outside a registered Copy, return or log a row value from an environment classified real, or print a credential? Hold the code to WP 1.4 and WP 1.5 word for word.',
  budgets: 'Do Budgets, NoSkips, the dependency laws and the banned-symbol build pass, and do the files stay within Appendix F?',
}
const lensesOf = wp => (wp.lenses && wp.lenses.length ? wp.lenses : ['spec'])
const effortOf = wp => (wp.effort ? { effort: wp.effort } : {})

const implement = wp => agent(
  [
    `Implement work package ${wp.id} (${wp.title}) of V3_MILESTONES.md, milestone ${args.milestone}.`,
    `Read its row and the sections it cites, and nothing unrelated. Create local branch wp/${wp.id} from ${args.branch}.`,
    'Write the test that encodes its Done-when first and see it fail; then write the code until it passes.',
    'Run dotnet build (warnings are errors), dotnet test --filter Category=fast, and the Io.Tests the work package touches.',
    'Stay inside Appendix F for your files. Commit on the branch. Never push.',
    'If it needs a decision only the operator can make, stop and return needs-decision with one question.',
  ].join('\n'),
  { label: `implement ${wp.id}`, phase: 'Implement', schema: WP_RESULT, isolation: 'worktree', ...effortOf(wp) })

const verify = (wp, branch, lens) => agent(
  [
    `Verify work package ${wp.id} at local branch ${branch}: run git checkout --detach ${branch} in your worktree.`,
    LENS[lens],
    'Run the tests yourself; do not rely on the implementer\'s report. If you cannot confirm a claim, pass is false.',
  ].join('\n'),
  { label: `verify ${wp.id} (${lens})`, phase: 'Verify', schema: VERDICT, isolation: 'worktree' })

const fix = (wp, branch, round, findings) => agent(
  [
    `Create local branch wp/${wp.id}-r${round} from ${branch}, fix these findings for work package ${wp.id}, re-run the tests, and commit. Never push.`,
    JSON.stringify(findings),
  ].join('\n'),
  { label: `fix ${wp.id} (round ${round})`, phase: 'Verify', schema: WP_RESULT, isolation: 'worktree', ...effortOf(wp) })

phase('Implement')
const outcomes = await pipeline(
  args.wps,
  wp => implement(wp),
  async (first, wp) => {
    if (!first || first.status !== 'done') return { wp, result: first }
    let result = first
    for (let round = 1; round <= 3; round++) {
      const lenses = lensesOf(wp)
      const verdicts = (await parallel(lenses.map(lens => () => verify(wp, result.branch, lens)))).filter(Boolean)
      const open = verdicts.flatMap(v => v.findings).filter(f => f.severity !== 'nit')
      const clean = verdicts.length === lenses.length && verdicts.every(v => v.pass) && !open.some(f => f.severity === 'must-fix')
      if (clean) return { wp, result, verdicts }
      if (round === 3) return { wp, result: { ...result, status: 'blocked', notes: 'verification did not converge in two fix rounds' }, verdicts }
      result = (await fix(wp, result.branch, round, open)) || result
    }
    return { wp, result }
  },
)

const ready = outcomes.filter(o => o && o.result && o.result.status === 'done')
const held = outcomes.filter(o => o && !(o.result && o.result.status === 'done'))
held.forEach(o => log(`held ${o.wp.id}: ${o.result ? o.result.status + ' — ' + o.result.notes : 'no result'}`))

phase('Integrate')
const integration = ready.length === 0 ? null : await agent(
  [
    `In the main checkout on ${args.branch}, merge these local branches in this order: ${ready.map(o => o.result.branch).join(', ')}.`,
    'Resolve conflicts in shared files (Estate.sln, Directory.Packages.props, ci/budgets.json, ci/docs.manifest.json) by keeping both sides\' intent.',
    'After each merge run dotnet build and dotnet test --filter Category=fast; after the last, the SQL lane\'s Io.Tests.',
    'If a merge breaks either, back it out and report it.',
    `Then push ${args.branch}, delete the merged local wp/ branches, and remove this wave's worktrees.`,
  ].join('\n'),
  { label: 'integrate', phase: 'Integrate', schema: INTEGRATION })

phase('Exit')
const exit = !args.final ? null : await agent(
  [
    `Milestone ${args.milestone}: for each Exit check in V3_MILESTONES.md, run it here if it can run here, and record pass or fail with the command and its evidence.`,
    'If it needs the estate, a team laptop, Dev, the metamodel or Octopus, record pending-estate with the exact command a person must run.',
    `Compare the milestone's work packages and its rows in Appendices A to D with what is on ${args.branch}, and list every gap.`,
    `Rewrite NEXT.md (in flight, next, waiting on a person), add at most one line to DECISIONS.md, commit, and push ${args.branch}.`,
  ].join('\n'),
  { label: `exit ${args.milestone}`, phase: 'Exit', schema: EXIT, effort: 'high' })

return {
  milestone: args.milestone,
  landed: integration ? integration.merged : [],
  held: held.map(o => ({
    id: o.wp.id,
    status: o.result ? o.result.status : 'none',
    notes: o.result ? o.result.notes : '',
    question: o.result && o.result.question ? o.result.question : '',
  })),
  integration,
  exit,
}
```

A typical wave of three work packages (four lenses in all) spends nine agents: three implement,
four verify, one integrates and one checks the exit. A fix round adds a fix agent and a verify
agent per lens.

---

## Appendix B — Rebuilding the classic build (§1 fact 1)

This recipe was re-run as written on 2026-09-23. It builds a classic, Visual Studio-format project
with `dotnet build` and a published DacFx folder, with no Visual Studio. It is the seed of
`tests/Golden/classic-minimal/` (WP 0.7) and of the tool folder's build route.

**`Probe.sqlproj`**

```xml
<?xml version="1.0" encoding="utf-8"?>
<Project DefaultTargets="Build" xmlns="http://schemas.microsoft.com/developer/msbuild/2003" ToolsVersion="4.0">
  <Import Project="$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\Microsoft.Common.props" Condition="Exists('$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\Microsoft.Common.props')" />
  <PropertyGroup>
    <Configuration Condition=" '$(Configuration)' == '' ">Debug</Configuration>
    <Platform Condition=" '$(Platform)' == '' ">AnyCPU</Platform>
    <Name>Probe</Name>
    <SchemaVersion>2.0</SchemaVersion>
    <ProjectVersion>4.1</ProjectVersion>
    <ProjectGuid>{2B0C6A43-7E1B-4C8E-9C64-2E5C3B8B7A11}</ProjectGuid>
    <DSP>Microsoft.Data.Tools.Schema.Sql.Sql160DatabaseSchemaProvider</DSP>
    <OutputType>Database</OutputType>
    <RootNamespace>Probe</RootNamespace>
    <AssemblyName>Probe</AssemblyName>
    <ModelCollation>1033, CI</ModelCollation>
    <DefaultFileStructure>BySchemaAndSchemaType</DefaultFileStructure>
    <TargetFrameworkVersion>v4.7.2</TargetFrameworkVersion>
    <TargetDatabaseSet>True</TargetDatabaseSet>
  </PropertyGroup>
  <PropertyGroup Condition=" '$(Configuration)|$(Platform)' == 'Release|AnyCPU' ">
    <OutputPath>bin\Release\</OutputPath>
    <BuildScriptName>$(MSBuildProjectName).sql</BuildScriptName>
    <TreatWarningsAsErrors>False</TreatWarningsAsErrors>
  </PropertyGroup>
  <PropertyGroup Condition=" '$(Configuration)|$(Platform)' == 'Debug|AnyCPU' ">
    <OutputPath>bin\Debug\</OutputPath>
    <BuildScriptName>$(MSBuildProjectName).sql</BuildScriptName>
  </PropertyGroup>
  <PropertyGroup>
    <VisualStudioVersion Condition="'$(VisualStudioVersion)' == ''">11.0</VisualStudioVersion>
  </PropertyGroup>
  <Import Condition="'$(SQLDBExtensionsRefPath)' != ''" Project="$(SQLDBExtensionsRefPath)\Microsoft.Data.Tools.Schema.SqlTasks.targets" />
  <Import Condition="'$(SQLDBExtensionsRefPath)' == ''" Project="$(MSBuildExtensionsPath)\Microsoft\VisualStudio\v$(VisualStudioVersion)\SSDT\Microsoft.Data.Tools.Schema.SqlTasks.targets" />
  <ItemGroup>
    <Folder Include="Properties" />
    <Folder Include="dbo\" />
    <Folder Include="dbo\Tables\" />
  </ItemGroup>
  <ItemGroup>
    <Build Include="dbo\Tables\Customer.sql" />
  </ItemGroup>
  <ItemGroup>
    <RefactorLog Include="Probe.refactorlog" />
  </ItemGroup>
  <ItemGroup>
    <PostDeploy Include="Script.PostDeployment.sql" />
  </ItemGroup>
</Project>
```

**`dbo/Tables/Customer.sql`**, **`Script.PostDeployment.sql`**, **`Probe.refactorlog`**

```sql
CREATE TABLE [dbo].[Customer]
(
    [Id] INT NOT NULL CONSTRAINT [PK_Customer] PRIMARY KEY,
    [GivenName] NVARCHAR(100) NULL
);
```

```sql
PRINT N'post-deploy ran';
```

```xml
<?xml version="1.0" encoding="utf-8"?>
<Operations Version="1.0" xmlns="http://schemas.microsoft.com/sqlserver/dac/Serialization/2012/02">
  <Operation Name="Rename Refactor" Key="3f6a2c1e-8a4b-4d1e-9d2f-7c0b1a2e3d4f" ChangeDateTime="09/23/2026 10:00:00">
    <Property Name="ElementName" Value="[dbo].[Customer].[FirstName]" />
    <Property Name="ElementType" Value="SqlSimpleColumn" />
    <Property Name="ParentElementName" Value="[dbo].[Customer]" />
    <Property Name="ParentElementType" Value="SqlTable" />
    <Property Name="NewName" Value="[GivenName]" />
  </Operation>
</Operations>
```

**The tool folder, the reference stub, and the build**

```bash
# 1. A published app that references DacFx: its output folder holds DacFx and every dependency.
#    (net8.0 here, so any .NET SDK of 8 or later can load the build task; see §5 item 5.)
mkdir tool && cat > tool/Tool.csproj <<'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <RollForward>Major</RollForward>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.SqlServer.DacFx" Version="170.5.96" />
  </ItemGroup>
</Project>
EOF
printf 'System.Console.WriteLine(typeof(Microsoft.SqlServer.Dac.DacServices).Assembly.GetName().Version);\n' > tool/Program.cs
dotnet publish tool -c Release -o tool/out
cp ~/.nuget/packages/microsoft.sqlserver.dacfx/170.5.96/lib/net8.0/Microsoft.Data.Tools.Schema.SqlTasks*.targets tool/out/

# 2. The reference stub: one reference assembly and its framework list.
curl -sSfL -o refasm.nupkg \
  https://api.nuget.org/v3-flatcontainer/microsoft.netframework.referenceassemblies.net472/1.0.3/microsoft.netframework.referenceassemblies.net472.1.0.3.nupkg
unzip -q refasm.nupkg -d refasm-pkg
mkdir -p stub/.NETFramework/v4.7.2/RedistList
cp refasm-pkg/build/.NETFramework/v4.7.2/mscorlib.dll stub/.NETFramework/v4.7.2/
cp refasm-pkg/build/.NETFramework/v4.7.2/RedistList/FrameworkList.xml stub/.NETFramework/v4.7.2/RedistList/

# 3. The build: the dacpac carries model.xml, refactor.xml and postdeploy.sql.
dotnet build Probe.sqlproj -c Release -p:DacFxTelemetryEnabled=false \
  -p:NetCoreBuild=true -p:NETCoreTargetsPath="$PWD/tool/out" -p:SQLDBExtensionsRefPath="$PWD/tool/out" \
  -p:TargetFrameworkRootPath="$PWD/stub"
unzip -l bin/Release/Probe.dacpac
```

If the package's `lib` folder is passed as the targets path instead of the published folder, the
build fails with `SqlBuildTask returned false but did not log an error`: the task needs DacFx's
dependencies beside it.
