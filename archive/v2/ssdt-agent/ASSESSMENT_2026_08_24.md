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
One caveat I owe the reader: I ran that proof in the environment where the tree already works —
Claude Code, with a Docker SQL Server — which is not the environment the team will use. So the
reproduction proves the method and the finding. It does not prove that the team can run either,
and section 6 is about exactly that gap.
Just as important, the tree has a documented habit of overturning its own past findings in the
open. When a later run disproved an earlier one, the earlier finding was struck with a dated note,
not quietly edited; the tree even records that some of its old evidence was narrative rather than a
real run, and that the narrative was hunted down and replaced with captured output. A tool that
publishes its own corrections does not ask to be trusted; it shows the evidence.

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

**The single largest gap is one I could not find by running the tree, because my sessions run in the
one environment where it works.** Both independent reviews caught it, and section 6 gives the
detail: the tree runs where I tested it, not where the team works. Its proving substrate assumes
Docker and the F# monorepo; its packaging assumes Claude Code; and it proves changes against the
small sample database, not the team's real tables. The team works in GitHub Copilot inside Visual
Studio, on Windows, against their own Azure DevOps repository and their own data, and the tree does
not yet reach any of those. This gap decides the verdict, so I state it first and give the reviews'
account of it in section 6. The weaknesses I found first-hand follow.

**The proving loop depends on a stable local database, and even in the ideal setup that dependency
is fragile.** I hit it first-hand: partway through the proof run, the local SQL Server degraded and
a publish hung until I restarted it. The restart took four seconds and the proof then ran cleanly,
so the tree's own code was never at fault — but the whole method rests on being able to publish to a
working local database on demand. If that database is slow to start or unstable on a developer's
machine, an agent will be pushed back toward guessing, which is the exact behavior the tree exists
to prevent.

**The proving ground is far smaller than the real estate.** Eight tables and about three dozen rows
stand in for a schema of a few hundred tables. The small size is deliberate and teaches the shapes
well, but it cannot teach scale or density. Cross-table cascades more than one level deep, cycles
in the dependency graph, and how long a change takes against a large table are not exercised
anywhere. The team will meet all three in the first weeks, and the tree has not rehearsed them.

**The teaching half is mostly unbuilt.** The graded practice curriculum, the scored certification
that would show whether a developer is ready to review without the rails, and the ledger that would
record the team's growing history are described but not implemented. This is acceptable — the tool
is meant to carry the cutover week, and the curriculum is the season after — but a reader should not
mistake the plan for the product.

**The proving engine is not yet pinned to the pipeline's, and one important behavior is known to
differ between versions.** Whether a new foreign key ends up trusted automatically depends on the
engine version, and the team's pipeline version is not yet recorded or checked. Section 6 explains
why this is not just hygiene but a real correctness risk: the skills state the trust behavior as
settled fact, and if the team's engine behaves like the older version, following them can produce a
green deploy that did not enforce the rule that was asked for. I built the ledger that will hold the
pinned versions; the actual numbers are the team's to record from the pipeline.

---

## 5. The pattern worth naming

The tree has two different kinds of weakness, and it is worth keeping them apart, because one is far
more reassuring than the other.

The first kind is its internal defects, and they follow a single pattern: each is a correct decision
that has not yet grown the check that keeps it correct. A review scenario that contradicted itself,
an old template lingering in three places, a doctrine change that reached most surfaces but not all
— every one of these is exactly what the tree's own doctrine predicts, because that doctrine says a
rule is not enforced until a detector would catch its absence. On these, the tree is not wrong about
the world; it is behind on wiring its own rules into checks. That is a far healthier failure mode
than being confidently wrong, and it is strong evidence that the method measures the right thing.

The second kind is different, and section 4's headline gap is its example: the tree was built for
one environment (Claude Code, Docker, the post-cutover repository) and the team works in another
(Copilot, Visual Studio, their live estate). That is not a missing detector. It is a deliberate
scoping — the tree's own connector notes say the Copilot target was left unbuilt on purpose, pending
confirmation of the format — that the calendar has now overtaken. This gap will not close by wiring
a check; it closes by building the delivery. It is the reason the verdict turns on prerequisites
rather than polish.

---

## 6. What the independent reviews found

Two agents reviewed the tree without having made this session's edits: one adversarial pass against
the current state, and one walkthrough that drove two real changes through the tree as a new SSDT
developer would. They converged on one finding above all others, and it is the finding this
assessment most needed, because it is the one my own testing was blind to.

**The convergence: the tree runs where I tested it, not where the team works.** I proved the
flagship change in Claude Code, against a Docker SQL Server, using the tree's Claude Code packaging.
The team has none of those. They work in GitHub Copilot inside Visual Studio, on Windows laptops,
committing to an Azure DevOps SSDT repository. The tree's substrate is Docker and monorepo
throughout: it brings up a named Docker container, runs commands the Windows host does not have,
works from a directory inside the F# project, and loads its skills through a Claude Code path that
will not resolve in the team's repository. There is no Windows-native or LocalDB proving path
anywhere in the tree. And even where it does run, it proves changes against the small sample
database, not the team's real tables — the bridge that would emit the real schema is documented as
unbuilt. The adversarial reviewer stated it plainly: the tree's core mechanism, prove the change
before classifying it, is operationally unavailable to this team as the tree stands. The walkthrough
reached the same wall from the other side — even the safest change in the catalog cannot skip
proving, and there is no advise-without-proving fallback, so if the substrate is not up, the method
stalls before it reaches the readable pull request.

This matters more than any other finding, because of what the team does when proving is unavailable:
they fall back on the classifications the skills state in prose, which is the guessing the tree
itself calls unsafe. And the most consequential of those stated classifications is not yet verified
on the team's engine.

**The sharpest correctness risk: the constraint-trust law is stated as settled but unproven on the
team's engine.** The tree teaches that adding a foreign key, a check, or a unique constraint
"validates and trusts itself" — the database re-checks every existing row as the constraint goes on,
and a violating row blocks the deploy. That behavior was proven on one version of the deployment
engine. On an older version, the same add read as untrusted: the constraint goes on without
re-checking the existing rows, so a violating row does not block, and the safety proof the tree
relies on does not fire. The team's pipeline engine version is not yet pinned or checked, and the
skill files state the trust behavior as fact with no version caveat. If the team's engine behaves
like the older one, an agent following these skills can produce a green deploy that quietly failed
to enforce the rule that was asked for — the worst failure this tree exists to prevent — while the
one reviewer who might catch it is away.

**A second silent-failure risk: the two-release tightening pattern reverts under a concurrent
unrelated publish.** The safe way to make a column required, on this pipeline, is two releases: the
first makes the change while the model still describes the old shape, the second lets the model
catch up. Between those two releases, the model deliberately lags. If a second developer ships an
unrelated Dev change in that window, their publish carries the lagging model and the deployment
undoes the tightening — and every check still shows green. The worked examples warn a developer not
to re-publish their own first release, but they do not warn about a second developer publishing
something else. Three new developers shipping concurrently during a fresh cutover is exactly the
condition that triggers this.

**The rest, in brief.** Both reviews confirmed the catalog blind spots — dropping a constraint,
changing a view, a computed column, a collation, or a trigger has no skill and nowhere to route, so
each dead-ends on a live database. Both confirmed the estate's scrutiny ledgers still hold only
sample data, so "at production row counts" and "first time on this estate" are answered against the
wrong facts. The adversarial reviewer added a set of concrete internal defects: the one
fully-worked make-mandatory example still uses the old eight-section shape and the word "Test,"
unlike the template and the other forty examples; the identity-swap proof runs on a table with no
incoming foreign key, so its dangerous leg is never exercised; the proven-and-unproven ledger still
marks one operation both; and the continuous-integration checks that keep the records honest live in
the monorepo, not in the estate repository the team will commit to. The walkthrough added that the
one hard-case example — turning free text into a lookup — demonstrates only the final step over an
already-clean domain, and that the tree structures the real work (reconciling dozens of misspelled
values, and sequencing an application cutover across releases) as if each were a single decision, so
a new developer comes away with a correct plan they are not equipped to carry out.

**What both reviews credited.** Neither review is dismissive, and both named real strengths that
bear on fitness: the two-release tightening examples are genuinely excellent, with real engine
output and the revert hazard surfaced; the phantom-rename and phantom-delete traps — the most
dangerous surprises in SSDT — are now taught correctly and explicitly; the translation from
OutSystems words to SQL is the best thing in the tree, and it never makes the developer learn an
SSDT word before proceeding; the record register is applied consistently across all forty-one
examples; and the continuous-integration checks exist, contrary to the tree's own older self-audit.

---

## 7. The verdict

**The method is proven and the authoring judgment is strong. The delivery of both to the team's
actual environment is not done, and that is prerequisite work, not a detail.** This is the honest
shift the two independent reviews force, and it is worth stating precisely, because "fit" and
"unfit" are each too blunt.

What is genuinely ready is the tree's method and its judgment. Proving the change against
real-shaped data and then classifying from what the engine did is sound, and its flagship result
reproduces on the real engine. Its handling of the worst SSDT traps — the phantom rename and delete,
the two-release tightening — is strong. Its register makes a pull request a reviewer can read.
Judged as a body of engineering judgment, it is a serious and unusually honest piece of work.

What is not ready is the path from that judgment to this team, next weekend. The team works in
GitHub Copilot and Visual Studio, against their own Azure DevOps repository and their own data. The
tree runs in Claude Code, against Docker, on the sample database. Until that gap is closed, the team
cannot use the core mechanism, and a team that cannot prove falls back on stated classifications —
one of which, constraint trust, is not yet verified on their engine and could produce a silently
wrong deploy. So the honest verdict for next weekend is: **not yet fit for the team's environment,
through no failure of the method, and fit only once the prerequisites below are met.**

The prerequisites, in the order they gate use:

1. A proving path that runs on a Windows and Visual Studio machine — LocalDB or a local SQL Server —
   pointed at the team's real SSDT project and a restored copy of their real Dev database. Without
   this, nothing else matters, because the method cannot run.
2. The packaging ported to GitHub Copilot in Visual Studio, so the skills load where the team works.
   This is the next task, and the research says it is straightforward.
3. The pipeline's engine version pinned, and the constraint-trust behavior confirmed once against a
   real publish. If it cannot be confirmed in time, add a version caveat and an "if the constraint
   is untrusted, do this" step to the foreign-key and constraint records.
4. The two-release operations given an explicit "hold other Dev publishes during the window"
   instruction, and the estate scrutiny ledgers filled with real row counts.
5. The reviewer-absence rule followed while the principal is out — two senior reviewers, a proven
   backup, and the principal's confirmation on return.

Meet those, and the strong method reaches the team intact. Skip them, and the team gets a tool that
either cannot run, or — worse — runs against the wrong data and the wrong engine while every check
shows green.

**As a curriculum: early, and that is fine.** The teaching machinery is the next season's work.

**The one-line stance.** A serious and unusually honest piece of engineering, whose method is proven
and whose judgment is strong, but which today runs where I tested it, not where the team works. The
remaining work is delivery, not method: put a real proving path on the team's machines, verify the
one unproven law on their engine, port the packaging, and the tool is ready to lean on. Until then,
it is a strong draft aimed at an environment the team does not have.

---

## 8. Risks to watch during the cutover week, most serious first

1. **The team cannot prove.** If the substrate does not run on their machines, or the skills do not
   load in Copilot, or proving runs against the sample instead of their real data, the tree's core
   mechanism is gone and the team is left with prose classifications the tree itself calls unsafe.
   This is the prerequisite that gates everything; close it first (section 7).
2. **A constraint that lands untrusted without anyone noticing.** If the pipeline's engine behaves
   like the older version, a new foreign key, check, or unique constraint can go on without
   re-checking the existing rows, so a violating row does not block the deploy. The result is a
   green deploy that did not enforce the rule. Until the engine version is pinned and the trust
   behavior confirmed, treat every "the constraint is trusted" claim as assumed, and check
   `is_not_trusted = 0` after every constraint deploy.
3. **A tightening silently reverted by a concurrent publish.** While a two-release change waits with
   its model lagging, another developer's unrelated Dev publish can undo it, with every check green.
   Hold other Dev publishes during a two-release window, or sequence the two releases close together.
4. **A change the catalog does not cover.** A view, a trigger, a computed column, a collation
   change, or a constraint drop with no skill — the agent will improvise on a live database. Agree
   in advance that an uncovered change goes to a human, not to an improvising agent.
5. **The first promotion into QA or UAT.** Those environments were cut over separately and may hold
   schema the Dev model does not expect. The first promotion needs a full schema compare that shows
   only the intended change; anything else is leftover drift and must be reconciled first.
6. **The principal being out.** The reviewers most likely to face a data-destroying change are the
   three who are new to SSDT. The absence rule — two senior reviewers, a proven backup, confirmation
   on return — is the safety net, and it only works if it is followed.

---

## 9. What would raise confidence further, in rough priority

- Stand up a proving path on a real corporate machine — Windows, Visual Studio, a LocalDB or local
  SQL Server pointed at a restored copy of the real Dev database — and run ten real changes through
  it end to end before the week. This is the highest-value work available, because it closes the
  prerequisite that gates everything else.
- Build the GitHub Copilot and Visual Studio packaging, so the skills load where the team works.
- Record the pipeline's engine version and flags in the toolchain ledger, and confirm the
  constraint-trust behavior once against a disposable copy. If it reads untrusted, add the remedy to
  the constraint records before the week.
- Add a "hold other Dev publishes during the window" line to the two-release operations, and fill
  the estate scrutiny ledgers with real row counts.
- Fix the concrete internal defects the reviews found: re-capture the make-mandatory golden example
  in the current ten-section shape, reconcile the one operation marked both proven and unproven, and
  resolve the "first time on this estate / added scrutiny: none" contradiction. Each is small.
- Add the missing catalog operations, constraint drops first, so fewer real changes fall through to
  improvisation. Grow the proving ground toward the real estate's shape and scale, so cascades and
  large-table timing get rehearsed before they are met in production.
