export const meta = {
  name: 'v3-wave',
  description: 'One wave of v3 work packages: implement test-first in worktrees, verify by lens, fix, integrate, push',
  phases: [
    { title: 'Implement', detail: 'one agent per work package, test first, in its own worktree, on a local wp/ branch' },
    { title: 'Verify', detail: 'fresh skeptical agents per lens; fix and re-verify at most twice' },
    { title: 'Integrate', detail: 'merge passing branches in order in the main checkout, run the suites, push' },
  ],
}

// args: { wave, branch, push, wps: [ { id, title, brief, lenses: [...], effort, sql: bool } ], integrateNotes }

const ROOT = '/home/user/outsystems-ddl-exporter'
const SCRATCH = '/tmp/claude-0/-home-user-outsystems-ddl-exporter/17755096-5809-5c10-a22d-d46e8371455a/scratchpad'
const SETPRIV = 'setpriv --bounding-set=-dac_override,-dac_read_search --inh-caps=-dac_override,-dac_read_search --'

const ENV = [
  'Environment facts (this container): 4 CPUs; .NET SDK 10.0.402 at ~/.dotnet (global.json pins the 10.0.4xx band). ' + (args.envNote || ''),
  `The session runs as root, so file-permission tests lie unless run without root\'s override. ALWAYS run tests as: ${SETPRIV} dotnet test ... . With that prefix the fast lane is fully green at the wave\'s base (${args.baseCounts || "the counts the integrator last reported"}); any red test is yours to explain.`,
  'Commands: dotnet build DbChange.sln (warnings are errors); the fast lane: ' + SETPRIV + ' dotnet test DbChange.sln --no-build --filter "Category=fast|Category=build"; the fixture lane (needs the SQL Server above): ' + SETPRIV + ' dotnet test DbChange.sln --no-build --filter Category=fixture. After switching branches or moving files, a stale build: dotnet clean.',
  'The archive (archive/) is readable by path (rg --no-ignore <pattern> archive/) and never edited. No commit, code or document names the model that wrote it. Commit messages end with the two attribution lines: "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>" and "Claude-Session: https://claude.ai/code/session_01Eu2Ney9xrEgmL5SpE1Q3io".',
  'Ledger rule: a change that adds a test holding a row of V3_PARITY.md changes that row\'s last cell to name the test, law, value or exit, in the same commit; tests/Budgets.Tests/ParityResolves.cs checks the grammar (V3_PARITY.md §1). Never rename an existing test method or test class: V3_PARITY.md, VALUES.md and LAWS.md cite them.',
  'Write the register AGENTS.md describes in every commit message, test name, error message and comment.',
].join('\n')

const WP_RESULT = {
  type: 'object',
  properties: {
    id: { type: 'string' },
    status: { type: 'string', enum: ['done', 'blocked', 'needs-decision'] },
    branch: { type: 'string' },
    commit: { type: 'string' },
    tests: { type: 'array', items: { type: 'string' } },
    suites: { type: 'string', description: 'the exact pass/fail counts of each suite you ran' },
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
    parityTally: { type: 'string' },
    notes: { type: 'string' },
  },
  required: ['merged', 'backedOut', 'suites', 'notes'],
}

const LENS = {
  spec: 'Does the branch do exactly what the work package\'s brief (and, where it names one, its row in V3_MILESTONES.md) says, and do its tests assert the done-when itself rather than something weaker? Is every test written so it would fail if the behaviour were missing?',
  safety: 'Can any path write to a named environment, publish Permissive outside a registered Copy, return or log a row value from an environment classified real, or print a credential? Hold the code to WP 1.4 and WP 1.5 of V3_MILESTONES.md word for word.',
  budgets: 'Do Budgets, NoSkips, the dependency laws and the banned-symbol build pass; does every file stay within its per-file ceiling and every line within the width ceiling ci/budgets.json sets; are DECISIONS.md lines one per decision in the dated format?',
  refactor: 'Is this behaviour-preserving? Prove it, do not assume it: for every file that was only reflowed, strip all whitespace from the base version and the branch version (git show <base>:<path> and the branch file, e.g. tr -d " \\t\\r\\n") and confirm they are identical; for every file that was split, confirm every member declaration of the base file appears exactly once across the new files, with the same body (strip whitespace and compare), and that no public or internal name, namespace, attribute, accessibility or test trait changed. Then run the build and the suites yourself. Any token change that is not a pure line break, or the partial modifier and the using lines a split needs, is a must-fix unless the brief asks for it. Also read the reflowed code: does each wrapped line break where the surrounding code style breaks (before an operator, after a comma, at a lambda), never mid-string, and stay readable?',
}
const lensesOf = wp => (wp.lenses && wp.lenses.length ? wp.lenses : ['spec'])
const effortOf = wp => (wp.effort ? { effort: wp.effort } : {})
const runsOf = wp => (wp.sql ? 'Run the build, the fast lane and the fixture lane.' : 'Run the build and the fast lane; run the fixture lane too if you changed anything under io/ or tests/Io.Tests.')

const implement = wp => agent(
  [
    `Work package ${wp.id}: ${wp.title}. Wave ${args.wave} of the v3 build in ${ROOT}.`,
    ENV,
    `You are in your own git worktree. Create local branch wp/${wp.id} from ${args.branch} (git checkout -b wp/${wp.id} ${args.branch}). Never push. Never touch the main checkout at ${ROOT} itself.`,
    'The brief:',
    wp.brief,
    'Tests first where the brief adds behaviour: write the test that encodes the done-when, see it fail, then write the code until it passes.',
    runsOf(wp),
    'Commit on your branch (several commits are fine). If something needs a decision only the operator can make (V3_BUILD_PROMPT.md §3 rule 8), stop and return needs-decision with one question. Return the exact suite counts you saw.',
  ].join('\n\n'),
  { label: `implement ${wp.id}`, phase: 'Implement', schema: WP_RESULT, isolation: 'worktree', ...effortOf(wp) })

const verify = (wp, result, lens) => agent(
  [
    `Verify work package ${wp.id} (${wp.title}) at local branch ${result.branch}, whose base is ${args.branch}. In your own worktree run: git checkout --detach ${result.branch}. Compare with the base by git diff ${args.branch}...${result.branch}.`,
    ENV,
    'The brief the implementer worked from:',
    wp.brief,
    'Your lens: ' + (wp.lensText && wp.lensText[lens] ? wp.lensText[lens] : LENS[lens]),
    'The implementer reported: ' + JSON.stringify({ tests: result.tests, suites: result.suites, notes: result.notes }),
    runsOf(wp) + ' Run them yourself; do not rely on the report. If you cannot confirm a claim, pass is false. A finding names the file and line, the evidence, and the fix.',
  ].join('\n\n'),
  { label: `verify ${wp.id} (${lens})`, phase: 'Verify', schema: VERDICT, isolation: 'worktree' })

const fix = (wp, result, round, findings) => agent(
  [
    `Work package ${wp.id} (${wp.title}). In your own worktree create local branch wp/${wp.id}-r${round + (wp.roundBase || 0)} from ${result.branch} (git checkout -b wp/${wp.id}-r${round + (wp.roundBase || 0)} ${result.branch}), fix these verification findings, re-run the suites, and commit. Never push. A finding you judge wrong: leave the code and say why in notes.`,
    ENV,
    'The brief:',
    wp.brief,
    'The findings:',
    JSON.stringify(findings, null, 1),
    runsOf(wp),
  ].join('\n\n'),
  { label: `fix ${wp.id} (round ${round})`, phase: 'Verify', schema: WP_RESULT, isolation: 'worktree', ...effortOf(wp) })

phase('Implement')
const outcomes = await pipeline(
  args.wps,
  wp => wp.startBranch
    ? Promise.resolve({ id: wp.id, status: 'done', branch: wp.startBranch, notes: wp.startNotes || 'carried from an interrupted wave', tests: [], suites: '' })
    : implement(wp),
  async (first, wp) => {
    if (!first || first.status !== 'done') return { wp, result: first }
    if (wp.verified) return { wp, result: first, verdicts: [], nits: [] }
    let result = first
    for (let round = 1; round <= 3; round++) {
      const lenses = lensesOf(wp)
      const verdicts = (await parallel(lenses.map(lens => () => verify(wp, result, lens)))).filter(Boolean)
      const open = verdicts.flatMap(v => v.findings || []).filter(f => f.severity !== 'nit')
      const clean = verdicts.length === lenses.length && verdicts.every(v => v.pass) && !open.some(f => f.severity === 'must-fix')
      if (clean) return { wp, result, verdicts, nits: verdicts.flatMap(v => (v.findings || []).filter(f => f.severity === 'nit')) }
      if (round === 3) return { wp, result: { ...result, status: 'blocked', notes: 'verification did not converge in two fix rounds: ' + JSON.stringify(open).slice(0, 1500) }, verdicts }
      result = (await fix(wp, result, round, open)) || result
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
    `Integrate wave ${args.wave} in the main checkout at ${ROOT}, on branch ${args.branch} (git checkout ${args.branch}; confirm git status is clean first; if it is not, stop and report).`,
    ENV,
    `Merge these local branches in this order with git merge --no-ff (a merge commit message in the register, naming the work package): ${ready.map(o => o.result.branch).join(', ')}.`,
    'Resolve conflicts in shared files (DbChange.sln, Directory.Packages.props, ci/budgets.json, ci/docs.manifest.json, DECISIONS.md, V3_PARITY.md, .github/workflows/dbchange.yml) by keeping both sides\' intent.',
    'After each merge: dotnet build DbChange.sln and the fast lane. After the last: the fixture lane, then bash ci/laws.sh and commit LAWS.md if it changed, and the Citations, Laws, Budgets and ParityResolves tests green. If a merge breaks the build or a suite and the fix is not a one-line conflict repair, back that merge out (git reset --hard to before it, then continue with the rest) and report it.',
    args.integrateNotes || '',
    args.push
      ? `Then push: git push -u origin ${args.branch}; on a network failure retry up to four times, waiting 2, 4, 8 and 16 seconds. Report the pushed commit.`
      : 'Do not push; report the head commit.',
    `Then delete the merged local wp/ branches (git branch -D) and run git worktree prune. Finally print the parity tally: dotnet test tests/Budgets.Tests/Budgets.Tests.csproj --no-build --filter "FullyQualifiedName~ParityResolves.Every_row" --logger "console;verbosity=detailed" and copy the line that starts "V3_PARITY.md:" into parityTally.`,
  ].join('\n\n'),
  { label: 'integrate', phase: 'Integrate', schema: INTEGRATION })

return {
  wave: args.wave,
  landed: integration ? integration.merged : [],
  held: held.map(o => ({ id: o.wp.id, status: o.result ? o.result.status : 'none', notes: o.result ? o.result.notes : '', question: o.result && o.result.question ? o.result.question : '' })),
  nits: ready.map(o => ({ id: o.wp.id, nits: o.nits || [] })),
  results: outcomes.filter(Boolean).map(o => ({ id: o.wp.id, commit: o.result && o.result.commit, tests: o.result && o.result.tests, suites: o.result && o.result.suites })),
  integration,
}
