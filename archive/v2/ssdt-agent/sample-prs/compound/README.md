# Compound exemplars — proofs at the release grain

The catalog's 41 worked examples each prove one operation. Real pull requests are usually
**molecules**: several operations shipping as one release, or one program shipping across
several. The deployment engine compiles **one script per release**, so per-operation proofs do
not compose into a release verdict — ordering, a blocking atom vetoing its innocent siblings,
a pre-deployment script one phase leaves behind, and the post-deployment seed's claim over
every column it names are all release-grain behavior. **The unit of proof is the release
delta**: whatever the pull request actually ships is what gets published to the disposable
copy, however many operations it carries.

Each exemplar here was proven live on a throwaway copy (sqlpackage 170.5.76, this branch,
2026-08-28), publish by publish, and each carries at least one finding no single-operation
example teaches:

- `additive-batch.md` — six additive atoms in ONE release, publishing clean in one delta: the
  engine orders the objects, the new NOT NULL column's default stamps every existing row, and
  every foreign key lands trusted. The fewest-releases packing (`../../skills/decompose/SKILL.md`)
  confirmed against the engine.
- `rename-then-tighten.md` — a rename and a tightening of the SAME table in one release: the
  tightening's guard blocks the publish and the WHOLE release rolls back, the innocent rename
  included. One blocking atom vetoes its siblings — the proven reason reshape-coupled atoms
  serialize. Includes the serialized fix, with a real refactorlog entry and the seed rename the
  change set must carry.
- `extract-to-lookup-program.md` — the full multi-phase program driven end to end: the migrate
  release (pre-deploy reconcile + a foreign key landing trusted over the populated child), the
  contract release the locked gate forces into two, and three mid-flight findings the program
  surfaced — the seed undoing a pre-deploy repoint, the seed failing over a dropped column, a
  stale phase-bound pre-deploy block breaking the next phase — plus the revert hazard captured
  on a green publish.

These are records in the register (`../../THE_RECORD.md` §2), shaped by the compound form in
`../../skills/author-pr/SKILL.md` §"The compound record". A compound pull request follows that
form; the per-operation fragments still come from each atom's own skill.
