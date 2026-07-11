# formal/ — the rung-4/rung-5 verification lane

> Owned by `FORMAL_METHODS.md` (the ladder + the rung ledger). This directory holds the
> machine-checked artifacts; the ledger holds the claims. A file here that the ledger does
> not cite — or a ledger citation with no file — fails `scripts/model-check.sh`.

## Run it

```sh
scripts/model-check.sh          # everything: Alloy specs + Dafny proofs + citation gate
scripts/model-check.sh alloy    # rung-4 bounded model checks only
scripts/model-check.sh dafny    # rung-5 proofs only
```

Tools fetch on demand into `formal/.tools/` (gitignored, sha256/version-pinned): Alloy 6.2
from Maven Central (needs Java 11+), Dafny 4.11 as a dotnet tool, Z3 from the host or
`pip install z3-solver`.

## Reading the results

- **Alloy `check … expect 0`** — a law. UNSAT = no counterexample within the command's
  scope = the law holds, bounded. SAT = the law BROKE; the solver's instance is the
  minimal counterexample trace (written next to the receipt).
- **Alloy `run … expect 1`** — a sanity/reachability witness. SAT = the model is
  inhabited / the path exists. UNSAT = the model went vacuous (over-constrained) — treated
  as a failure so no law can pass by accident of an empty model.
- **The Part VI witnesses in `Catalog.als` are deliberately `expect 1`**: they are
  machine-checked proof that the audit's illegal-but-representable states are real today.
  When a `Catalog.create` hardening closes one, its witness goes UNSAT, the runner fails,
  and the closing commit flips the expectation + moves the invariant into the
  `TodaysInvariants` fact — the spec is the live ledger of open quadrants.
- **Dafny** — every lemma is proven for ALL inputs (no bound). The module headers state
  the binding contract: the proofs own the algebra; the existing FsCheck suites own the
  correspondence between the F# functions and the verified model.

## Bounds (the honest fine print)

Alloy checks are **bounded model checking**: exhaustive over every instance and trace
within the per-command scopes (`for N but 1..K steps`), not beyond them. Scopes are chosen
so every guard, gate, and counter in the machine is exercisable (e.g. the cutover ladder's
consecutive-green counter uses model constant N=3 for the policy value N=10 — the checked
law is the structure: consecutiveness, single-route promotion, gate composition). TLA⁺/TLC
is the named upgrade path for unbounded temporal checking; see `FORMAL_METHODS.md` §4 for
why it is not in this environment and what would trigger the swap.
