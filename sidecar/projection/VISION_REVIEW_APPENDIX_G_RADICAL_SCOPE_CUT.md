# Appendix G — Radical Scope Cut on the "Informational Widening"

**Date:** 2026-05-08
**Reviewing:** VISION.md @ commit `2fb51ef`; VISION_REVIEW.md
**Brief:** For each "informational widening" item in VISION.md, give a defer-vs-keep verdict with hard triggers, focused on minimizing V2's surface so cutover-critical work ships fast.
**Synthesis location:** `VISION_REVIEW.md`, `VISION_REVISION_2.md`

---

| # | Item | Cost | Cutover value | Post-cutover value | Algebraic distance | Verdict | Trigger |
|---|---|---|---|---|---|---|---|
| 1 | V2 outlives OutSystems / Catalog as platform-survival | small (rhetoric only) | none | small (only if migration off OutSystems is real) | trivial (Catalog already source-agnostic; V1↔V2 vocab table proves it) | CUT | — (already implicit in source-agnostic naming convention; no extra surface earns it) |
| 2 | Profile as longitudinal evidence (time/env/population) | medium (Profile-diff IR + storage) | none | medium | expensive (needs temporal/identity over Profile snapshots, env labelling, comparison algebra; not a sibling Π) | DEFER-WITH-TRIGGER | First time a real operator question requires comparing two persisted Profiles (e.g., "did P95 of X drift between QA and prod?") and DistributionsEmitter+grep can't answer it. |
| 3 | Six-dimension synthetic data quality scoring | large (six undefined metrics; research-grade) | none | small (Faker itself is post-cutover) | expensive (six axes have no existing algebra; not a sibling Π) | CUT | — (six adjectives with zero defined metrics; if Faker ever lands, ship one dimension on demand per Appendix C §3) |
| 4 | AI-agent substrate (Playwright/test/code/copilot) | large (per-agent integrations) | none | small-to-medium (speculative) | mostly trivial *as outputs* (Catalog-as-text already exists in RawText/Json), but "substrate" framing implies harness work | CUT framing; KEEP only the byproduct fact that Json/RawText emitters already serve agents | — (no new chapter; document the corollary in one sentence if at all) |
| 5 | Recipes-as-Terraform (compose, provisioning, Playwright) | small for canary's compose; large for the rest | small (canary compose is byproduct) | small | trivial for canary's compose; expensive for full Terraform-class scope | DEFER-WITH-TRIGGER (scoped to canary's compose only) | A second consumer asks for the canary's compose file outside the canary loop (e.g., per-developer SQL Server ask). |
| 6 | Per-developer Docker SQL Server with Profile-shaped synthetic data | large (FakerEmitter + provisioning + per-dev workflow) | none | medium | expensive (depends on Faker, which is itself deferred) | DEFER-WITH-TRIGGER | A developer files an environment-stand-up request that the canary's compose file can't already answer, AND Faker has shipped against a real consumer. |
| 7 | GraphQL schema + resolver emitters | small for schema (sibling Π); medium for resolvers | none | small-to-medium | schema = trivial sibling Π (`Π : Catalog → graphql.sdl text`); resolvers = expensive (runtime, not text) | DEFER-WITH-TRIGGER (schema only; cut resolvers) | A real consumer needs to query the Catalog from outside the F# core (e.g., a tool authoring against the domain). At that point, schema is half a session; resolvers stay deferred. |
| 8 | Drift detection | small (read-side adapter pointed at four DBs, scheduled job) | medium-to-large (Appendix C: cutover safety net, currently underweighted) | large | trivial (read-side adapter is already a canary deliverable; just point it at four DBs) | KEEP-IN-VISION (and *promote* from post-cutover trajectory to cutover-critical, per VISION_REVIEW) | — |
| 9 | CI/CD substrate (canary on every PR, refreshed Playwright plans) | medium for PR-canary; large for Playwright refresh | small (PR canary is the canary loop with a different trigger) | medium | trivial for PR-canary (canary in a workflow file); expensive for Playwright plan refresh | DEFER-WITH-TRIGGER (PR-canary only; cut Playwright refresh) | First post-cutover schema-evolution PR where operator wants pre-merge canary signal. |
| 10 | Personal tooling (V2 packageable per module) | medium (packaging story) | none | small | expensive (host-shell + module-scoped Catalog filtering) | DEFER-WITH-TRIGGER | Maintainer's "flow metrics from code review app" use case becomes a real ask, not a hypothetical. |
| 11 | V1 sunset | small to write the rule; large to execute | none | medium | trivial as policy (an ADMIRE table state); the *execution* is gated on canary track record | KEEP-IN-VISION (one sentence: "ADMIRE.extracted requires a green canary; sunset deferred until all four envs run on V2 emissions for one schema-evolution cycle" — already in VISION_REVIEW Appendix C §7) | — |
| 12 | Open-source / community contribution | medium-to-large (license, governance, sanitization) | none | none | trivial-as-aspiration; expensive-as-action | CUT | — (the "optional; the option emerges" hedge already concedes it doesn't belong) |

---

**7. Platform-survival framing.** Scope inflation. The Catalog being source-agnostic is already structurally true — `Kind`/`Module`/`Catalog` naming and the V1↔V2 vocabulary table at `sidecar/projection/README.md` lines 218–227 prove it. "V2 outlives OutSystems" adds no surface area; it just relabels existing discipline. Migrating off OutSystems would require a *new adapter* (the same shape as `Projection.Adapters.Osm`) and nothing else V2 doesn't already plan to ship. The framing is rhetorical, not load-bearing. CUT the section; the source-agnostic-naming convention in README.md already carries the substance.

**8. AI-agent substrate.** All post-cutover trajectory, *except* the trivial fact that JsonEmitter and RawTextEmitter already produce agent-consumable Catalog projections. There is no cheap pre-cutover piece that helps the cutover — agents tightening rules requires Profile + Policy literacy that no current LLM tool consumes. Drop the section entirely; if it earns a sentence anywhere, it's "Json/RawText emitters happen to be agent-legible," recorded as a corollary in DECISIONS.md, not a vision pillar.

**9. Recipes framing.** The canary's testcontainers compose file is a credible byproduct, and it gives maybe 30%, not 80%, of "recipes." It's a single SQL Server stand-up scoped to the canary's needs (version pinning, ephemeral, no seed data). The other 70% — Playwright invocations, provisioning scripts, Profile-shaped synthetic data tuning — depends on Faker (deferred) and Playwright integration (no forcing function). Minimum scope worth keeping: *the canary's compose file is documented as a reusable artifact* — one sentence, no chapter. Everything else cuts.

**10. What to actually CUT (not just defer) from VISION.md.**

- **"V2 outlives OutSystems" / Catalog as platform-survival** (item 1). The Catalog's source-agnosticism is already structural in the V1↔V2 vocabulary mapping. Re-asserting it as platform-survival inflates rhetoric without adding obligation. Source-agnostic naming earns the property; the section earns nothing.
- **Six-dimension quality scoring** (item 3). Six undefined adjectives. Per Appendix C §4, this is the single biggest scope risk and "research-grade" — it has no metric definitions, no consumer, and no cutover dependency. Faker itself can stay as a post-cutover possibility; the *scoring* leaves the document.
- **AI-agent substrate as a section** (item 4). No cheap piece helps the cutover; the rest is post-cutover speculation. The Catalog being agent-legible is a corollary, not a pillar. One sentence in DECISIONS.md if at all.
- **Open-source / community contribution** (item 12). The vision already hedges with "Possibly" and "the option emerges from the work whether or not it gets exercised" — that's the document conceding the item doesn't belong. Cut and revisit only if a maintainer files an explicit OSS proposal with license/governance/sanitization commitments.
- **Playwright-plan refresh under CI/CD** (sub-item of 9). No forcing function; depends on AI-agent substrate that's also being cut.
- **Per-developer Docker SQL Server with Profile-shaped synthetic data** (item 6) — *consider cutting*; transitively depends on Faker, which is deferred. Defer-with-trigger is defensible, but a cleaner VISION.md would just CUT and let it re-enter via DECISIONS.md if a real developer asks.

**Net effect on VISION.md.** §"The informational widening" shrinks from five paragraphs to two: drift detection (promoted to cutover-critical per Appendix C §5), and a single-sentence corollary that Json/RawText emitters happen to be agent-legible. §"The post-cutover trajectory" shrinks from eight bullets to four: drift detection, schema evolution, GraphQL schema (only, no resolvers), V1 sunset rule. The vision goes from aspirational manifesto to load-bearing spine — which is what VISION_REVIEW.md §"Closing assessment" already names as the goal.

Files referenced:
- `sidecar/projection/VISION.md`
- `sidecar/projection/VISION_REVIEW.md`
- `sidecar/projection/VISION_REVIEW_APPENDIX_C_SCOPE_FEASIBILITY.md`
- `sidecar/projection/README.md`
