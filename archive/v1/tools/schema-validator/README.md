# OutSystems Model JSON Schema Validator

This utility provides a portable Zod schema for the OutSystems 11 model export and a small CLI that highlights contract gaps in emitted JSON. It mirrors the invariants enforced by the .NET domain layer (module/entity/attribute uniqueness, identifier requirements, flag normalization, etc.) so you can validate payloads without building the full solution.

## Prerequisites

* Node.js 18 or later (needed for the ESM-based tooling and `tsx` runner).
* `npm` (bundled with Node.js).

## Installation

```bash
cd tools/schema-validator
npm install
```

This installs the lightweight dependencies (`zod` and `tsx`).

## Validating a model export

Run the CLI and provide the path to your OutSystems model JSON file:

```bash
npm run validate -- ../../tests/Fixtures/model.edge-case.json
```

The command will parse the JSON, enforce the schema, and print a summary. If the payload passes, you will see output similar to:

```
✅ Model JSON matches the OutSystems 11 contract.
Export timestamp: <not provided>
Modules: 1
  1. AppCore (system: no, active: yes, entities: 5)
     • Customer / OSUSR_ABC_CUSTOMER — attributes: 12, indexes: 3, relationships: 2
     • ...
```

Add `--print-normalized` (or `-p`) to emit the normalized projection the validator produced. This is helpful when you need to inspect the sanitized booleans, trimmed identifiers, or defaulted delete rules:

```bash
npm run validate -- my-export.json --print-normalized
```

## Interpreting validation errors

When the schema check fails, the CLI surfaces each issue with a JSON pointer-like path and a descriptive message. For example:

```
✖ Schema validation failed with 2 issues:
  1. modules[0].entities[2].attributes[4].name: Duplicate attribute logical name "Email" in entity "Customer" (first at index 1).
  2. modules[0].entities[2].attributes: Entity "Customer" must include at least one attribute marked as identifier.
```

* `modules[0].entities[2].attributes[4].name` pinpoints the failing field (module index 0, entity index 2, attribute index 4, `name` property).
* The message reuses the same business rules as the C# domain layer, so you can update your JSON to match the expected contract.

Fix the highlighted fields and re-run the command until the validator reports success.

## What the schema enforces

The Zod schema (`src/modelSchema.ts`) mirrors the key OutSystems DDL exporter rules:

* Module names must be unique (case-insensitive) and each module must contain at least one entity.
* Entity logical/physical names must be unique within a module, have at least one attribute, and include at least one primary key (`isIdentifier: true`).
* Attribute logical/physical names must be unique within an entity, numeric metadata (`length`, `precision`, `scale`) must be non-negative integers, and references require target entity metadata.
* Indexes must contain at least one column with unique ordinals; composite indexes preserve ordering.
* Relationship delete rules default to `"Ignore"` when absent and `hasDbConstraint` is normalized to a boolean.
* Flags encoded as `0`/`1` are normalized to booleans, optional strings are trimmed, and absent optional values become `null` for easier diffing.

By running the CLI against your exported JSON, you can quickly spot mismatches before handing the payload to the .NET pipeline.

## Project structure

```
src/
  modelSchema.ts      # Zod schema + helpers (formatting, summary)
  validate-model.ts   # CLI entry point
package.json          # Scripts (npm run validate) and dependencies
README.md             # This guide
```

Feel free to copy `modelSchema.ts` into other tooling or pipelines; it is framework-agnostic and only relies on `zod`.

