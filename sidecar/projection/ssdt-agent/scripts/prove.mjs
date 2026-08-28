#!/usr/bin/env node
// prove.mjs — the packaged proving loop: build → delta → Strict publish → structured verdict.
//
// This is the one-command form of the sequence `skills/prove-on-dacpac/SKILL.md` scaffolds,
// executing the connector named in `CONNECTORS.md` §4. The two-profile discipline survives
// intact: Strict surfaces whether the deployment is blocked; `--permissive` (run only after a
// block) proceeds past it so the consequence can be observed; the data probes and the
// content-hash snapshot remain skill-owned (`skills/talk-to-local-sql/SKILL.md`).
//
// Surface-agnostic on purpose (see `../PORTABILITY.md`):
//   - The SQL engine is any reachable endpoint — LocalDB, a local SQL Server, or a container
//     someone happens to run. Docker is one substrate, never a requirement.
//   - The build fork (classic .sqlproj → msbuild; SDK-style → dotnet build) is detected from
//     the project file, not assumed from the machine.
//   - No shell is spawned, so no Git Bash path mangling; the .NET roll-forward shim is set by
//     the tool, not by folklore.
//   - stdout carries exactly one JSON verdict object; human progress goes to stderr; the exit
//     code carries the verdict class. A BLOCKED publish is a finding (exit 3), never a tool
//     error.
//
// Exit codes (aligned with the twin CLI's vocabulary where they overlap):
//   0 published clean · 3 blocked by the engine (the finding) · 1 argv · 4 target/tool
//   unreachable or timed out · 6 config · 7 build failed · 9 indeterminate output.
//
// Zero dependencies. Node >= 18. Lives in the tree so it travels on vendor and on eject.

import { spawnSync } from 'node:child_process';
import { existsSync, readFileSync, writeFileSync, mkdirSync, readdirSync, statSync } from 'node:fs';
import { dirname, join, resolve, basename } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const TOOL_VERSION = '0.1.0';
export const TREE_ROOT = dirname(dirname(fileURLToPath(import.meta.url)));

export const EXIT = { OK: 0, ARGV: 1, BLOCKED: 3, UNREACHABLE: 4, CONFIG: 6, BUILD: 7, INDETERMINATE: 9 };

const log = (line) => process.stderr.write(`prove: ${line}\n`);
export const fail = (code, line) => { process.stderr.write(`prove: ${line}\n`); process.exit(code); };

// ---------------------------------------------------------------------------
// argv
// ---------------------------------------------------------------------------

export function parseArgs(argv) {
  const args = { _: [] };
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    if (!a.startsWith('--')) { args._.push(a); continue; }
    const eq = a.indexOf('=');
    if (eq > 0) { args[a.slice(2, eq)] = a.slice(eq + 1); continue; }
    const key = a.slice(2);
    const flagOnly = ['permissive', 'script-only', 'json', 'help'];
    if (flagOnly.includes(key)) { args[key] = true; continue; }
    args[key] = argv[++i];
    if (args[key] === undefined) fail(EXIT.ARGV, `--${key} needs a value`);
  }
  return args;
}

const USAGE = `prove — build the project, generate the real delta, publish under Strict, report the verdict.

usage:
  node prove.mjs detect  [--project <path.sqlproj>] [--config <prove.config.json>]
  node prove.mjs verdict [--project <path.sqlproj>] [--config <path>] [--db <name>]
                         [--dacpac <path>] [--script-only] [--permissive] [--timeout <seconds>]
  node prove.mjs selftest

verdict flow: build (dotnet or msbuild, detected from the project) -> sqlpackage /Action:Script
(the delta, always kept) -> sqlpackage /Action:Publish under the Strict profile -> classify the
printed outcome. --permissive additionally publishes under the Permissive profile AFTER a block,
so the consequence the block was protecting against can be observed on the disposable copy.

stdout: one JSON verdict object. stderr: progress. exit: 0 clean, 3 blocked, 1 argv, 4 target or
tool unreachable, 6 config, 7 build failed, 9 indeterminate.
The target database must be DISPOSABLE. Never point this tool at a shared or real environment.`;

// ---------------------------------------------------------------------------
// substrate resolution: explicit config first, then the detection ladder
// ---------------------------------------------------------------------------

function readJson(path) {
  try { return JSON.parse(readFileSync(path, 'utf8')); }
  catch (e) { fail(EXIT.CONFIG, `${path}: does not parse as JSON (${e.message})`); }
}

function resolvePassword(target, origin) {
  if (target.passwordEnv) {
    const v = process.env[target.passwordEnv];
    if (!v) fail(EXIT.CONFIG, `${origin}: target.passwordEnv names ${target.passwordEnv}, which is not set in this shell`);
    return v;
  }
  return target.password; // built-in substrate defaults only; a config file should use passwordEnv
}

export function connectionString(target, dbOverride) {
  const db = dbOverride || target.database;
  const parts = [`Server=${target.server}`, `Initial Catalog=${db}`];
  if (target.auth === 'integrated') parts.push('Integrated Security=True');
  else { parts.push(`User ID=${target.user || 'sa'}`); parts.push(`Password=${resolvePassword(target, target.origin)}`); }
  parts.push('TrustServerCertificate=True');
  if (target.encrypt === undefined || target.encrypt === false) parts.push('Encrypt=False');
  return parts.join(';');
}

function findConfig(explicit, projectDir) {
  if (explicit) {
    const p = resolve(explicit);
    if (!existsSync(p)) fail(EXIT.CONFIG, `--config ${explicit}: no such file`);
    return p;
  }
  for (const dir of [process.cwd(), projectDir]) {
    if (!dir) continue;
    const p = join(dir, 'prove.config.json');
    if (existsSync(p)) return p;
  }
  return null;
}

function commandExists(cmd) {
  const probe = spawnSync(cmd, ['--version'], { encoding: 'utf8', timeout: 15000 });
  return !probe.error;
}

const CONFIG_TEMPLATE = `{
  "project": "YourProject.sqlproj",
  "build": "auto",
  "profiles": { "strict": "Local.Strict.publish.xml", "permissive": "Local.Permissive.publish.xml" },
  "target": { "server": "(localdb)\\\\MSSQLLocalDB", "database": "ProvingCopy", "auth": "integrated" }
}
(for a SQL-auth engine instead: "target": { "server": "localhost,1433", "database": "ProvingCopy",
 "user": "sa", "passwordEnv": "PROVE_SQL_PASSWORD" })`;

export function resolveSubstrate(args, projectDir) {
  // 1 — the committed answer: prove.config.json (the estate's own substrate declaration).
  const configPath = findConfig(args.config, projectDir);
  if (configPath) {
    const cfg = readJson(configPath);
    const dir = dirname(configPath);
    if (!cfg.target || !cfg.target.server || !cfg.target.database)
      fail(EXIT.CONFIG, `${configPath}: target.server and target.database are required`);
    cfg.target.origin = configPath;
    return {
      kind: 'config', origin: configPath,
      project: cfg.project ? resolve(dir, cfg.project) : null,
      build: cfg.build || 'auto',
      profiles: cfg.profiles
        ? { strict: cfg.profiles.strict && resolve(dir, cfg.profiles.strict),
            permissive: cfg.profiles.permissive && resolve(dir, cfg.profiles.permissive) }
        : null,
      target: cfg.target,
    };
  }
  // 2 — the Twin substrate: twin.json beside the project and the twin CLI reachable.
  if (projectDir && existsSync(join(projectDir, 'twin.json')) && commandExists('twin')) {
    const twinCfg = readJson(join(projectDir, 'twin.json'));
    const port = (twinCfg.container && twinCfg.container.port) || 21433;
    return {
      kind: 'twin', origin: join(projectDir, 'twin.json'), project: null, build: 'auto', profiles: null,
      target: { server: `localhost,${port}`, database: 'twin', user: 'sa',
                password: 'Twin@Strong1', origin: 'twin defaults' },
    };
  }
  // 3 — the source repository's warm container (Docker is incidental: it is just an endpoint).
  if (existsSync(join(TREE_ROOT, '..', 'scripts', 'warm-sql.sh'))) {
    return {
      kind: 'warm', origin: 'scripts/warm-sql.sh (source repository layout)', project: null, build: 'auto', profiles: null,
      target: { server: 'localhost,11433', database: 'ProvingGround', user: 'sa',
                password: 'Projection@Strong1', origin: 'warm-container defaults' },
    };
  }
  fail(EXIT.CONFIG,
    `no substrate found. Write a prove.config.json beside the project (or pass --config):\n${CONFIG_TEMPLATE}`);
}

// ---------------------------------------------------------------------------
// project + profiles + build
// ---------------------------------------------------------------------------

export function resolveProject(args, substrate) {
  const candidate = args.project || (substrate && substrate.project)
    || (existsSync(join(TREE_ROOT, 'proving-ground', 'SampleCatalog.sqlproj'))
        ? join(TREE_ROOT, 'proving-ground', 'SampleCatalog.sqlproj') : null);
  if (!candidate) fail(EXIT.CONFIG, 'no project: pass --project <path.sqlproj> or set "project" in prove.config.json');
  const p = resolve(candidate);
  if (!existsSync(p)) fail(EXIT.CONFIG, `project ${p}: no such file`);
  return p;
}

export function resolveProfiles(substrate, projectDir) {
  if (substrate.profiles && substrate.profiles.strict) return substrate.profiles;
  const defaults = {
    strict: join(projectDir, 'profiles', 'ProvingGround.Strict.publish.xml'),
    permissive: join(projectDir, 'profiles', 'ProvingGround.Permissive.publish.xml'),
  };
  if (existsSync(defaults.strict)) return defaults;
  fail(EXIT.CONFIG, `no Strict publish profile found (looked for ${defaults.strict}); name one in prove.config.json`);
}

export function sh(cmd, cmdArgs, opts = {}) {
  const env = { ...process.env, DOTNET_ROLL_FORWARD: process.env.DOTNET_ROLL_FORWARD || 'Major' };
  if (!env.DOTNET_ROOT && existsSync(join(process.env.HOME || '', '.dotnet', 'dotnet')))
    env.DOTNET_ROOT = join(process.env.HOME, '.dotnet');
  const r = spawnSync(cmd, cmdArgs, {
    encoding: 'utf8', env, maxBuffer: 64 * 1024 * 1024,
    timeout: (opts.timeoutSeconds || 300) * 1000, cwd: opts.cwd,
  });
  if (r.error && r.error.code === 'ENOENT')
    fail(EXIT.UNREACHABLE, `${cmd}: not found on PATH. ${opts.enoentHint || ''}`);
  if (r.error && r.error.code === 'ETIMEDOUT')
    fail(EXIT.UNREACHABLE,
      `${cmd} timed out after ${opts.timeoutSeconds || 300}s — on a local container this usually means the ` +
      `engine degraded, not that the change is wrong; restart it and re-run.`);
  return r;
}

export function detectBuildMode(projectPath, declared) {
  if (declared && declared !== 'auto') return declared;
  const text = readFileSync(projectPath, 'utf8');
  return /Sdk\s*=\s*"Microsoft\.Build\.Sql/i.test(text) ? 'dotnet' : 'msbuild';
}

function newestDacpac(dir) {
  if (!existsSync(dir)) return null;
  let best = null;
  for (const entry of readdirSync(dir, { recursive: true })) {
    const p = join(dir, String(entry));
    if (p.endsWith('.dacpac') && statSync(p).isFile()) {
      const m = statSync(p).mtimeMs;
      if (!best || m > best.mtime) best = { path: p, mtime: m };
    }
  }
  return best && best.path;
}

export function build(projectPath, mode, timeoutSeconds) {
  const projectDir = dirname(projectPath);
  log(`building (${mode}): ${basename(projectPath)}`);
  const r = mode === 'dotnet'
    ? sh('dotnet', ['build', projectPath, '-c', 'Release', '--nologo', '-v', 'q'],
         { timeoutSeconds, enoentHint: 'install the .NET SDK, or pass --dacpac <prebuilt>.' })
    : sh('msbuild', [projectPath, '/p:Configuration=Release', '/v:minimal', '/nologo'],
         { timeoutSeconds, enoentHint: 'run from a Visual Studio Developer shell (classic .sqlproj needs MSBuild), or pass --dacpac <prebuilt>.' });
  if (r.status !== 0) {
    process.stderr.write(r.stdout.slice(-4000) + r.stderr.slice(-2000));
    fail(EXIT.BUILD, `build failed (${mode}, exit ${r.status})`);
  }
  const dacpac = newestDacpac(join(projectDir, 'bin', 'Release')) || newestDacpac(join(projectDir, 'bin'));
  if (!dacpac) fail(EXIT.BUILD, `build succeeded but no .dacpac found under ${join(projectDir, 'bin')}`);
  return dacpac;
}

// ---------------------------------------------------------------------------
// sqlpackage + classification
// ---------------------------------------------------------------------------

export function sqlpackageVersion() {
  const r = sh('sqlpackage', ['/version'], { timeoutSeconds: 60, enoentHint: 'dotnet tool install --global microsoft.sqlpackage' });
  return (r.stdout || '').trim().split('\n').pop();
}

export function runSqlpackage(action, dacpac, profile, conn, outPath, timeoutSeconds) {
  const args = [`/Action:${action}`, `/SourceFile:${dacpac}`, `/Profile:${profile}`, `/TargetConnectionString:${conn}`];
  if (outPath) args.push(`/OutputPath:${outPath}`, '/OverwriteFiles:True');
  return sh('sqlpackage', args, { timeoutSeconds, enoentHint: 'dotnet tool install --global microsoft.sqlpackage' });
}

// The block lives in the TEXT, never the exit code (prove-on-dacpac; PROTOCOL §0).
export function classifyPublish(output) {
  const messages = [];
  for (const line of output.split('\n')) {
    const t = line.trim();
    if (/^(Error|Warning)\s+SQL\d+/.test(t) || /^Msg \d+, Level \d+, State \d+/.test(t) ||
        /^\*\*\* / .test(t) || /Initializing deployment .* failed/.test(t)) messages.push(t);
  }
  if (/Could not deploy package/.test(output)) return { outcome: 'blocked', messages };
  if (/Successfully published database/.test(output)) return { outcome: 'published', messages };
  if (/Unable to connect|error occurred while communicating|Failed to connect|login failed/i.test(output))
    return { outcome: 'unreachable', messages };
  return { outcome: 'indeterminate', messages };
}

// Named data-motion signals read from the generated delta — the same signals the skills teach.
export function deltaSignals(deltaText) {
  const signals = [];
  if (/RAISERROR[^;]*data loss might occur/i.test(deltaText)) signals.push('data-loss guard (row-presence RAISERROR above the change)');
  if (/^\s*DROP TABLE/im.test(deltaText)) signals.push('DROP TABLE');
  if (/DROP COLUMN/i.test(deltaText)) signals.push('DROP COLUMN');
  if (/tmp_ms_xx/i.test(deltaText)) signals.push('shadow-table rebuild (tmp_ms_xx)');
  if (/sp_rename/i.test(deltaText)) signals.push('sp_rename (identity-preserving rename)');
  return signals;
}

// ---------------------------------------------------------------------------
// verbs
// ---------------------------------------------------------------------------

function verbDetect(args) {
  const project = resolveProject(args, null) || null;
  const substrate = resolveSubstrate(args, project ? dirname(project) : null);
  const resolvedProject = resolveProject(args, substrate);
  const out = {
    tool: 'prove', version: TOOL_VERSION, verb: 'detect',
    substrate: { kind: substrate.kind, origin: substrate.origin,
                 server: substrate.target.server, database: args.db || substrate.target.database },
    project: resolvedProject,
    buildMode: detectBuildMode(resolvedProject, substrate.build),
    profiles: resolveProfiles(substrate, dirname(resolvedProject)),
  };
  process.stdout.write(JSON.stringify(out, null, 2) + '\n');
  return EXIT.OK;
}

function verbVerdict(args) {
  const timeoutSeconds = args.timeout ? Number(args.timeout) : 300;
  const preProject = args.project ? resolve(args.project) : null;
  const substrate = resolveSubstrate(args, preProject ? dirname(preProject) : join(TREE_ROOT, 'proving-ground'));
  const project = resolveProject(args, substrate);
  const projectDir = dirname(project);
  const profiles = resolveProfiles(substrate, projectDir);
  const conn = connectionString(substrate.target, args.db);
  const dbName = args.db || substrate.target.database;
  const outDir = join(projectDir, 'bin', 'prove');
  mkdirSync(outDir, { recursive: true });

  const spVersion = sqlpackageVersion();
  const buildMode = args.dacpac ? 'prebuilt' : detectBuildMode(project, substrate.build);
  const dacpac = args.dacpac ? resolve(args.dacpac) : build(project, buildMode, timeoutSeconds);

  log(`delta: scripting against ${substrate.target.server} / ${dbName}`);
  const deltaPath = join(outDir, 'delta.sql');
  const scriptRun = runSqlpackage('Script', dacpac, profiles.strict, conn, deltaPath, timeoutSeconds);
  const scriptOutcome = classifyPublish(scriptRun.stdout + scriptRun.stderr);
  if (scriptOutcome.outcome === 'unreachable')
    fail(EXIT.UNREACHABLE, `cannot reach ${substrate.target.server}: ${scriptOutcome.messages.join(' | ') || 'connection failed'}`);
  const delta = existsSync(deltaPath) ? readFileSync(deltaPath, 'utf8') : '';

  const verdict = {
    tool: 'prove', version: TOOL_VERSION, sqlpackage: spVersion,
    substrate: { kind: substrate.kind, server: substrate.target.server, database: dbName },
    build: { mode: buildMode, dacpac },
    delta: { path: existsSync(deltaPath) ? deltaPath : null, dataMotionSignals: deltaSignals(delta) },
    strict: null, permissive: null,
  };

  if (args['script-only']) {
    verdict.strict = { outcome: 'not-run (script-only)', messages: scriptOutcome.messages };
    process.stdout.write(JSON.stringify(verdict, null, 2) + '\n');
    return EXIT.OK;
  }

  log(`strict publish: does the data refuse the change?`);
  const strictRun = runSqlpackage('Publish', dacpac, profiles.strict, conn, null, timeoutSeconds);
  const strict = classifyPublish(strictRun.stdout + strictRun.stderr);
  verdict.strict = { outcome: strict.outcome, messages: strict.messages };
  if (strict.outcome === 'indeterminate') verdict.strict.rawTail = (strictRun.stdout + strictRun.stderr).slice(-1500);

  if (strict.outcome === 'blocked' && args.permissive) {
    if (!profiles.permissive || !existsSync(profiles.permissive))
      fail(EXIT.CONFIG, 'blocked, and --permissive was asked for, but no Permissive profile is configured');
    log(`permissive publish: observing what the block was protecting against (disposable copy only)`);
    const permRun = runSqlpackage('Publish', dacpac, profiles.permissive, conn, null, timeoutSeconds);
    const perm = classifyPublish(permRun.stdout + permRun.stderr);
    verdict.permissive = { outcome: perm.outcome, messages: perm.messages,
      note: 'the permissive outcome shows the consequence; the data probes and content hash (talk-to-local-sql) show what moved' };
  }

  writeFileSync(join(outDir, 'verdict.json'), JSON.stringify(verdict, null, 2) + '\n');
  process.stdout.write(JSON.stringify(verdict, null, 2) + '\n');
  if (strict.outcome === 'published') { log('verdict: PUBLISHED CLEAN — the data does not change how this ships'); return EXIT.OK; }
  if (strict.outcome === 'blocked') { log('verdict: BLOCKED — the block is the finding; read strict.messages'); return EXIT.BLOCKED; }
  if (strict.outcome === 'unreachable') { log('verdict: target unreachable'); return EXIT.UNREACHABLE; }
  log('verdict: INDETERMINATE — the publish output matched neither a block nor a clean publish; read rawTail');
  return EXIT.INDETERMINATE;
}

// ---------------------------------------------------------------------------
// selftest — the detector that keeps the classifier honest (no SQL needed)
// ---------------------------------------------------------------------------

const FIXTURE_BLOCKED = `Publishing to database 'PG_x' on server 'localhost,11433'.
Initializing deployment (Start)
Warning SQL72016: The column [dbo].[Customer].[Email] is being made NOT NULL.
Initializing deployment (Failed)
*** Could not deploy package.
Error SQL72014: Framework Microsoft SqlClient Data Provider: Msg 50000, Level 16, State 127, Line 6 Rows were detected. The schema update is terminating because data loss might occur.
Error SQL72045: Script execution error.`;

const FIXTURE_CLEAN = `Publishing to database 'PG_x' on server 'localhost,11433'.
Updating database (Start)
Updating database (Complete)
Successfully published database.`;

const FIXTURE_DELTA = `IF EXISTS (select top 1 1 from [dbo].[Customer])
    RAISERROR (N'Rows were detected. The schema update is terminating because data loss might occur.', 16, 127) WITH NOWAIT
GO
ALTER TABLE [dbo].[Customer] ALTER COLUMN [Email] NVARCHAR (256) NOT NULL;
EXEC sp_rename '[dbo].[Order].[StatusText]', 'StatusName', 'COLUMN';`;

function verbSelftest() {
  const checks = [
    ['blocked fixture classifies blocked', classifyPublish(FIXTURE_BLOCKED).outcome === 'blocked'],
    ['blocked fixture carries the Msg line', classifyPublish(FIXTURE_BLOCKED).messages.some((m) => /Msg 50000, Level 16, State 127/.test(m))],
    ['clean fixture classifies published', classifyPublish(FIXTURE_CLEAN).outcome === 'published'],
    ['garbage classifies indeterminate', classifyPublish('something else entirely').outcome === 'indeterminate'],
    ['connection failure classifies unreachable', classifyPublish('Unable to connect to target server').outcome === 'unreachable'],
    ['delta signals: data-loss guard', deltaSignals(FIXTURE_DELTA).some((s) => s.startsWith('data-loss guard'))],
    ['delta signals: sp_rename', deltaSignals(FIXTURE_DELTA).some((s) => s.startsWith('sp_rename'))],
    ['delta signals: no false DROP TABLE', !deltaSignals(FIXTURE_DELTA).includes('DROP TABLE')],
  ];
  let failed = 0;
  for (const [name, ok] of checks) { if (!ok) { failed++; process.stderr.write(`selftest FAIL: ${name}\n`); } }
  process.stderr.write(failed === 0 ? `prove selftest: ${checks.length} checks clean\n` : `prove selftest: ${failed} of ${checks.length} failed\n`);
  return failed === 0 ? EXIT.OK : 1;
}

// ---------------------------------------------------------------------------

// Dispatch only when executed directly — bake.mjs imports this module for its helpers.
if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  const args = parseArgs(process.argv.slice(2));
  const verb = args._[0];
  if (args.help || !verb) { process.stderr.write(USAGE + '\n'); process.exit(verb ? EXIT.OK : EXIT.ARGV); }
  if (verb === 'detect') process.exit(verbDetect(args));
  else if (verb === 'verdict') process.exit(verbVerdict(args));
  else if (verb === 'selftest') process.exit(verbSelftest());
  else fail(EXIT.ARGV, `unknown verb '${verb}' — one of: detect, verdict, selftest`);
}
