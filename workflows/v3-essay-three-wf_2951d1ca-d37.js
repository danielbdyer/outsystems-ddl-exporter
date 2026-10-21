export const meta = {
  name: 'v3-essay-three',
  description: 'Essay three of the v3 series: what waves 5 to 7 built, why it is shaped so, checked against the code',
  phases: [
    { title: 'Read', detail: 'three readers map the work of waves 5 to 7 with evidence' },
    { title: 'Draft', detail: 'one writer drafts the essay in the series voice' },
    { title: 'Check', detail: 'a fact checker and a page checker' },
    { title: 'Revise', detail: 'the writer applies every confirmed correction' },
  ],
}

const REPO = '/home/user/outsystems-ddl-exporter'
const ESSAYS = '/tmp/claude-0/-home-user-outsystems-ddl-exporter/17755096-5809-5c10-a22d-d46e8371455a/scratchpad/essays'
const OUT = ESSAYS + '/03-waves-5-7.html'
const SPAN = 'the commits from 77544d6 (where essay two was read, 20 October 2026) to 28d6bd7 (head of claude/v3-architecture-design-sl95mx, 21 October 2026)'

const FACTS = {
  type: 'object',
  properties: {
    cluster: { type: 'string' },
    topics: {
      type: 'array',
      items: {
        type: 'object',
        properties: {
          title: { type: 'string' },
          what: { type: 'string', description: 'what was built, concretely: types, functions, verbs, numbers' },
          why: { type: 'string', description: 'why it has this shape: the measured fact, the failure it prevents, the decision line, the alternative declined' },
          subtle: { type: 'string', description: 'the one detail a careful reader would admire or would not have guessed, with its evidence' },
          where: { type: 'array', items: { type: 'string' }, description: 'file:line ranges, test names, commit hashes, DECISIONS.md dates' },
          closes: { type: 'string', description: 'the failure or v2 problem it closes, and the VALUES.md row or law if any' },
          v2: { type: 'string', description: 'how v2 did the same thing, measured or cited from archive/' },
          cost: { type: 'string', description: 'what it costs: lines, time, a limit it accepts, what it does not check' },
        },
        required: ['title', 'what', 'why', 'where', 'cost'],
      },
    },
    numbers: { type: 'array', items: { type: 'string' }, description: 'measured figures with how each was measured' },
  },
  required: ['cluster', 'topics', 'numbers'],
}

const CLUSTERS = [
  { key: 'predict', ask: 'WP 2.2 (io/BlockOnPossibleDataLoss: the checks read from DacFx own script, state 127, the predicate regenerated with ScriptDom, SQLCMD values in memory, sensitive values refused), WP 2.1 second slice (kernel/Vocabulary and Preconditions.Kinds: the kinds only a plan statements show, matched by rows that are data), and WP 2.6 (the corpus under tests/Golden/changes/ on an empty, a populated and a violating copy; the Facsimile case catalog moved to tests/Cases/ with its lint as a C# test)' },
  { key: 'copy', ask: 'WP 3.8 (kernel Witness: the existing-data tier plan, one valid row where the environment has rows, one violating row per precondition answered violating, ordinal literals, the largest need per table, NULL semantics in unique keys, needs named where literals cannot satisfy a check, D1 and D3) and WP 3.5 (io/SyntheticCopy and synthetic-copy up: the eight steps, the declared witnesses grammar and what it refuses, the repository objects and linked servers that reach another server, R15, the trust step, hash, acceptance, identity)' },
  { key: 'ground', ask: 'spikes S9, S10 and S11 (trust kept as an annotation, validation after the post-deploy script, system names compared by role, AUTO_CLOSE on Express), WP 3.4 (io/LocalServer: the registry-only sweep, the DROP race and its application lock with the SQL Server errors measured, the first connect retried only when the connection never opened, the copy cache and where a backup can be written) with spike S4 (restore against publish, its CI timings and decision), and WP 7.1 (tests/Budgets.Tests/Citations.cs: the v2 citation forms refused, findings and verbs resolved, value O3)' },
]

phase('Read')
const read = await parallel(CLUSTERS.map(c => () => agent(
  `You are reading the dbchange v3 repository at ${REPO} (C#, .NET 10; kernel/, io/, cli/, tests/) to gather material for an essay, not to change anything: edit no file and run no build. The essay covers ${SPAN}; use git log, git show and git diff over that span. Your cluster: ${c.ask}. Read the code, its tests, the DECISIONS.md lines of 2026-10-20 and 2026-10-21 that decided it, the V3_MILESTONES.md rows it implements, and where it ports v2, the v2 files under archive/ (search them with rg --no-ignore <pattern> archive/). For each topic give what was built, why it has this shape (the measured fact, the failure it prevents, the decision, the alternative declined), the one subtle detail a careful engineer would admire, file:line evidence, what it closes, how v2 did it, and what it costs. Measure every number you report (wc -l, git diff --shortstat, test counts from the test names or a grep of [Fact]/[Theory]); never estimate one. Quote short code only where it carries the point. Never print a password or the contents of ~/.dbchange/sql.env.`,
  { label: `read:${c.key}`, phase: 'Read', schema: FACTS, effort: 'high' })))
const material = read.filter(Boolean)
log(`readers returned ${material.length} of ${CLUSTERS.length} clusters`)

const CONTRACT = `The page contract: the file is one self-contained HTML page whose <title> is a name of two to four words (essay one was titled for its subject, essay two "Measured Beside"); a <meta name="description"> sentence; colors as custom properties on :root, redefined for dark mode under @media (prefers-color-scheme: dark) guarded by :root:not([data-theme="light"]) and again under :root[data-theme="dark"]; an explicit body background; stylesheets only from Google Fonts; no external script (inline SVG for charts); it works at phone width with a 16px side gutter and no horizontal page scroll; under 16 MB.`

phase('Draft')
const brief = `Write essay three of the dbchange v3 series to ${OUT}. Read essay one (${ESSAYS}/01-m0-m1.html) and essay two (${ESSAYS}/02-waves-1-4.html) first, in full: essay three belongs to the same series, so reuse essay two's stylesheet, tokens, fonts, layout and components (the header with the read-at line and the package line counts with their growth, the sections each with a small kicker of the packages it touches, the framed ledger entry closing each idea with Where, Closes, v2 and Cost, the charts in inline SVG drawn from the page's tokens), and its voice: plain, exact, technical English that names the file, the type, the SQL Server error number and the measured figure, states the circumstance, then the background, then the point, and lets the reader admire the work by seeing exactly why it is the way it is, not by being told to. No adjective of praise, no metaphor where a literal phrase exists, no "Here is why", no hedging. Essay three covers ${SPAN}: what waves 5, 6 and 7 built. The readers' material, with its evidence, is below as JSON; use it, and check anything you are unsure of in the repository at ${REPO} (read only; edit nothing there). Open with the situation in a paragraph or two (where essay two left v3, what these waves added, the habit that runs through them), then one section per idea, ordered so each builds on the last, then a section on where the weight sits (the package line counts at 77544d6 and at 28d6bd7, measured with the Budgets test's counting or wc -l over kernel/ io/ cli/ tests/ excluding bin/ and obj/; the test counts: fast and build lanes Kernel 674, Budgets 753, Io 1186, the fixture lane Io 383), the parity ledger as a chart (at 77544d6 V3_PARITY.md §0 recorded its count of 2026-10-19: held 21, partly held 22, not held yet 75; after wave 6: held 25, partly held 27, not held yet 66; after wave 7: held 28, partly held 52, not held yet 38; cutover tools 3 and retired 6 throughout; 127 rows), and a closing section on what wave 8 adds (WP 2.5's predict, classify and check cdc with M2's exits; WP 3.6's check, status and down; WP 3.7's profile and its folded signatures; WP 7.2's first knowledge files). Put the read-at line as read at 28d6bd7 on claude/v3-architecture-design-sl95mx, from 77544d6, 21 October 2026. ${CONTRACT} Every claim must be true of the code at 28d6bd7; never print a password. Return the path you wrote and a list of the claims you were least sure of.\n\nMaterial:\n${JSON.stringify(material)}`
const draft = await agent(brief, { label: 'draft', phase: 'Draft', effort: 'max' })

const ISSUES = {
  type: 'object',
  properties: {
    issues: {
      type: 'array',
      items: {
        type: 'object',
        properties: {
          where: { type: 'string', description: 'the section and the sentence, quoted' },
          problem: { type: 'string' },
          evidence: { type: 'string', description: 'what the code, test, commit or measurement shows, with file:line' },
          fix: { type: 'string', description: 'the corrected sentence or change' },
          severity: { type: 'string', enum: ['wrong', 'misleading', 'page', 'prose'] },
        },
        required: ['where', 'problem', 'evidence', 'fix', 'severity'],
      },
    },
  },
  required: ['issues'],
}

phase('Check')
const checks = await parallel([
  () => agent(`Fact-check the essay at ${OUT} against the dbchange v3 repository at ${REPO} at commit 28d6bd7 (read only: edit nothing, run no build). The writer was least sure of: ${draft}. Try to refute every factual claim: each file:line range, type and function name, SQL Server error number, DacFx behaviour, number, test name, decision date, commit hash, and each account of how v2 did something (archive/, searched with rg --no-ignore). Measure what can be measured. Report only claims that are wrong or misleading, each with its evidence and the corrected sentence; report none that you could not refute.`,
    { label: 'check:facts', phase: 'Check', schema: ISSUES, effort: 'max' }),
  () => agent(`Check the essay page at ${OUT} as a page and as prose. ${CONTRACT} Also check: it renders without a horizontal scroll at 375px wide (read the CSS; long code lines must wrap or scroll inside their own block), every SVG chart reads correctly in both themes and its numbers match the text, the series' components are used as essays one and two use them (${ESSAYS}/01-m0-m1.html, ${ESSAYS}/02-waves-1-4.html), and the prose holds the series voice: exact, literal, circumstance before background before point, no adjective of praise, no metaphor where a literal phrase exists, no announcing phrase ("Here is why", "Two things:"), no hedging, no sentence a reader must recover a meaning from. You may render it with Playwright (Chromium is at /opt/pw-browsers; do not run playwright install) to check layout at 375px and 1280px in light and dark. Report each issue with the fix.`,
    { label: 'check:page', phase: 'Check', schema: ISSUES, effort: 'high' }),
])
const issues = checks.filter(Boolean).flatMap(c => c.issues)
log(`checkers found ${issues.length} issues`)

phase('Revise')
const revised = issues.length === 0 ? draft : await agent(`Revise the essay at ${OUT} (essay three of the dbchange v3 series) by applying these checker findings. For a 'wrong' or 'misleading' finding, confirm it against the repository at ${REPO} (read only) before applying it, and apply it when confirmed; for a 'page' or 'prose' finding, apply the fix or a better one that meets the same point. Keep the series voice and the page contract. Return a list of the findings applied and of any not applied, with the reason.\n\nFindings:\n${JSON.stringify(issues)}`,
  { label: 'revise', phase: 'Revise', effort: 'high' })

return { path: OUT, draft, issues: issues.length, revised }
