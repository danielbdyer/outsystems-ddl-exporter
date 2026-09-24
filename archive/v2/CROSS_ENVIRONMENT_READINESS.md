# CROSS_ENVIRONMENT_READINESS.md — proving the estate is one shape before cutover

> **Status: design, 2026-06-21.** The build sheet for the **espace-safe cross-environment
> readiness check**: prove that the OutSystems cloud environments (`cloud-dev` / `cloud-qa` /
> `cloud-uat`) resolve to the **same logical/SSDT shape**, and surface every data dealbreaker,
> *before* the migration runs. Grounded in read-only code traces (every `file:line` below is
> from the live source on `main` @ `b06324e4`). The forcing function is one law —
> **espace-invariance** — with an `AxiomTests` witness and a warm-Docker canary.
>
> **Reading order.** Sits beside `THE_CONFIG_CONTROL_PLANE.md` (A44: expressible ⇔ reachable —
> this surface is a new point in it), `THE_DATA_PRODUCERS.md` / `CatalogRendition.fs` (the
> rendition machinery this reuses), `DATABASE_ARCHETYPES.md` (the sibling "declare-then-verify"
> discipline), and `THE_CLI.md` §8 (the `check` verb family this extends). The operator
> workflow it enables is the cutover-readiness gate `CUTOVER_READINESS_BRIEF.md` assumes but
> never builds (that brief is V1→V2 *flip* readiness; this is *estate-shape* readiness).

---

## 0 — The one idea

The cutover rests on an assumption the operator currently takes on faith:

> **The three cloud environments host the *same logical model*** — same entities, same
> attributes — so the `cloud-dev`-authored schema is *the* schema for all three, and each
> environment's data lands in that one shape. Only the per-environment data differs.

That assumption is **cutover-critical and must be proven, not hoped** — because OutSystems
*espacing* means the physical realization differs per environment: the same logical entity
`Customer` is `OSUSR_ABC_CUSTOMER` in one cell and `OSUSR_XF_CUSTOMER` in another, and the same
holds at the column grain. A check that keys on physical names would either miss real
divergence or invent false divergence. The readiness check proves the real thing:

> **Same logical shape across all three environments, modulo espace · plus zero data
> dealbreakers · ⇒ ready to execute.** Anything else is named, counted, and blocks.

It answers the operator's three pre-cutover questions in one run (the operator's own framing):

1. the schema (translated to its logical form) is **identical across all three** environments;
2. the data in each environment **conforms to the agreed exported schema's shape**;
3. every **data issue** (orphaned FKs, NULLs in a NOT-NULL column, duplicates in a UNIQUE,
   width/type overflow) is understood **across all three**, before the activity.

---

## 1 — Why the naive compare is espace-*unsafe* (the defect)

A cross-environment `projection compare <a> <b>` / `diff` today is espace-unsafe for a
*vanilla* OutSystems environment (one this engine did not itself emit). The chain:

1. A `live:` operand resolves through `Source.ofLive` → **`ReadSide.read`** — the *physical*
   `INFORMATION_SCHEMA`/`sys.*`, **not** OSSYS (`Ref.fs:63-67`; `Source.fs` `ofLive`).
2. `ReadSide` **synthesizes** each identity from the *physical coordinate* —
   `SsKey.synthesized "READSIDE_KIND" "schema.table"` and `"READSIDE_ATTR"
   "schema.table.column"` (`ReadSide.fs:69-76`) — *unless* a `Projection.SsKey` / `V2.SsKey`
   extended property is present to recover (`ReadSide.fs:1140-1150`, `:1555-1566`). A vanilla
   OutSystems cell has **no such property** (only engine-emitted schemas do).
3. `CatalogDiff.between` matches kinds/attributes **by `SsKey`** (`CatalogDiff.fs:633-660`;
   `RunFaces.fs:1396` "compares by SsKey"). Two cells with different espace keys synthesize
   **different** SsKeys ⇒ the same logical entity is reported as **Add + Drop (unrelated)**,
   never the same thing.
4. So the fix is **identity, not projection**: read each env via OSSYS (S1) so the GUIDs align;
   `CatalogDiff` is already physical-agnostic (§2), so once identity aligns the espace names fall
   out of the comparison on their own. The defect is purely the `ReadSide` identity source.

`CatalogRendition.fs:6-14` states the same root cause outright: *"A live read cannot produce them
(`ReadSide` synthesizes attribute SsKeys from physical coordinates, so two independent reads never
align)."* (The reverse leg solves it by rendering one model at two renditions; the readiness check
solves it more cheaply — by reading each env's *own* OSSYS GUIDs, which already align.)

---

## 2 — The law: espace-invariance (A45 candidate · L2 + L3)

> **Espace-invariance.** Two environments hosting the same OutSystems model are the **same
> shape**, at **both the kind and the attribute grain**, regardless of their per-environment
> physical (`OSUSR_{key}_…`) coordinates. Formally: for catalogs `A`, `B` that differ *only*
> in physical-realization slots (the `OSUSR_*` table/column physical names), `A ⊖ B = ∅`
> under `CatalogDiff` — no projection required.

This is **not new algebra** — it falls out of two facts already in the code, named here so it
becomes a forcing function:

- **OSSYS identity is cross-environment-stable (A1 / the four-variant `SsKey`, AXIOMS.md §A1
  amendment `:1156-1195`).** `OssysOriginal g` — the native OutSystems GUID — *"honors A1
  **unconditionally**"* (`AXIOMS.md:1182`). The operator has confirmed (2026-06-21) this holds
  across the estate's environments **for attributes as well as entities**: LifeTime promotion
  preserves the SS_KEY at every grain. So an OSSYS read (S1) yields aligned identity across
  environments where a physical `ReadSide` read (which synthesizes the SsKey from the
  espace-varying physical name) does not.
- **`CatalogDiff` ignores physical table/column NAMES.** It matches by `SsKey`, reports a rename
  only on a logical-`Name` change, and **never** compares `Kind.Physical` (the `OSUSR_*` table
  name) or the physical column name (`CatalogDiff.fs:286-296` / `KindFacet :180-184`).
  *Caveat the canary forced (§7 S4):* its facet set DOES compare three physical-REALIZATION
  artifacts that OutSystems names *after* the physical table — the default-constraint name
  (`DefaultName`, inside the `DefaultValue` facet), `Triggers`, and `ColumnChecks`. Those vary per
  espace, so the readiness check **normalizes them to the logical shape first**
  (`Readiness.toLogicalShape`: drop the constraint name — keep the default VALUE; drop triggers +
  column checks, which the engine regenerates deterministically from the logical model on
  emission). After that normalization the diff is espace-safe at *every* grain.

Put together: two OSSYS-read catalogs of one model carry the same GUIDs + logical names +
structure, so after the realization-artifact normalization `CatalogDiff.between` returns **zero**
— the espace-varying coordinates AND their derived constraint/trigger/check names are gone from
the comparison. The safety is the composition of **OSSYS identity** (stable GUID) + a
**physical-name-agnostic diff** + a **targeted realization-artifact normalization**.
(`CatalogRendition.logical` — the reverse leg's table/column rename — is NOT needed, because
`CatalogDiff` already ignores those names; the normalization that IS needed is the narrower
`toLogicalShape`.) The law is L2 (the algebra above) and L3 (operator promise: "the estate's
environments are provably one shape"); numbering settles at build time per the standing rule —
*every `AXIOMS.md` change ships its `AxiomTests.fs` witness in the same commit*.

**The witnesses (the forcing functions).** (1) A pure property test (`CatalogDiffTests`): build
an OSSYS-identity catalog, derive a twin differing *only* in espace coordinates (table/column
physical names) holding GUIDs/logical-names/facets fixed; assert `CatalogDiff.between` reports
**zero**, with a sensitivity counter-test (a type change must surface). (2) A pure normalization
test (`ReadinessTests`): two catalogs differing *only* in the default-constraint NAME compute as
**Ready**. (3) **The two-DB Docker canary** (`OssysComprehensiveFixtureTests`): deploy two
espace-variant OSSYS DBs of one model (same GUIDs, `OSUSR_*` → `OSUSR_X*`, so the derived
constraint/trigger/check names diverge too), read BOTH via the real OSSYS path, assert the
readiness gate sees ONE shape. **Canary (3) is what caught** that pure-test (1) — which left the
realization facets empty — was an incomplete claim; the `toLogicalShape` normalization is its fix.

---

## 3 — The mechanism (three existing pieces, one new seam)

| # | Need | Reuse | Citation |
|---|---|---|---|
| 1 | **Espace-safe identity** — read each env via **OSSYS** (native GUID), not `ReadSide` | `LiveModelRead.fromConnSpec : connSpec → Task<Result<Catalog>>` | `LiveModelRead.fs:113`; `ModelResolution.fs:40-45` |
| 2 | **Realization-artifact normalization** (`toLogicalShape`: drop constraint names / triggers / column checks; keep logical shape + default VALUE) before the **physical-name-agnostic** `CatalogDiff` + data dealbreakers | `Readiness.toLogicalShape`; `CatalogDiff.between` + `Compare.compute` (`ModelFidelity`: NOT-NULL / UNIQUE / orphan-FK / overflow) | `Readiness.fs`; `CatalogDiff.fs:286-296`; `Compare.fs:106-123` |
| — | **New seam** — an OSSYS-sourced operand + the normalization + an N-way readiness aggregator + a config block | — | this doc §4–§5 |

**The verdict is the `CatalogDiff` norm over the normalized catalogs — nothing more.** After
`toLogicalShape` (§2), two OSSYS-read catalogs of one model return zero; a non-zero norm is a
*real* logical divergence (an added/dropped/reshaped kind or attribute, a changed type / nullability
/ default-VALUE / FK), never an espace artifact. There is no "espace rename" channel to discount —
neither the physical names nor their derived constraint/trigger/check names enter the comparison.

The data-dealbreaker half is *already* exactly what the operator asked for: `Compare.compute`
profiles the source env's data and runs it against the target's declared model, flagging
"NOT NULL declared, NULLs would land" / "FK orphans would land" / "UNIQUE declared, duplicates
would land" / "length/type overflow" (`Compare.fs:170-175`). The readiness check runs that for
each environment against the agreed (`cloud-dev`) schema.

---

## 4 — The operator surface (config-primary, thin verb)

Per the standing config-primary doctrine: the whole N-way check is **one recipe in
`projection.json`, one command**.

```jsonc
{
  "model": {
    "env": "cloud-dev",                           // the schema source, named into the registry by NAME (live OSSYS, espace-safe)
    "modules": ["Sales", "Billing"]
  },
  "environments": {
    "cloud-dev": { "access": "direct", "conn": "file:./secrets/cloud-dev.conn", "rendition": "physical", "archetype": "managed-dml" },
    "cloud-qa":  { "access": "direct", "conn": "file:./secrets/cloud-qa.conn",  "rendition": "physical", "archetype": "managed-dml" },
    "cloud-uat": { "access": "direct", "conn": "file:./secrets/cloud-uat.conn", "rendition": "physical", "archetype": "managed-dml" }
  },
  "readiness": {
    // "schema" omitted ⇒ defaults to model.env (cloud-dev), the agreed shape. Name it only to override.
    "confirm": ["cloud-dev", "cloud-qa", "cloud-uat"]   // every env that must match it + carry clean data
  }
}
```

- **`model.env`** — the schema source as an environment *reference*: it points into the
  `environments` registry by name (like `flow.from` / `readiness.schema`), resolving to that
  env's live OSSYS connection — so the canonical environment (`cloud-dev`) is **named once**,
  not inlined as a duplicate connection. (`ossys:<conn-ref>` remains for a standalone source
  with no registry; the two are mutually exclusive — a named refusal if both are set.)
- **`readiness.schema`** — the environment whose OSSYS-read catalog is the *agreed shape* (the
  operator's "exported dev schema"). Read via `LiveModelRead` (native GUIDs). **Defaults to
  `model.env`** when omitted: the optional gate defers to the mandatory schema source, so the
  agreed shape and the emission source are the same canonical environment without restating it.
  Name `schema` explicitly only when the gate's reference must differ from the emission source.
- **`readiness.confirm`** — the environments to confirm. Each is OSSYS-read, diffed against the
  agreed shape (same model ⇒ **zero** delta, the espace coordinates invisible to the diff), and
  data-profiled for dealbreakers against that shape.

The thin verb (one new `check` sub-verb — the lightest surface; THE_CLI §8's closed family):

```
projection check shape                 # run the readiness block: schema-equivalence + data dealbreakers across the confirm set
projection check shape --format json   # the machine-read sibling (readiness.json)
```

> **Naming note.** `check ready` is **already taken** — it is the run-ledger readiness *gauge*
> (`CheckReady`, `MovementSurface.fs:1178`), a different question. This verb is `check shape`
> (does the estate resolve to one shape?). The `readiness` config block is the recipe; `check
> shape` is the act. (Operator may prefer another token — trivially re-bindable; the config
> block name is the load-bearing one.)

`check shape` resolves the block's env names through the existing `resolveLiveConn`
(`MovementSurface.fs:955`), exactly as `check data --before/--after` already resolves env names
to connections (`MovementSurface.fs:1174-1177`).

---

## 5 — The readiness report (THE_VOICE register)

Count-first, stative, agentless, the verdict on top, the proof beneath, the next move named at
the close (THE_VOICE; mirrors `Compare.render`). One block per confirmed environment, then the
estate verdict:

```
READINESS — the estate against cloud-dev's shape

  cloud-dev   Ready.   schema matches · 0 data dealbreakers
  cloud-qa    Ready.   schema matches · 0 data dealbreakers
  cloud-uat   Paused.  schema matches · 3 data dealbreakers
      FK orphans would land                         2   [Order→Customer 1 · OrderLine→Order 1]
      NOT NULL declared, NULLs would land           1   [Customer.Email 1]

  ESTATE — 2 of 3 ready. cloud-uat carries 3 data dealbreaker(s); resolve before cutover.
```

- **Schema equivalence** is the espace-invariant verdict: a clean env shows **zero** `CatalogDiff`
  delta, because the diff compares logical identity + structure only and the espace-varying
  physical names never enter it (§2). "schema matches" *is* the proof.
- **A non-zero `added`/`dropped`/`reshaped`** is a **real** logical divergence — that environment
  is not the agreed shape; it blocks, named.
- **Data dealbreakers** are the `ModelFidelity` violations of that env's data against the agreed
  schema — the operator's "understand all the data issues across all three."

`readiness.json` is the byte-deterministic sibling (per-env schema-delta channels + dealbreaker
categories), so the gate is machine-readable for a cutover pipeline.

---

## 6 — Total decisions, named refusals (no silent drop)

- **An env in `confirm` that cannot be OSSYS-read** (no live conn / not an OutSystems cell /
  connection refused) is a **named refusal**, never a silent skip — the readiness verdict is
  *unknown*, not *ready*. (Sibling of `GrantUnreadable`/NM-55.)
- **A GUID-instability surprise** — if an entity in `confirm` carries an `OssysOriginal` GUID
  absent from the agreed schema (or vice-versa) where logical names *do* match — is surfaced as
  a **named declared-vs-actual mismatch** (the espace-invariance precondition did not hold for
  that node), not folded into add/drop. This is the A1-amendment discipline applied across
  environments: an identity claim is *verified*, never trusted.
- **A profiling failure** degrades the *data* section to advisory-silent for that env (the
  schema verdict still leads) — exactly `Compare`'s existing posture (`RunFaces.fs:1459-1465`),
  never a hard abort.
- The whole verb is **read-only** (advisory) — no `--go`, no writes; it is a gate, not a move.

---

## 7 — Slice plan (each independently shippable + green; warm tests)

| Slice | Scope | Forcing guard |
|---|---|---|
| **S1** | The **`ossys:<conn-ref>`** operand: a `Ref`/`Source` variant reading via `LiveModelRead` (GUID identity), reusable by `compare`/`diff`. | **property:** an OSSYS-read catalog carries `OssysOriginal` SsKeys at **kind *and* attribute** grain (not `Synthesized`) — the operator's 2026-06-21 invariant, checked |
| **S2** | The **espace-invariance witness** — no production projection needed (`CatalogDiff` is already physical-agnostic on OSSYS-identity catalogs, §2). | **the espace-invariance property** (§2 witness) + its sensitivity counter-test → the `AxiomTests` entry |
| **S3** | The **N-way readiness aggregator**: per-env schema-equivalence (zero modulo espace) + per-env dealbreakers → one pure `ReadinessReport` (text + JSON). | unit + property: clean estate ⇒ "ready"; one injected NULL/orphan/dropped-attr ⇒ "paused"/"blocked", named |
| **S4** | The **config block** (`readiness`) + parse + the **`check shape`** verb + run face (`runCheckShape` + `Source.ofOssys` profiling) + the **realization-artifact normalization** (`toLogicalShape`, forced by the canary). | config-parse + verb-routing + A44 round-trip (`ReadinessConfigTests`) + the pure normalization test (`ReadinessTests`) + the **two-DB warm-Docker canary** (`OssysComprehensiveFixtureTests`: two espace-variant OSSYS DBs of one model ⇒ READY). |
| **S5** | Docs: this surface finalized + `AXIOMS.md`/`PRODUCT_AXIOMS.md` + `AxiomTests.fs` + `CONFIG_REFERENCE`/`THE_CLI`/`DECISIONS` — **and the worked `examples/projection.json` + README** (the originating task, now correctly grounded on env-to-env + this gate). | doc/consumer tests green; full `dotnet test` warm |

Regression guards every slice: `MovementSurfaceTests` (parse + `planCheck` totality),
`ConfigTests`, `CompareTests`/`CatalogDiffTests`, and the new espace-invariance property +
readiness canary.

---

## 8 — Disciplines held (break none without the DECISIONS amendment first)

- **Pure core.** The aggregator (`ReadinessReport` compute) is a pure function of resolved
  operands; the OSSYS read + connection opening live in the CLI run face (mirrors `Compare.fs`'s
  pure-core / I/O-one-layer-up split).
- **Espace-invariance is constructed, then proven** — `OssysOriginal` identity + a
  physical-name-agnostic `CatalogDiff` + the `toLogicalShape` realization-artifact normalization,
  witnessed by: the pure property test over the *real* `CatalogDiff` (`CatalogDiffTests`); the
  OSSYS-reader's both-grains `OssysOriginal` test (`OsmRowsetReaderTests`); the pure normalization
  test (`ReadinessTests`); and **the two-DB warm-Docker canary** (`OssysComprehensiveFixtureTests`)
  — two espace-variant OSSYS DBs of one model read as ONE shape.
- **The canary earned its keep.** It caught that the pure property (which left the realization
  facets empty) was an *incomplete* claim — `CatalogDiff` also compares the default-constraint
  name, triggers, and column checks, which OutSystems derives from the physical table name and so
  vary per espace. `toLogicalShape` (drop those; keep the logical shape + the default VALUE) is
  its fix, now green end-to-end (the two-DB seed is built by a GUID-preserving `OSUSR_` physical
  rewrite of the edge-case OSSYS seed — no synthesizer change needed after all).
- **Named refusals, no silent drop** (§6); **THE_VOICE** for the report (§5); **config-primary,
  thin CLI** (§4) — one recipe, one command.
- **A44 (expressible ⇔ reachable).** The `readiness` block is a new point in the config space;
  it resolves to exactly one read-only plan. Reuses `resolveLiveConn`; adds no parsed direction.
- **IR grows under evidence.** The `ossys:` operand is built at its *first* consumer (this gate),
  reusable by `compare`/`diff`; the `toLogicalShape` normalization was added under the **canary's
  evidence** (not speculatively) — the discipline of building at the forcing consumer.

---

## 9 — Cross-references

- `THE_CONFIG_CONTROL_PLANE.md` — A44; the unified config this block joins.
- `THE_DATA_PRODUCERS.md` §6 / `CatalogRendition.fs` — the reverse-leg's rendition projection: a
  *sibling* of this gate's `toLogicalShape`, NOT reused (that one renames table/column physical
  coordinates, which `CatalogDiff` already ignores; this one drops the realization-name artifacts
  `CatalogDiff` does compare).
- `AXIOMS.md` §A1 (four-variant `SsKey`, `OssysOriginal` honors A1 unconditionally) — the
  identity foundation.
- `THE_CLI.md` §8 — the `check` verb family; `Compare.fs` / `RunFaces.fs` `runCompare` — the
  read-only compare this generalizes from 2-operand-physical to N-operand-espace-safe.
- `CUTOVER_READINESS_BRIEF.md` — the V1→V2 flip readiness this complements (estate-shape vs
  flip-eligibility).

— Design opened 2026-06-21.

— **Follow-on 2026-06-22 (model-from-environment).** The schema source `model` gained an `env`
field: it names an environment in the registry (resolving to that env's live OSSYS conn-ref),
instead of inlining a connection that duplicated the environment's own `conn`. `readiness.schema`
now **defaults to `model.env`** when omitted, so the canonical environment is named once and the
optional readiness gate defers to the mandatory model source (dependency flows optional→mandatory,
never the reverse). Resolution lives in `ProjectionConfig.parse` (the only surface carrying the
registry); the pure `Config.ModelSection` is untouched. Named refusals: `env` + `ossys` both set,
an unknown environment, a non-direct (bundle/docker) one (`ReadinessConfigTests`). The worked
`examples/projection.sample.json` now uses `model.env` with a defaulted `readiness.schema`.
