# SCALAR_REPRESENTATION_AUDIT.md — how every scalar is collected, carried, and applied (V1 vs V2)

> **Prepared 2026-07-15**, companion to `SSDT_HANDOFF_REVIEW_PACKET.md` (register rows C1–C10,
> F11). Audience: the same SSDT dev leads. This is the scalar-by-scalar, hop-by-hop audit of how
> a value's *representation* is treated from source read to target write — the research project
> the datetime question opened up. It exists because "does the data plane coerce datetime to
> DATETIME2?" turned out to have a structural answer that applies to **every** scalar, not just
> datetime. §1–§8 compare V2's data plane against V1's static-seed literal path; **§9 adds V1's
> dynamic / all-entities path** and confirms the findings extend to it (reframing the fix with the
> CDC-silence tradeoff V2 bought). Register rows C4/C11 + WP-17 in the packet. Line references
> as-of `ef706ac`; code and `DECISIONS.md` win on any disagreement.

---

## 1 — The structural root: two type vocabularies, and only one reaches the data plane

V2 carries **two** type concepts, deliberately (see the docstring at
`src/Projection.Core/SqlStorageType.fs:11-38`):

- **`PrimitiveType`** — the semantic OutSystems category. Exactly **9 variants**: `Integer`,
  `Decimal`, `Text`, `Boolean`, `DateTime`, `Date`, `Time`, `Binary`, `Guid`
  (`src/Projection.Core/PrimitiveType.fs:11-20`).
- **`SqlStorageType`** — the concrete SQL Server realization. **28 variants** including `BigInt`,
  `SmallInt`, `TinyInt`, `Money`, `SmallMoney`, `Float`, `Real`, `NChar`, `Char`, `NText`,
  `Text`, `DateTime`, `DateTime2`, `DateTimeOffset`, `SmallDateTime`, `Image`, `Xml`
  (`src/Projection.Core/SqlStorageType.fs:40-68`).

**The DDL plane can see both.** A column emits from its `SqlStorage` evidence when present
(`ScriptDomBuild.dataTypeReferenceFromStorage`, `ScriptDomBuild.fs:239-277`) and falls back to
the `PrimitiveType` mapping when absent (`ScriptDomBuild.dataTypeReference`,
`ScriptDomBuild.fs:151-192`). Two lanes.

**The DATA plane can see only `PrimitiveType`.** The unit of transported data is a `CellValue`
carrying a `PrimitiveType` + a raw `string` — never a `SqlStorageType` (the reader keys every
hop off `cv.Type : PrimitiveType`, `Bulk.fs:78-79`, `clrType : PrimitiveType -> Type`
`Bulk.fs:27`). So **before any value can become data, its 28-way concrete type is collapsed to
one of the 9 semantic categories** by `SqlStorageType.toPrimitiveType`
(`SqlStorageType.fs:79-108`). That collapse is lossless for the DDL (the storage evidence still
types the column and the `#temp`) but **the transported value now knows only its coarse
category** — and that is where representation is lost. §4 is the collapse table.

**The single-source codec.** All three data-plane string conversions converge on one module,
`RawValueCodec` (`src/Projection.Core/RawValueCodec.fs:1-28`), with three consumers: the SQL
literal emitter (`SqlLiteral`), the bulk parser (`Bulk.parseRaw`), and the readback formatter
(`ReadSide.formatRawValue`). Its contract is a round-trip law: `parse (format v) = v` for every
`PrimitiveType`, modulo culture-invariance, canonical hex prefix, and canonical boolean spelling
(`RawValueCodec.fs:16-28`). The law is real and strong — **for the 9 categories**. It says
nothing about the 19 concrete types that collapsed to reach them.

**V1, by contrast, has no raw-string codec.** V1 carries native CLR objects end-to-end and
formats each by its *runtime* type at the last moment (`SqlLiteralFormatter.FormatValue`, a type
switch over `string/bool/int/long/decimal/double/float/DateTime/DateTimeOffset/TimeSpan/Guid/
byte[]/…`, `src/Osm.Emission/Formatting/SqlLiteralFormatter.cs:9-41`). This one architectural
difference is the source of most of the divergences in §6: V1 never collapses, so it never loses
`double` precision, a `DateTimeOffset` zone, or a control character.

---

## 2 — The hops

Each scalar's journey, and the file that owns each hop:

| Hop | V2 owner | V1 owner |
|---|---|---|
| **1. Collect** (SQL → CLR → raw string) | `ReadSide.formatRawValue` (`ReadSide.fs:611-672`) / hydration | `StaticEntityDataProviders` (CLR objects, no raw string) |
| **2. Encode** (canonical raw form) | `RawValueCodec` (`RawValueCodec.fs`) | — (n/a; objects retained) |
| **3a. DDL type** (column + `#temp`) | `dataTypeReferenceFromStorage` (storage) / `dataTypeReference` (fallback) | `TypeMappingPolicy` (on-disk-first) |
| **3b. Literal render** (seed MERGE/INSERT) | `SqlLiteral.ofRaw`→`toString` (`SqlLiteral.fs:75-119`) + `ScriptDomBuild.buildSqlLiteral` (`:290-328`) | `SqlLiteralFormatter.FormatValue` (`:9-41`) |
| **4. Bulk write** (raw → CLR → SqlBulkCopy) | `Bulk.parseRaw` (`Bulk.fs:51-73`) | — (rendered SQL only) |
| **5. Compare** (CDC change-detect / round-trip digest) | MERGE predicate + tolerances | unconditional MERGE |

The staged path is the one place the *data* hop re-acquires storage evidence: the `#temp` mirror
is typed from the target attribute's `SqlStorage` (`StagedMerge.stagingColumnDefsOf`,
`StagedMerge.fs:46-51` — `SqlStorage = a.SqlStorage`, all columns nullable), so the raw string is
converted to the true column type on `INSERT INTO #temp` and the MERGE compares like-typed. The
inline (sub-threshold) path has no `#temp`; the bare literal is reconciled against the target
column by SQL Server's type precedence at MERGE time.

---

## 3 — Master catalog: the 9 data-plane categories

For each `PrimitiveType`: how it is carried and rendered on every hop, V2 vs V1.

| PrimitiveType | V2 CLR carrier (`Bulk.clrType`) | V2 raw format (`RawValueCodec`) | V2 seed literal (`SqlLiteral.toString`) | V1 seed literal (`SqlLiteralFormatter`) | V2 bulk parse (`Bulk.parseRaw`) | Round-trip |
|---|---|---|---|---|---|---|
| **Integer** | `int64` (`Bulk.fs:29`) | `Int64.ToString(inv)` (`ReadSide.fs:625`) | bare digits (`IntegerLit`) | `int/long/short/byte.ToString(inv)` (`:21-28`) | `Int64.Parse(inv)` (`:57`) | faithful |
| **Decimal** | `decimal` (`:30`) | `Decimal.ToString(inv)` (`ReadSide.fs:656`) | bare digits (`DecimalLit`) | `decimal.ToString(inv)` (`:29`) | `Decimal.Parse(inv)` (`:59`) | faithful **within decimal**; see §4 Float/Real |
| **Boolean** | `bool` (`:31`) | `"true"`/`"false"` (`:71-74`) | `1`/`0` (`:103-104`) | `1`/`0` (`:20`) | `parseBoolean`, **fails loud** on garbage (NM-20, `:109-120`) | faithful; V2 stricter than V1 |
| **DateTime** | `DateTime` (`:32`) | `"yyyy-MM-dd HH:mm:ss.fffffff"` — **7 digits** (`:43`) | bare `'<raw>'` (`TemporalLit`, `:105`) | **`CAST('…fffffff' AS datetime2(7))`** (`:90`) | `ParseExact(DateTimeFormat)` (`:63`) | faithful value; **literal form + DDL lane diverge — §5** |
| **Date** | `DateTime` (`:33`) | `"yyyy-MM-dd"` (`:47`) | bare `'<raw>'` | **`CAST('…' AS date)`** (`:84`) | `ParseExact(DateFormat)` (`:65`) | faithful; V1 casts |
| **Time** | `TimeSpan` (`:34`) | `"c"` (`:52`) | bare `'<raw>'` | **`CAST('…fffffff' AS time(7))`** (`:87`) | `TimeSpan.Parse` (`:67`) | faithful; V1 casts |
| **Guid** | `Guid` (`:35`) | `"D"` lower hyphenated (`:58`) | `'<raw>'` (`GuidLit`) | `'{g:D}'` (`:37`) | `Guid.Parse` (`:69`) | faithful |
| **Text** | `string` (`:36`) | verbatim string | `N'<''-doubled>'` (`:107-111`) | `N'<''-doubled + CR/LF/TAB→CHAR()>'` (`:18,53-64`) | `raw` (`:71`) | faithful value; **control-char form diverges — §6.4**; `''`→NULL — §6.5 |
| **Binary** | `byte[]` (`:37`) | `0x`+hex (`withHexPrefix`, `:144`) | `0x…` bare (`BinaryLit`) | `0x`+`X2` uppercase (`:66-81`) | `FromHexString(strip 0x)` (`:73`) | faithful (hex-case-insensitive) |

Readback defensive-hardening worth knowing (all in `ReadSide.fs`): `Time` accepts a driver that
surfaces `time` as `DateTime` (`:638-645`); `Guid` accepts `SqlGuid` (`:646-654`); `Binary`
accepts `SqlBytes`/`SqlBinary` (`:661-672`). These guard the *expected* categories — they do not
add cases for the collapsed-away concrete types (§4).

---

## 4 — The collapse table: 28 concrete types → 9 categories (where representation is lost)

`SqlStorageType.toPrimitiveType` (`SqlStorageType.fs:79-108`) is exhaustive and total, so nothing
crashes — but the data carriage after collapse is only as rich as the target category. The DDL
column type stays faithful (storage lane); the **transported value** rides the coarse carrier.

| Concrete `SqlStorageType` | DDL emitted (storage lane) | Collapses to (data) | Data-carriage consequence | Verdict |
|---|---|---|---|---|
| `BigInt` / `Int` / `SmallInt` / `TinyInt` | faithful | **Integer** → `int64` | all fit in int64; `TinyInt` unsigned 0-255 and `SmallInt` fit | **faithful** |
| `Decimal(p,s)` / `Numeric(p,s)` | faithful | **Decimal** → `decimal` | decimal carries it exactly | **faithful** |
| `Money` / `SmallMoney` | faithful | **Decimal** → `decimal` | money = decimal(19,4)/(10,4); decimal holds it | **faithful** (scale display only) |
| **`Float`** (53-bit ≈17 sig digits, ±1.79E308) | `FLOAT` faithful | **Decimal** → `decimal` | ★ `Convert.ToDecimal(double)` keeps ~15 sig digits and **overflows** above ≈7.9E28; then `Decimal.Parse`→decimal→bulk→implicit reconvert to `FLOAT` loses again. V1 carried native `double` at **G17** (`:30`) | ★ **lossy / overflow** |
| **`Real`** (24-bit ≈9 sig digits) | `REAL` faithful | **Decimal** → `decimal` | decimal holds the value, but the semantic is now "decimal string" not IEEE-754; V1 used **G9** (`:31`) | **lossy-adjacent** (semantic shift) |
| `NVarChar` / `VarChar` / `NChar` / `Char` | faithful | **Text** → `string` | value round-trips; `Char`/`NChar` trailing-blank **padding** preserved in storage (comparison covered by `CharAnsiPaddingTolerated`) | **faithful** (padding = compare tolerance) |
| `NText` / `Text` (legacy LOB) | faithful | **Text** → `string` | round-trips as string; deprecated types | **faithful** |
| **`Xml`** | `XML` (`XmlDataTypeReference`) | **Text** → `string` | ★ read as markup string, bulk-written back into `xml` → SQL Server **re-serializes** (whitespace/attribute-order/decl normalized) — not byte-faithful; `''`→NULL nukes empty xml; and a CDC-aware MERGE builds `T.[c] <> S.[c]`, but **`xml` has no `<>` operator** → predicate compile error on a CDC-enabled kind | ★ **lossy + latent MERGE error** |
| `DateTime` | `DATETIME` (`:218`) | **DateTime** → `DateTime` | 7-digit raw → `DATETIME` rounds to 1/300s; source was 1/300s ⇒ exact | **faithful** (see §5 for the lane split) |
| `DateTime2(s)` | `DATETIME2(s)` | **DateTime** → `DateTime` | 7-digit raw preserves ticks | **faithful** |
| **`DateTimeOffset(s)`** | `DATETIMEOFFSET(s)` faithful | **DateTime** → `DateTime` | ★ offset **dropped** on collapse; readback `Convert.ToDateTime(DateTimeOffset)` **throws** (`DateTimeOffset` isn't `IConvertible`→DateTime, `ReadSide.fs:628-629`). V1 kept it: `CAST('…K' AS datetimeoffset(7))` (`:93`) | ★ **broken / zone-lost** |
| `SmallDateTime` (minute precision) | `SMALLDATETIME` faithful | **DateTime** → `DateTime` | 7-digit raw is finer than the column; round-trips | **faithful** |
| `Date` | `DATE` | **Date** | faithful | **faithful** |
| `Time(s)` | `TIME(s)` | **Time** → `TimeSpan` | faithful | **faithful** |
| `VarBinary` / `Binary` | faithful | **Binary** → `byte[]` | hex round-trip | **faithful** |
| **`Image`** (legacy LOB ≤2GB) | `IMAGE` | **Binary** → `byte[]` | hex round-trip works but doubles memory (hex string = 2× bytes) on the estate-scale path | **faithful** (memory cost) |
| `Bit` | `BIT` | **Boolean** | faithful | **faithful** |
| `UniqueIdentifier` | `UNIQUEIDENTIFIER` | **Guid** | faithful | **faithful** |

**The four that are not faithful — `Float`, `Real`, `DateTimeOffset`, `Xml` — share one trait:
OutSystems has no native attribute type that produces them.** They enter only through
DBA-authored columns or **External Entities** — precisely the boundary the exporter's own
cutover mission targets (`VISION.md`: the External-Entities swap). So they are rare in a pure
native estate and real at exactly the edge the product exists to serve. That is the case for
treating them, not dismissing them.

---

## 5 — The datetime deep-dive (the question that opened this audit)

Three findings, which together **correct and complete packet row C4**:

**(a) The DDL type for `DateTime` is lane-dependent.**
- Storage-evidence lane (live OSSYS, where `SqlStorage = Some DateTime`):
  `storageDataTypeOption … DateTime -> DateTime` (`ScriptDomBuild.fs:218`) → **`DATETIME`** (legacy).
- `PrimitiveType`-fallback lane (no storage evidence — catalog-direct goldens, ReadSide-derived
  catalogs, JSON without `SqlStorage`): `sqlDataTypeOption … DateTime -> DateTime2`
  (`ScriptDomBuild.fs:129`), mirrored by `SqlStorageType.ofPrimitiveType … DateTime ->
  DateTime2 None` (`SqlStorageType.fs:128`) → **`DATETIME2`**.

So the same logical `DateTime` attribute emits **`DATETIME` on a live-OSSYS export and
`DATETIME2` on the fallback/golden/ReadSide path.** The golden corpus is authored with
`SqlStorage = None`, so **the goldens show `DATETIME2`** — which is why the "does it coerce to
DATETIME2?" instinct was correct: the coercion is real and lives in the fallback DDL lane.

**(b) The DATA literal is always the 7-digit form, and V1 wraps it in an explicit CAST that V2
dropped.** V2 renders `TemporalLit` as a bare non-national string `'2020-01-01 12:34:56.7890000'`
(`ScriptDomBuild.buildSqlLiteral`, `:306-310`; `SqlLiteral.toString`, `:105`) — 7 fractional
digits regardless of the column's type. V1 renders **`CAST('2020-01-01 12:34:56.7890000' AS
datetime2(7))`** (`SqlLiteralFormatter.cs:90`). The V1 CAST is (i) precision-explicit and (ii)
language-independent — `datetime2` parses ISO strings the same under any `SET DATEFORMAT`/
`LANGUAGE`, whereas V2's bare string, when reconciled against a legacy `DATETIME` column, relies
on SQL Server's implicit conversion (the ` `-separated, no-`T` form is a boundary case for
`datetime` parsing). **So V1 *does* pivot to datetime2 — in the data plane, on every seeded
datetime — via CAST**, contradicting the packet's earlier "V1 does not pivot to DATETIME2"
(which was true only of V1's *DDL* type-mapping config).

**(c) Round-trip is nonetheless preserved, because the `#temp`/column reconciles the type.**
The staged `#temp` mirror is typed from `a.SqlStorage` (`StagedMerge.fs:51`), so a `DATETIME`
target gets a `DATETIME` `#temp`, the 7-digit raw converts (rounding to 1/300s) on INSERT, and
the MERGE compares `DATETIME = DATETIME` — no CDC misfire. A legacy-datetime source value only
ever had 1/300s precision, so the round-trip is exact. **No silent datetime *data* loss.** The
divergences are (a) the DDL *type* (DATETIME vs DATETIME2) and (b) the literal *form* (bare vs
CAST) — both representational, both worth blessing.

**Corrected C4 recommendation:** keep the target type at **database reality** (`DATETIME` on the
storage-evidence lane), but (1) **fix the fallback lane** so a storage-evidence-less catalog does
not silently upgrade `DateTime → DATETIME2` — it should carry the same legacy `DATETIME` default
(align `sqlDataTypeOption`/`ofPrimitiveType` to the storage lane, or refuse to emit datetime
without evidence); and (2) **adopt V1's explicit `CAST(... AS datetime2(7))` seed-literal form**
(and `AS date` / `AS time(7)` / `AS datetimeoffset(7)`) for language-independence and
precision-explicitness. The post-cutover `DATETIME2(3)` modernization stands as before. This is
folded into **WP-17** (§10 of the packet).

---

## 6 — The V1 ↔ V2 divergence ledger (the audit's punchlines)

Ranked by data-fidelity stakes:

1. **`Float` / `Real` precision + overflow** (§4). V1: native `double` at G17, `float` at G9,
   carried as CLR objects — full round-trip. V2: collapsed to the `Decimal` carrier — ~15-digit
   truncation and `OverflowException` above ≈7.9E28. ★ regression. *(External/DBA columns only.)*
2. **`DateTimeOffset` zone dropped, readback throws** (§4). V1: `CAST(... AS datetimeoffset(7))`
   with the `K` offset. V2: collapsed to `DateTime`; `Convert.ToDateTime` on a boxed
   `DateTimeOffset` raises. ★ regression. *(External/DBA columns only.)*
3. **Datetime/date/time literal form** (§5). V1: explicit `CAST(... AS datetime2(7)/date/
   time(7))`. V2: bare quoted string, implicit conversion. Robustness + precision-explicitness
   delta; also the DDL-lane split (DATETIME vs DATETIME2).
4. **Text control characters.** V1 escapes CR/LF/TAB into `' + CHAR(13) + N'…'` concatenation
   (`SqlLiteralFormatter.cs:53-64`) — the emitted SQL contains no raw control bytes. V2 embeds the
   literal CR/LF/TAB inside `N'…'` (`SqlLiteral.toString:107-111` does single-quote doubling only).
   Legal SQL, but the seed script now carries raw control characters — affects diff review,
   byte-determinism, and any downstream that assumes single-line literals.
5. **Empty string → NULL** (packet F11 / WP-3). V1 preserves `N''`; V2's `raw = ""` is the
   *universal* NULL sentinel for every category (`SqlLiteral.ofRaw:81`, `Bulk.parseRaw:52`). ★
   regression; also nukes empty `xml`/zero-length `binary`.
6. **Boolean garbage.** V2 improvement: `parseBoolean` fails loud with a named code
   (`RawValueCodec.fs:109-120`, NM-20) where a silent coercion would hide a real BIT divergence.
   V1 carried native `bool`, so the question didn't arise. Keep V2's behavior.
7. **Value-carriage model (the root).** V1 threads native CLR objects and formats by runtime type,
   so it never has to know a column's semantic category to render its value faithfully. V2's
   raw-string IR is keyed on the 9-way `PrimitiveType`, so it must pre-collapse — which is why
   1–4 exist. This is a deliberate V2 design (single codec, three consumers, provable round-trip
   *for the 9*), not an oversight; the gap is that the collapse has no named-erasure accounting
   for the concrete types it flattens, unlike everywhere else in the engine.

---

## 7 — Hazards, ranked, with plan mapping

| # | Hazard | Where | Reaches production when | Plan |
|---|---|---|---|---|
| S1 | `Float`/`Real` precision loss + overflow | `SqlStorageType.toPrimitiveType:89-90` → `Decimal` carrier | any `float`/`real` column (external/DBA) in a data lane | **✅ WP-17(a) LANDED (2026-07-16)** — G17/G9 raws, shape-driven parse; Docker gallery witness |
| S2 | `DateTimeOffset` zone dropped; readback throws | `toPrimitiveType:101` → `DateTime`; `ReadSide.fs:628-629` | any `datetimeoffset` column read back / transferred | **✅ WP-17(b) LANDED (2026-07-16)** — offset-bearing raw + `DateTimeOffsetLit` CAST; throw retired; Docker gallery witness |
| S3 | Fallback DDL lane upgrades `DateTime → DATETIME2`; seed literal bare (no CAST) | `ScriptDomBuild.fs:129`, `:306-310` | storage-evidence-less catalog (goldens, ReadSide, JSON-no-storage) | **✅ WP-17(d) LANDED (2026-07-16)** — fallback = legacy `DATETIME` (3 mirror sites); literals = explicit CAST via the category-bearing `DateTimeLit`/`DateLit`/`TimeLit` split |
| S4 | Text control chars embedded raw in `N'…'` | `SqlLiteral.toString:107-111` | any seed value with CR/LF/TAB | **✅ WP-17(e) LANDED (2026-07-16)** — `CHAR()` splice via the shared `textLiteralSegments`, both planes |
| S5 | `Xml` re-serialization + empty-xml erase + CDC `<>` compile error | `toPrimitiveType:98` → `Text`; CDC predicate | any `xml` column, esp. on a CDC-enabled kind | **✅ WP-17(c) LANDED (2026-07-16)** — per-type cast-compare guard; the gallery canary widened the class to `image`/`text`/`ntext` (same `<>` refusal); content carriage named; erase was WP-3 |
| S6 | `''` → NULL universal erasure | `SqlLiteral.ofRaw:81`, `Bulk.parseRaw:52` | every empty-string Text value | **✅ WP-3 LANDED (2026-07-16)** — option-grain cells; `''` survives; an empty raw on a non-empty-capable type refuses loudly |

---

## 8 — Test-witness map

| Category / concrete type | Strongest witness | Status |
|---|---|---|
| Integer / Decimal / Boolean / Guid / Binary | `RawValueCodec` round-trip property + golden seed rows (`Tier`, `Country`) | **witnessed** |
| DateTime / Date / Time | golden seed rows + CDC-silence Docker canary | **witnessed** (legacy-datetime rounding not explicitly asserted) |
| Text incl. `''`, control chars | `''` preserved end-to-end (WP-3: the `TextFidelity` golden + the F11 Docker canary); control-char CHAR()-splice round-trips in the gallery canary (WP-17(e)/(f)) | **witnessed** |
| `Money` / `SmallMoney` | `ScalarCarriageRoundTripTests` (WP-17(f) Docker gallery) | **witnessed** (`SmallMoney` rides the same Decimal path) |
| `Float` / `Real` | `ScalarCarriageRoundTripTests` — `Double.MaxValue` through FLOAT; G9 through REAL | **witnessed** (WP-17(a)) |
| `DateTimeOffset` | `ScalarCarriageRoundTripTests` — `-03:00` verbatim via CONVERT style 121 | **witnessed** (WP-17(b)) |
| `Xml` | `ScalarCarriageRoundTripTests` — content probes + the CDC-armed MERGE compiles and re-runs | **witnessed** (WP-17(c)) |
| `NText` / `Image` (legacy LOB) | `Image` in the gallery canary (hex round-trip + the `<>`-refusal guard the canary itself discovered); `NText` rides the same cast-compare class | **witnessed** (Image) / guard-covered (NText) |
| `SmallDateTime` | `ScalarCarriageRoundTripTests` — minute-rounding compare | **witnessed** |

The unwitnessed rows are the WP-17 fixture backlog: every concrete type that collapses to a
category needs at least one round-trip fixture proving the collapse is faithful or the refusal is
loud. Until then, §4's verdicts are code-derived, not test-proven.

---

## 9 — V1's dynamic / "bulk" data path (the other half of the V1 comparison)

**Correction first: V1 has no bulk-copy path at all.** There is no `SqlBulkCopy` / `WriteToServer`
/ `DataTable` / `bcp` anywhere in V1 `src/` (grep-confirmed; the only `IDataReader`/`DataTable`
hits are metadata-extraction test doubles). V1's dynamic / all-entities data path is **rendered
T-SQL literals through the same `SqlLiteralFormatter` as the static seeds**, carrying **native CLR
objects** end-to-end. So the V1↔V2 fidelity gap in §4/§6 is not "V1 bulk-copy vs V2 codec" — it is
**native-object carriage (V1) vs the raw-string 9-variant collapse (V2)**. V1 never widens or
narrows a type because it formats each cell by its *runtime* CLR type at the last moment
(`SqlLiteralFormatter.FormatValue` switch, `SqlLiteralFormatter.cs:16-40`), driven by whatever
`reader.GetValue` returned — never by the model's declared category.

**Read hop = the seed machinery.** `SqlDynamicEntityDataProvider.ExtractTableAsync` pages the
source with `OFFSET/FETCH` (batch 1000, `SqlDynamicEntityDataProvider.cs:593-597,36`) and reads
each cell `reader.IsDBNull(i) ? null : reader.GetValue(i)` → `NormalizeValue` (`:636-637`) into
`StaticEntityRow` — byte-identical to the static provider (`StaticEntityDataProviders.cs:289-290`).
Same `object?[]` carriage, same single-space `" "`→NULL sentinel on nullable columns only, `''`
preserved.

**Write hop = rendered literals, two generators:**
- `DynamicEntityInsertGenerator` → `INSERT … WITH (TABLOCK) VALUES …` (batch 1000, `IDENTITY_INSERT`
  bracket) — **INSERT-only, not idempotent** — and **deprecated at orchestration**:
  `BuildSsdtDynamicInsertStep` is a logged no-op returning empty (`BuildSsdtDynamicInsertStep.cs:33-40`).
  Still DI-registered and what the integration test drives directly.
- `PhasedDynamicEntityInsertGenerator` → `MERGE … WHEN NOT MATCHED THEN INSERT` via a `VALUES` CTE,
  with the two-phase NULL→UPDATE dance for nullable FK cycles (`:231-240,293-297`) — the **live**
  path, reached because the extracted dynamic dataset flows into `BuildSsdtBootstrapSnapshotStep`
  (below) rather than the deprecated INSERT step.

**Per-scalar: V1-dynamic ≡ V1-seed for every type** (same formatter, same object carriage), so the
"V1 seed literal" column of §3 and the V1 verdicts of §4 apply to V1's dynamic path unchanged. The
confirmation the audit was after, on the four types V2 collapses:

| V2-lossy type | V1 (both paths, native object → literal) | V2 data plane |
|---|---|---|
| **float** (8-byte) | `double` → `G17`, round-trip-exact (`SqlLiteralFormatter.cs:30`) | `Float → Decimal`: ~15-digit + overflow ★ |
| **real** (4-byte) | `float` → `G9`, round-trip-exact (`:31`) | `Real → Decimal`: semantic shift ★ |
| **datetimeoffset** | `DateTimeOffset` → `CAST('…K' AS datetimeoffset(7))`, **offset kept** (`:92-93`; test-witnessed `-03:00`) | `DateTimeOffset → DateTime`: offset dropped, readback throws ★ |
| **money/smallmoney** | `decimal` → InvariantCulture (`:29`) | `Money → Decimal` (safe, but via the collapse) |
| **xml** | `string` content kept, `N'…'` (implicit nvarchar→xml on insert; no `CAST(… AS xml)`) | `Xml → Text`: content kept, type collapsed, CDC `<>` hazard |

So **V1's dynamic path is strictly higher-fidelity than V2's data plane on float / real /
datetimeoffset (and typed-xml), and never worse on any scalar** — the headline is confirmed, and
it holds for the *live* V1 path (phased MERGE), not just the deprecated INSERT one.

**The balancing nuance — V2 bought something with the collapse.** V1 has **no CDC-awareness
anywhere** (no rowversion / watermark / changed-since predicate); its live idempotency is MERGE-key
`WHEN NOT MATCHED THEN INSERT` — **insert-if-absent, never change-detecting** (a matched row is
never updated). V2's data plane is the change-detecting, CDC-silent idempotent MERGE (packet F2).
The two pipelines optimized different properties: **V1 kept concrete-type fidelity (native objects)
but not CDC-silence; V2 bought CDC-silence + a single provable round-trip codec and pays for it
with the 9-variant collapse.** This reframes the fix (WP-17): *not* "revert to V1's rendered-literal
object carriage" — that would forfeit CDC-silence — but **add faithful carriers (or named refusals)
for the four collapsing types while keeping the raw-string / CDC-silent design.** Fidelity *and*
silence, not one at the other's cost.

**`AllEntitiesIncludingStatic` = V2's Bootstrap.** `BuildSsdtBootstrapSnapshotStep` concatenates
static-seed + dynamic/regular + supplemental (`ossys_User`) rows, globally FK-sorts them, and
writes `Bootstrap/AllEntitiesIncludingStatic.bootstrap.sql` via the phased MERGE
(`BuildSsdtBootstrapSnapshotStep.cs:50-65,170-178`). Per-scalar it is identical to the seed path —
every row goes through the same `SqlLiteralFormatter` — so the difference from a plain seed is
orchestration (global topo order, cycle phasing, one-shot post-deploy guard), not scalar handling.

**Test-witness (V1 dynamic path), and the shared blind spot.** The read hop is integration-tested
against a real DB but asserts only non-empty (`SqlDynamicEntityDataProviderIntegrationTests`);
write literals are witnessed for **`int`/`nvarchar`/`NULL` only**
(`DynamicEntityInsertGeneratorTests`, `PhasedDynamicEntityInsertGeneratorTests`). The shared
`SqlLiteralFormatterTests` witnesses `NULL`/`N''`/binary/`date`/`time(7)`/`datetime2(7)`/
**`datetimeoffset(7)` with `-03:00`** — but **`float`/`real`/`money`/`xml`/`decimal`/`Guid` are
untested on every V1 path** (the G17/G9/decimal branches exist in code, unexercised). So V1's
fidelity edge on exactly the contested types rests on code inspection + documented SqlClient
`GetValue` mappings, not passing tests — the **same unwitnessed set as V2** (§8). **Neither
pipeline has a test proving `float`/`real`/`datetimeoffset`/`xml` round-trip** — that shared gap is
the strongest argument for WP-17's fixture backlog. (Read-type nuance: SqlClient returns `DateTime`
for a `date` column and `TimeSpan` for `time`, so V1's SQL-read path lands in the `datetime2(7)`/
`TimeSpan` literal branches, not the `DateOnly`/`TimeOnly` `date`/`time(7)` branches — those fire
only for the JSON fixture provider; value-preserving via implicit conversion, only the declared
cast type differs.)

---

*Companion to `SSDT_HANDOFF_REVIEW_PACKET.md`. Source anchors: `src/Projection.Core/{PrimitiveType,
SqlLiteral,SqlStorageType,RawValueCodec}.fs`; `src/Projection.Targets.SSDT/ScriptDomBuild.fs`;
`src/Projection.Pipeline/Bulk.fs`; `src/Projection.Adapters.Sql/ReadSide.fs`;
`src/Projection.Targets.Data/StagedMerge.fs`; V1 seed + dynamic paths
`src/Osm.Emission/Formatting/SqlLiteralFormatter.cs`, `src/Osm.Pipeline/StaticData/`
(`SqlDynamicEntityDataProvider`, `StaticEntityDataProviders`), `src/Osm.Emission/`
(`DynamicEntityInsertGenerator`, `PhasedDynamicEntityInsertGenerator`, `BuildSsdtBootstrapSnapshotStep`).*
