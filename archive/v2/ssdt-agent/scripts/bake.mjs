#!/usr/bin/env node
// bake.mjs — the substrate as a versioned artifact you pull, not a machine you build.
//
// `make` produces the artifact: it ensures a source database holds the current baseline
// (the Twin's deterministic mint when the Twin is present; otherwise a Strict publish of the
// unedited project plus its idempotent seed), then exports it as a `.bacpac` named by the
// content fingerprint of the estate sources, beside a manifest carrying the fingerprint and
// the artifact's sha256. `restore` consumes the artifact on any machine: it imports the
// `.bacpac` into a fingerprint-versioned database — LocalDB, a local SQL Server, or a
// container; Docker is one substrate, never a requirement, and the consuming machine needs
// no engine, no F#, and no monorepo: only sqlpackage.
//
// Why fingerprint-versioned names: a pull is always an import into a NEW database
// (`<base>_<fp12>`), so no restore ever needs a drop, and two schema versions can stand
// side by side while a developer finishes a change. Old copies are dropped at leisure.
//
// Substrate resolution, build detection, and process discipline are shared with prove.mjs
// (one waist — see `../PORTABILITY.md`). Exit codes: 0 done · 1 argv · 4 target/tool
// unreachable · 6 config · 7 build failed · 9 refused/indeterminate.

import { createHash } from 'node:crypto';
import { existsSync, readFileSync, writeFileSync, mkdirSync, readdirSync, statSync } from 'node:fs';
import { dirname, join, resolve, basename, relative } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import {
  EXIT, fail, parseArgs, sh, resolveSubstrate, resolveProject, resolveProfiles,
  connectionString, detectBuildMode, build, runSqlpackage, sqlpackageVersion, classifyPublish,
} from './prove.mjs';

const TOOL_VERSION = '0.1.0';
const log = (line) => process.stderr.write(`bake: ${line}\n`);

const USAGE = `bake — produce and consume the proving substrate as a versioned .bacpac artifact.

usage:
  node bake.mjs make    [--project <path.sqlproj>] [--config <prove.config.json>]
                        [--scenario <name>] [--out <dir>] [--timeout <seconds>]
  node bake.mjs restore [--manifest <bake.manifest.json> | --file <x.bacpac>]
                        [--config <prove.config.json>] [--db <name>] [--timeout <seconds>]
  node bake.mjs selftest

make:    ensure the source database holds the current baseline (Twin substrate -> 'twin up',
         the deterministic mint; otherwise build + Strict-publish the UNEDITED project so the
         schema and the idempotent seed converge on a fingerprint-named bake database), then
         sqlpackage /Action:Export -> <out>/<base>-<fp12>.bacpac + bake.manifest.json.
restore: verify the artifact's sha256 against the manifest, then sqlpackage /Action:Import
         into <base>_<fp12> (a NEW database — no drop is ever needed) on the substrate the
         config names. The target must be a DISPOSABLE local engine, never a real environment.
stdout: one JSON result object. stderr: progress. Consumers need only sqlpackage.`;

// ---------------------------------------------------------------------------
// fingerprint — content-addressed identity of the estate sources
// ---------------------------------------------------------------------------

function sourceFiles(projectDir, projectPath) {
  const files = [];
  const skip = new Set(['bin', 'obj', '.git', 'node_modules']);
  const walk = (dir) => {
    for (const name of readdirSync(dir)) {
      if (skip.has(name)) continue;
      const p = join(dir, name);
      if (statSync(p).isDirectory()) walk(p);
      else if (/\.(sql|sqlproj|refactorlog)$/i.test(name) || name === 'twin.json') files.push(p);
    }
  };
  walk(projectDir);
  if (!files.includes(projectPath)) files.push(projectPath);
  return files.sort((a, b) => relative(projectDir, a).localeCompare(relative(projectDir, b)));
}

export function fingerprintOf(entries /* [relPath, content][] sorted by relPath */) {
  const h = createHash('sha256');
  for (const [rel, content] of entries) {
    h.update(`${rel.length}:${rel}`);
    h.update(`${Buffer.byteLength(content)}:`);
    h.update(content);
  }
  return h.digest('hex').slice(0, 12);
}

function fingerprintProject(projectDir, projectPath) {
  const entries = sourceFiles(projectDir, projectPath)
    .map((p) => [relative(projectDir, p).replaceAll('\\', '/'), readFileSync(p, 'utf8')]);
  return fingerprintOf(entries);
}

export function versionedDbName(base, fp) { return `${base}_${fp}`; }

const sha256File = (p) => createHash('sha256').update(readFileSync(p)).digest('hex');

// ---------------------------------------------------------------------------
// make
// ---------------------------------------------------------------------------

function verbMake(args) {
  const timeoutSeconds = args.timeout ? Number(args.timeout) : 600;
  const preProject = args.project ? resolve(args.project) : null;
  const substrate = resolveSubstrate(args, preProject ? dirname(preProject) : process.cwd());
  const project = resolveProject(args, substrate);
  const projectDir = dirname(project);
  const fp = fingerprintProject(projectDir, project);
  const base = basename(project).replace(/\.sqlproj$/i, '');
  const outDir = args.out ? resolve(args.out) : join(projectDir, 'bin', 'bake');
  mkdirSync(outDir, { recursive: true });
  const spVersion = sqlpackageVersion();

  let sourceDb;
  if (substrate.kind === 'twin') {
    // The Twin IS the baseline mechanism: deterministic schema publish + mint.
    sourceDb = substrate.target.database;
    log(`twin up${args.scenario ? ` --scenario ${args.scenario}` : ''} (deterministic baseline)`);
    const twinArgs = ['up', ...(args.scenario ? ['--scenario', args.scenario] : [])];
    const r = sh('twin', twinArgs, { timeoutSeconds, cwd: projectDir, enoentHint: 'the twin CLI is not on PATH' });
    if (r.status !== 0) { process.stderr.write((r.stdout + r.stderr).slice(-2000)); fail(EXIT.INDETERMINATE, `twin up failed (exit ${r.status})`); }
  } else {
    // No Twin: converge a fingerprint-named bake database from the UNEDITED project —
    // Strict publish creates or converges the schema; the post-deploy seed is idempotent.
    sourceDb = versionedDbName(`${base}_bake`, fp);
    const profiles = resolveProfiles(substrate, projectDir);
    const mode = detectBuildMode(project, substrate.build);
    const dacpac = build(project, mode, timeoutSeconds);
    log(`baseline: Strict publish of the unedited project -> ${substrate.target.server} / ${sourceDb}`);
    const pub = runSqlpackage('Publish', dacpac, profiles.strict, connectionString(substrate.target, sourceDb), null, timeoutSeconds);
    const outcome = classifyPublish(pub.stdout + pub.stderr);
    if (outcome.outcome !== 'published') {
      process.stderr.write((pub.stdout + pub.stderr).slice(-2000));
      fail(outcome.outcome === 'unreachable' ? EXIT.UNREACHABLE : EXIT.INDETERMINATE,
        `baseline publish did not complete (${outcome.outcome}) — bake only from an unedited baseline`);
    }
  }

  const bacpac = join(outDir, `${base}-${fp}.bacpac`);
  log(`export: ${sourceDb} -> ${basename(bacpac)}`);
  const exp = sh('sqlpackage',
    ['/Action:Export', `/SourceConnectionString:${connectionString(substrate.target, sourceDb)}`, `/TargetFile:${bacpac}`, '/OverwriteFiles:True'],
    { timeoutSeconds, enoentHint: 'dotnet tool install --global microsoft.sqlpackage' });
  if (exp.status !== 0 || !existsSync(bacpac)) {
    process.stderr.write((exp.stdout + exp.stderr).slice(-2000));
    fail(EXIT.INDETERMINATE, `export failed (exit ${exp.status})`);
  }

  const manifest = {
    tool: 'bake', version: TOOL_VERSION, created: new Date().toISOString(),
    fingerprint: fp, base,
    source: { kind: substrate.kind, server: substrate.target.server, database: sourceDb,
              scenario: args.scenario || null },
    sqlpackage: spVersion,
    bacpac: { file: basename(bacpac), sha256: sha256File(bacpac), bytes: statSync(bacpac).size },
    restoreAs: versionedDbName(base, fp),
  };
  writeFileSync(join(outDir, 'bake.manifest.json'), JSON.stringify(manifest, null, 2) + '\n');
  process.stdout.write(JSON.stringify(manifest, null, 2) + '\n');
  log(`done: ${basename(bacpac)} (${manifest.bacpac.bytes} bytes, sha256 ${manifest.bacpac.sha256.slice(0, 12)}…)`);
  if (substrate.kind !== 'twin')
    log(`the bake database ${sourceDb} remains on ${substrate.target.server}; drop it at leisure`);
  return EXIT.OK;
}

// ---------------------------------------------------------------------------
// restore — the consuming machine's whole toolchain is sqlpackage
// ---------------------------------------------------------------------------

function findManifest(args) {
  if (args.manifest) return resolve(args.manifest);
  for (const dir of [process.cwd(), join(process.cwd(), 'bin', 'bake')]) {
    const p = join(dir, 'bake.manifest.json');
    if (existsSync(p)) return p;
  }
  return null;
}

function verbRestore(args) {
  const timeoutSeconds = args.timeout ? Number(args.timeout) : 600;
  let bacpac; let manifest = null;
  if (args.file) {
    bacpac = resolve(args.file);
    if (!existsSync(bacpac)) fail(EXIT.CONFIG, `--file ${args.file}: no such file`);
  } else {
    const mPath = findManifest(args);
    if (!mPath) fail(EXIT.CONFIG, 'no bake.manifest.json found — pass --manifest <path> or --file <x.bacpac>');
    manifest = JSON.parse(readFileSync(mPath, 'utf8'));
    bacpac = join(dirname(mPath), manifest.bacpac.file);
    if (!existsSync(bacpac)) fail(EXIT.CONFIG, `${manifest.bacpac.file}: named by the manifest but not beside it`);
    const actual = sha256File(bacpac);
    if (actual !== manifest.bacpac.sha256)
      fail(EXIT.INDETERMINATE, `sha256 mismatch: artifact ${actual.slice(0, 12)}… vs manifest ${manifest.bacpac.sha256.slice(0, 12)}… — re-pull the artifact`);
    log(`sha256 verified (${actual.slice(0, 12)}…)`);
  }

  const substrate = resolveSubstrate(args, process.cwd());
  const db = args.db || (manifest ? manifest.restoreAs
    : versionedDbName(basename(bacpac).replace(/\.bacpac$/i, '').replace(/-/g, '_'), 'adhoc'));
  log(`import: ${basename(bacpac)} -> ${substrate.target.server} / ${db}`);
  const imp = sh('sqlpackage',
    ['/Action:Import', `/SourceFile:${bacpac}`, `/TargetConnectionString:${connectionString(substrate.target, db)}`],
    { timeoutSeconds, enoentHint: 'dotnet tool install --global microsoft.sqlpackage' });
  const text = imp.stdout + imp.stderr;
  if (imp.status !== 0) {
    if (/already exists/i.test(text)) {
      fail(EXIT.INDETERMINATE,
        `database ${db} already exists — this artifact version is already restored there. ` +
        `Use it as-is, or import under another name with --db, or drop the old copy first ` +
        `(DROP DATABASE [${db}] on ${substrate.target.server}).`);
    }
    process.stderr.write(text.slice(-2000));
    fail(/Unable to connect|Failed to connect|login failed/i.test(text) ? EXIT.UNREACHABLE : EXIT.INDETERMINATE,
      `import failed (exit ${imp.status})`);
  }
  const result = {
    tool: 'bake', version: TOOL_VERSION, verb: 'restore',
    restored: { server: substrate.target.server, database: db, from: basename(bacpac),
                fingerprint: manifest ? manifest.fingerprint : null },
    note: 'this database is a disposable proving copy; point prove.mjs at it with --db',
  };
  process.stdout.write(JSON.stringify(result, null, 2) + '\n');
  log(`done: ${db} is ready — prove against it with: node prove.mjs verdict --db ${db}`);
  return EXIT.OK;
}

// ---------------------------------------------------------------------------
// selftest — fingerprint and naming laws, no SQL needed
// ---------------------------------------------------------------------------

function verbSelftest() {
  const a = [['Modules/Customer.sql', 'CREATE TABLE ...'], ['SampleCatalog.sqlproj', '<Project/>']];
  const b = [['Modules/Customer.sql', 'CREATE TABLE ... NOT NULL'], ['SampleCatalog.sqlproj', '<Project/>']];
  const checks = [
    ['fingerprint is deterministic', fingerprintOf(a) === fingerprintOf(a)],
    ['fingerprint is 12 hex chars', /^[0-9a-f]{12}$/.test(fingerprintOf(a))],
    ['content change moves the fingerprint', fingerprintOf(a) !== fingerprintOf(b)],
    ['path identity matters', fingerprintOf(a) !== fingerprintOf([[a[0][0] + 'x', a[0][1]], a[1]])],
    ['length prefixing prevents boundary collisions', fingerprintOf([['p', 'ab'], ['q', 'c']]) !== fingerprintOf([['p', 'a'], ['q', 'bc']])],
    ['versioned name composes base and fingerprint', versionedDbName('SampleCatalog', 'abc123def456') === 'SampleCatalog_abc123def456'],
  ];
  let failed = 0;
  for (const [name, ok] of checks) { if (!ok) { failed++; process.stderr.write(`selftest FAIL: ${name}\n`); } }
  process.stderr.write(failed === 0 ? `bake selftest: ${checks.length} checks clean\n` : `bake selftest: ${failed} of ${checks.length} failed\n`);
  return failed === 0 ? EXIT.OK : 1;
}

// ---------------------------------------------------------------------------

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  const args = parseArgs(process.argv.slice(2));
  const verb = args._[0];
  if (args.help || !verb) { process.stderr.write(USAGE + '\n'); process.exit(verb ? EXIT.OK : EXIT.ARGV); }
  if (verb === 'make') process.exit(verbMake(args));
  else if (verb === 'restore') process.exit(verbRestore(args));
  else if (verb === 'selftest') process.exit(verbSelftest());
  else fail(EXIT.ARGV, `unknown verb '${verb}' — one of: make, restore, selftest`);
}
