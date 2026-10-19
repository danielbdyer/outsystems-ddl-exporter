#!/usr/bin/env node
// inflight-check.mjs — the lag-window hold, made a check instead of a memory.
//
// While a multi-phase change is in flight, its table's model deliberately lags the database,
// and ANY other publish touching that table regenerates the old shape on a green publish (the
// revert was captured live — sample-prs/compound/extract-to-lookup-program.md). The in-flight
// ledger (estate/in-flight.md) records the hold; this check enforces it: given the files a
// pull request changes, it refuses the PR when a changed table collides with an open in-flight
// row's `tables` column.
//
// Usage:
//   node inflight-check.mjs diff <base-ref>      # changed files from `git diff --name-only <base-ref>...HEAD`
//   node inflight-check.mjs files <f1> <f2> ...  # changed files given explicitly (CI-friendly)
//   node inflight-check.mjs selftest
//
// Table extraction: every changed .sql file is scanned for `CREATE TABLE <name>` — the tables a
// declarative file DEFINES. The ledger's `tables` column carries space-separated schema.Table
// names. Matching is case-insensitive and bracket-insensitive; a bare table name in the ledger
// matches any schema.
//
// The one exemption: a PR that also edits estate/in-flight.md is treated as the phase-advance
// (or hold-re-dating) PR — the collision is reported as a warning, not a refusal, because the
// reviewer sees the ledger change and the schema change side by side.
//
// Exit codes: 0 no collision (or exempted) · 1 argv · 5 collision · 6 ledger unreadable.

import { spawnSync } from 'node:child_process';
import { existsSync, readFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const TREE_ROOT = dirname(dirname(fileURLToPath(import.meta.url)));
const log = (line) => process.stderr.write(`inflight-check: ${line}\n`);

const norm = (name) =>
  name.replace(/[\[\]]/g, '').trim().toLowerCase();

const bareName = (qualified) => {
  const parts = norm(qualified).split('.');
  return parts[parts.length - 1];
};

// The tables a .sql file DEFINES (declarative CREATEs — the reshape surface).
export function tablesInSql(text) {
  const found = new Set();
  for (const m of text.matchAll(/CREATE\s+TABLE\s+((?:\[?[\w ]+\]?\.)?\[?[\w ]+\]?)/gi)) {
    found.add(norm(m[1]));
  }
  return found;
}

// Open in-flight rows -> [{id, tables: Set<normalized>}]. Header: id | change | tables | ...
export function openRows(ledgerText) {
  const rows = [];
  const lines = ledgerText.split('\n').filter((l) => /^\s*\|/.test(l));
  const body = lines.slice(2); // header + separator
  for (const line of body) {
    const cells = line.split('|').slice(1, -1).map((c) => c.trim());
    if (cells.length < 3 || !cells[0]) continue;
    const tables = new Set(cells[2].split(/\s+/).filter(Boolean).map(norm));
    rows.push({ id: cells[0], change: cells[1], tables });
  }
  return rows;
}

export function collisions(changedTables, rows) {
  const hits = [];
  for (const row of rows) {
    for (const t of changedTables) {
      const match = [...row.tables].some(
        (held) => held === t || bareName(held) === bareName(t)
      );
      if (match) { hits.push({ id: row.id, change: row.change, table: t }); break; }
    }
  }
  return hits;
}

function run(files) {
  const ledgerPath = join(TREE_ROOT, 'estate', 'in-flight.md');
  if (!existsSync(ledgerPath)) { log(`ledger not found at ${ledgerPath}`); process.exit(6); }
  // The example row is fenced; strip fences so it never parses as live state.
  const ledger = readFileSync(ledgerPath, 'utf8').replace(/```[\s\S]*?```/g, '');
  const rows = openRows(ledger);
  if (rows.length === 0) { log('no in-flight rows — nothing to hold'); process.exit(0); }

  const changedTables = new Set();
  let ledgerEdited = false;
  for (const f of files) {
    if (/estate[\\/]in-flight\.md$/.test(f)) ledgerEdited = true;
    if (!f.endsWith('.sql')) continue;
    const p = resolve(f);
    if (!existsSync(p)) continue; // deleted file: its tables are leaving, handled by its own program
    for (const t of tablesInSql(readFileSync(p, 'utf8'))) changedTables.add(t);
  }

  const hits = collisions(changedTables, rows);
  if (hits.length === 0) { log(`no collision (${rows.length} in-flight row(s), ${changedTables.size} changed table(s))`); process.exit(0); }
  for (const h of hits)
    log(`${ledgerEdited ? 'WARNING' : 'REFUSED'}: ${h.table} is held by in-flight ${h.id} (${h.change})`);
  if (ledgerEdited) {
    log('the PR also edits estate/in-flight.md — treated as the phase-advance PR; the reviewer sees both');
    process.exit(0);
  }
  log('a publish touching a held table during its lag window reverts the in-flight change on a green publish;');
  log('wait for the in-flight row to close, or coordinate with its owner and advance the row in this PR');
  process.exit(5);
}

function verbDiff(baseRef) {
  const r = spawnSync('git', ['diff', '--name-only', `${baseRef}...HEAD`], { encoding: 'utf8' });
  if (r.status !== 0) { log(`git diff failed: ${r.stderr.trim()}`); process.exit(1); }
  run(r.stdout.split('\n').filter(Boolean));
}

function verbSelftest() {
  const ledger = `| id | change | tables | phase | of | next action | window closes | PR |
|---|---|---|---|---|---|---|---|
| CHG-1 | tighten Email | dbo.Customer | 1 | 2 | R2 model catch-up | 2099-01-01 | #1 |
| CHG-2 | split Order | dbo.[Order] dbo.OrderLine | 2 | 3 | cutover | 2099-01-01 | #2 |
`;
  const rows = openRows(ledger);
  const sql = 'CREATE TABLE dbo.[Order]\n( Id INT NOT NULL );\nGO\nCREATE TABLE dbo.Product ( Id INT );';
  const changed = tablesInSql(sql);
  const checks = [
    ['two rows parse', rows.length === 2],
    ['row tables parse with brackets', rows[1].tables.has('dbo.order')],
    ['sql tables extract', changed.has('dbo.order') && changed.has('dbo.product')],
    ['collision found on held table', collisions(changed, rows).some((h) => h.id === 'CHG-2')],
    ['no collision on free table', collisions(new Set(['dbo.category']), rows).length === 0],
    ['bare name matches any schema', collisions(new Set(['customer']), rows).some((h) => h.id === 'CHG-1')],
  ];
  let failed = 0;
  for (const [name, ok] of checks) { if (!ok) { failed++; process.stderr.write(`selftest FAIL: ${name}\n`); } }
  process.stderr.write(failed === 0 ? `inflight-check selftest: ${checks.length} checks clean\n` : `inflight-check selftest: ${failed} of ${checks.length} failed\n`);
  process.exit(failed === 0 ? 0 : 1);
}

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  const [verb, ...rest] = process.argv.slice(2);
  if (verb === 'diff' && rest[0]) verbDiff(rest[0]);
  else if (verb === 'files' && rest.length) run(rest);
  else if (verb === 'selftest') verbSelftest();
  else { log('usage: inflight-check.mjs diff <base-ref> | files <f1> ... | selftest'); process.exit(1); }
}
