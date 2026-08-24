# An assessment of the ssdt-agent tree

**Written 2026-08-24, in the week before the Dev cutover. This is an outside evaluation of
`sidecar/projection/ssdt-agent/`: what the tree is for, and how well it serves that purpose.**

The assessment rests on three kinds of evidence. First, a full read of the tree and the corpus
around it — the charter documents, the 41 operation skills and their shared knowledge layer, the
three agents, the self-test suite and its captured golden runs, the proving ground, the register
standard, the parent program the tree sits inside, and the source curriculum it distills. Second,
first-hand work inside the tree during this session: I armed its register standard, rewrote a batch
of its prose to that standard, and ran its proving loop end to end against a live SQL Server.
Third, two independent agents reviewed the tree without having made any of those edits — one
adversarially, one by walking real changes through it as a new SSDT developer would. Section 6
carries what they found.

I have one bias to declare up front. I spent several hours editing this tree today, so I am no
longer a neutral reader of it. The two independent reviews in section 6 exist to catch what a
person who just changed something can no longer see.

---

## 1. What the tree is

The ssdt-agent tree is a working tool, not documentation about a tool. It is a set of instructions
that a frontier language model loads in order to help a developer change a database schema safely.
It has three parts, and they work together:

- **Three agent roles.** An intake agent turns a plain request into one named operation. An author
  agent drafts the change, proves it, and writes the pull request. A reviewer agent reproduces the
  proof on its own copy of the database and rules on it.
- **A catalog of 41 operations.** Each schema change the team might make — add a column, make a
  column required, add a foreign key, drop a table, split an entity — has a skill file that says how
  that change ships and who must review it. A shared layer underneath holds the cross-cutting
  knowledge several operations depend on.
- **A proving ground.** A small SQL Server project, seeded with deliberately awkward data, that the
  agent publishes changes against so it can watch what the deployment engine actually does.

The method has one governing idea, and the tree states it plainly: you cannot tell how a schema
change will behave by reading its SQL. The same edit ships one way against an empty table and a
different way against a table that already holds rows. So the tree does not give advice from the
text. It publishes the change to a disposable copy of the database and reads what the engine does
with real-shaped data. The tree's phrase for this is "proving is classifying."

---

## 2. What the tree is for

The tree serves one primary purpose and one secondary one, and the difference between them decides
the verdict.

**The primary purpose is a tool the team uses now.** After the cutover, a mixed-experience
OutSystems team owns its database schema in SSDT for the first time. Most of the team knows SQL
well but has never used SSDT. The tree's job is to let one of them state a change in plain words
and, inside the same working session, come away with a proven change and a pull request that a
reviewer can approve by reading. The measure of success is not technical. It is organizational: the
team keeps working at the speed it had under the managed OutSystems workflow, at higher safety, and
does not miss a beat during the cutover.

**The secondary purpose is a curriculum.** The tree is also built to teach — to move the team from
needing the rails, to reviewing well, and eventually to authoring changes without an agent's hand
on every one. This purpose is real and the tree states it. But it is the second act. Most of its
machinery — a graded practice curriculum, a scored certification loop that improves the weakest
skill each week — is planned, not built.

The tree is close to fit for the first purpose and early in the second. The rest of this assessment
keeps the two apart, because a reader who judges the tree as a finished training program will
undervalue it, and a reader who judges it as a finished product will miss how much of the teaching
half is still on paper.

---

## 3. What the tree does well

**It proves instead of advising, and it proves against itself.** This is the tree's strongest
property and the reason it can be trusted. The central claim reproduces: I ran the flagship change
(make an existing column required, on a table that holds rows) end to end against the live SQL
Server, on the exact deployment-engine version the team's sessions install. The deployment refused
it with the precise error the tree documents. Then I cleared every blank value the change would
have tripped on and published again — and it still refused, with the same error, because the guard
fires on whether the table has rows at all, not on whether the column has blanks. That is the
single most important and least intuitive finding in the tree, and it held on the real engine.
Just as important, the tree has a documented habit of overturning its own past findings in the
open. When a later run disproved an earlier one, the earlier finding was struck with a dated note,
not quietly edited; the tree even records that some of its old evidence was narrative rather than a
real run, and that the narrative was hunted down and replaced with captured output. A tool that
publishes its own corrections is a tool that does not ask to be trusted. It shows the receipt.

**Its safety model matches the real pipeline.** The team's pipeline always publishes with the
data-loss guard on and cannot relax it for a single deploy. The tree treats that as a fixed law,
not a preference, and builds the whole authoring machine around it. The one transition that would
be dangerous — carrying the model change and the hand-written data fix in the same release, which
was proven to fail halfway and leave the schema in a broken state — is a transition the machine
simply does not have. An agent following the tree cannot reach it.

**It is unusually consistent for its size.** Every cross-reference in the skill tree resolves; the
41 operations map one-to-one onto the 41 worked example pull requests and onto the catalog that
lists them; the publish settings quoted in the skills match the real settings files exactly. A tree
this large usually drifts against itself. This one mostly does not, because it runs its own checks
in continuous integration.

**It keeps teaching and evidence on separate surfaces.** The tree writes in two registers, on
purpose. It teaches the developer the reason a change behaves as it does, in conversation. It hands
the reviewer a finding and its evidence, with no teaching, on the record. That separation is what
makes "a pull request a reviewer approves by reading" achievable rather than aspirational, and a
lint enforces it.

---

## 4. Where the tree is weak or unproven

**The proving ground is far smaller than the real estate.** Eight tables and about three dozen rows
stand in for a schema of a few hundred tables. The small size is deliberate and teaches the shapes
well, but it cannot teach scale or density. Cross-table cascades more than one level deep, cycles
in the dependency graph, and how long a change takes against a large table are not exercised
anywhere. The team will meet all three in the first weeks, and the tree has not rehearsed them.

**The proving loop depends on a stable local database, and that dependency is fragile.** This is the
largest operational risk, and I hit it first-hand: partway through the proof run, the local SQL
Server degraded and a publish hung until I restarted it. The restart took four seconds and the
proof then ran cleanly, so the tree's own code was never at fault — but the whole method rests on
being able to publish to a working local database on demand. On the team's corporate laptops, if
that database is slow to start or unstable, an agent will be pushed back toward guessing, which is
the exact behavior the tree exists to prevent. The tree names this as an open problem in its own
findings. It is real, and it is worth solving before the team leans on the tool.

**The teaching half is mostly unbuilt.** The graded practice curriculum, the scored certification
that would show whether a developer is ready to review without the rails, and the ledger that would
record the team's growing history are described but not implemented. This is acceptable — the tool
carries the cutover week, and the curriculum is the season after — but a reader should not mistake
the plan for the product.

**The catalog has known blind spots.** The 41 operations cover the common changes, but the tree's
own audit lists gaps: several "undo" operations exist without their matching "redo," and whole
categories — views, triggers, computed columns, collation changes — are not owned by any skill. A
change of one of those kinds will find no skill to load, and the agent will be improvising exactly
where the team is least able to check it.

**The packaging targets the wrong tool for this team.** The tree is packaged for Claude Code. The
team develops in GitHub Copilot inside Visual Studio. The port is researched and looks
straightforward — recent Visual Studio reads the same skill files the tree already emits — but it
is not built yet. Until it is, the tree runs where I am testing it, not where the team works.

**The proving engine is not yet pinned to the pipeline's.** The version of the deployment engine
used for local proving is not yet aligned to the version the Azure DevOps pipeline runs, and one
important behavior — whether a new foreign key ends up trusted automatically — is known to differ
between versions. I built the ledger that will hold the pinned versions; the actual numbers are the
team's to record from the pipeline, and until they are recorded, the trust-related findings assume
the configured behavior rather than the proven one.

---

## 5. The pattern worth naming

The most reassuring thing about this tree is not any single strength. It is that its weaknesses are
the ones its own method predicts. The tree's doctrine says a rule is not enforced until a detector
would catch its absence. Every real defect I found in it — a review scenario that contradicted
itself, an old template lingering in three places, a doctrine change that reached most surfaces but
not all — was a case of exactly that: a correct decision that had not yet grown the check that keeps
it correct. The tree is not wrong about the world. It is behind on wiring its own rules into checks.
That is a far healthier failure mode than being confidently wrong, and it is strong evidence that
the method measures the right thing.

---

## 6. What the independent reviews found

*(Filled in from the two independent agent reviews — the adversarial red-team pass and the
new-developer walkthrough. See below.)*

<!-- PENDING: red-team + cold-walkthrough findings -->

---

## 7. The verdict

**As a tool for the cutover week: conditionally fit.** The tree reproduces its central proof on the
real engine, its safety model matches the pipeline that will actually deploy the changes, and its
internal structure is sound. Its failure modes are not "gives a confident wrong answer" — the
proving discipline guards against that. Its failure modes are "cannot run," "is not loaded where the
team works," and "meets a change it does not cover." Those are the three to watch, and all three are
addressable. The conditions that would make it fit, plainly:

1. A stable local SQL Server on each developer's machine, proven before the week, so proving never
   hangs.
2. The deployment-engine version pinned to the pipeline's, and the trust behavior confirmed once
   against it.
3. The packaging ported to GitHub Copilot in Visual Studio, so the tree loads where the team works.
4. The reviewer-absence rule actually followed while the principal is out — two senior reviewers,
   a proven backup, and the principal's confirmation on return — because that is the safety net for
   exactly the changes the SSDT-new reviewers are least ready to judge alone.

Meet those four, and the tree will help the team more than it will mislead them, which is the honest
bar for a first cutover.

**As a curriculum: early, and that is fine.** The teaching machinery is the next season's work. The
tool carries the week; the curriculum grows the team afterward.

**The one-line stance.** This is a serious, unusually honest piece of engineering that does the hard
thing — it proves rather than advises — and mostly does it well. It is close to ready as a tool and
early as a school. The risks that remain are operational and external (a stable database, the right
packaging, the version pin, the review rule) rather than failures of the method itself. Clear those,
and the team can lean on it.

---

## 8. Risks to watch during the cutover week, most serious first

1. **A flaky local SQL Server.** If proving hangs or fails on a developer's machine, the agent loses
   the one thing that separates it from guessing. Prove the local database is stable on every
   machine before the week starts.
2. **A change the catalog does not cover.** A view, a trigger, a computed column, or an "undo" with
   no skill — the agent will improvise. Agree in advance that an uncovered change goes to a human,
   not to an improvising agent.
3. **The first promotion into QA or UAT.** Those environments were cut over separately and may hold
   schema the Dev model does not expect. The first promotion needs a full schema compare that shows
   only the intended change; anything else is leftover drift and must be reconciled first.
4. **The principal being out.** The reviewers most likely to face a data-destroying change are the
   three who are new to SSDT. The absence rule is the safety net; it only works if it is followed.
5. **The version pin left unrecorded.** Until the pipeline's engine version is pinned and its trust
   behavior confirmed, treat any "the constraint is trusted" claim as assumed, and check it after
   deploy.

---

## 9. What would raise confidence further, in rough priority

- Stand up the proving substrate on a real corporate machine and run ten real changes through it,
  end to end, before the week. This is the highest-value hour available.
- Build the GitHub Copilot / Visual Studio packaging, so the team uses the tree where it works.
- Record the pipeline's engine version and flags in the toolchain ledger, and confirm the trust
  behavior once against a disposable copy.
- Add the missing catalog operations, drops-with-no-matching-add first, so fewer real changes fall
  through to improvisation.
- Grow the proving ground toward the real estate's shape and scale, so cascades and large-table
  timing get rehearsed before they are met in production.
