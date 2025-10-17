# Metadata Contract Overrides & Attribute JSON Nullability

OutSystems metadata exports normally guarantee that every row in the `AttributeJson` result set includes a JSON payload in the
`AttributesJson` column. When the Advanced SQL extractor encounters a module that omits the JSON (for example, when a freshly
created attribute has no metadata yet), the exporter treats the NULL as a contract violation and raises the following error:

```
osm.pipeline.SqlExtraction.MetadataRowMappingException: Failed to map row 76 in result set 'AttributeJson'. Column 'AttributesJson' (ordinal 1) expected type 'System.String' but provider type was 'System.String'. Root cause: Column 'AttributesJson' (ordinal 1) contained NULL but a non-null value was required. Column snapshot preview: <NULL>.
```

Even though the provider type matches the expected CLR type (`System.String`), the pipeline fails because the strict contract
requires a non-null JSON payload. The mismatch is not a SQL client bug: the reader forces the column to be non-null and emits a
`ColumnReadException` when the database returns `NULL`. The error message now surfaces the inner failure (`contained NULL`)
so the reason is clear and includes a column preview for instant context.【F:src/Osm.Pipeline/SqlExtraction/MetadataRowMappingException.cs†L27-L72】

To improve triage further, the metadata reader logs the offending row as structured JSON and highlights the column value (or `<NULL>`) directly inside the error payload. You can copy the raw values into issue trackers or overrides without re-running the export.【F:src/Osm.Pipeline/SqlExtraction/SqlClientOutsystemsMetadataReader.cs†L247-L262】【F:src/Osm.Pipeline/SqlExtraction/MetadataRowSnapshot.cs†L1-L103】

When you confirm that `AttributesJson` should be optional for your environment, register a metadata contract override so the
reader treats the column as nullable instead of aborting the extraction.

## Declaring optional columns

1. Copy `config/appsettings.example.json` to a location you control (for example `config/appsettings.json`).
2. Populate the `sql.metadataContract.optionalColumns` section with the result set name and column(s) that should be nullable.

```jsonc
{
  "sql": {
    "connectionString": "Server=localhost;Database=OutSystems;Trusted_Connection=True;",
    "metadataContract": {
      "optionalColumns": {
        "AttributeJson": ["AttributesJson"]
      }
    }
  }
}
```

3. Point the CLI at the configuration file:

```bash
dotnet run --project src/Osm.Cli -- build-ssdt --config config/appsettings.json --out ./out
```

The loader resolves the override into a `MetadataContractOverrides` instance, which the SQL metadata reader honors at runtime.
When the `AttributesJson` column returns `NULL`, the pipeline now accepts the row and logs the override for traceability.【F:src/Osm.Pipeline/Application/SqlOptionsResolver.cs†L30-L88】【F:src/Osm.Pipeline/SqlExtraction/SqlClientOutsystemsMetadataReader.cs†L30-L83】【F:src/Osm.Pipeline/SqlExtraction/SqlClientOutsystemsMetadataReader.cs†L144-L170】

## Verifying the override

* The metadata reader writes an information-level log describing the optional column that has been activated. Enable `--verbose`
  (or set `Logging:LogLevel:Osm.Pipeline.SqlExtraction.SqlClientOutsystemsMetadataReader=Debug` when hosting in ASP.NET Core) to
  see entries like:

  ```
  Metadata contract override active for result set AttributeJson. Optional columns: AttributesJson.
  ```

* Downstream unit tests exercise the same pathway by injecting `MetadataContractOverrides.Strict.WithOptionalColumn("AttributeJson", "AttributesJson")`,
  ensuring that null payloads are accepted when explicitly allowed.【F:tests/Osm.Pipeline.Tests/SqlClientOutsystemsMetadataReaderTests.cs†L210-L245】

If additional result sets gain optional columns in the future, extend the configuration with more entries. The reader merges
all overrides, so you can mark multiple columns or result sets as nullable in a single deployment-specific configuration file.
