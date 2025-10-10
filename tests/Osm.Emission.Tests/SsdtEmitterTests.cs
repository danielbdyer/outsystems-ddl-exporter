using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.SqlServer.Management.Smo;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using Osm.Domain.Configuration;
using Osm.Domain.ValueObjects;
using Osm.Emission;
using Osm.Smo;
using Osm.Validation.Tightening;
using Tests.Support;

namespace Osm.Emission.Tests;

public class SsdtEmitterTests
{
    [Fact]
    public async Task EmitAsync_writes_per_table_artifacts_and_manifest()
    {
        var model = ModelFixtures.LoadModel("model.edge-case.json");
        var snapshot = ProfileFixtures.LoadSnapshot(FixtureProfileSource.EdgeCase);
        var options = TighteningOptions.Default;
        var policy = new TighteningPolicy();
        var decisions = policy.Decide(model, snapshot, options);
        var report = PolicyDecisionReporter.Create(decisions);
        var smoOptions = SmoBuildOptions.FromEmission(options.Emission);
        var factory = new SmoModelFactory();
        var smoModel = factory.Create(
            model,
            decisions,
            profile: snapshot,
            options: smoOptions);

        using var temp = new TempDirectory();
        var emitter = new SsdtEmitter();
        var manifest = await emitter.EmitAsync(smoModel, temp.Path, smoOptions, report);

        Assert.Equal(4, manifest.Tables.Count);
        Assert.Equal(options.Emission.IncludePlatformAutoIndexes, manifest.Options.IncludePlatformAutoIndexes);
        Assert.False(manifest.Options.EmitBareTableOnly);
        Assert.NotNull(manifest.PolicySummary);
        Assert.Equal(report.TightenedColumnCount, manifest.PolicySummary!.TightenedColumnCount);
        Assert.Equal(report.UniqueIndexCount, manifest.PolicySummary!.UniqueIndexCount);
        Assert.Equal(report.UniqueIndexesEnforcedCount, manifest.PolicySummary!.UniqueIndexesEnforcedCount);

        var customerTable = manifest.Tables.Single(t => t.Table.Equals("Customer", StringComparison.Ordinal));
        var customerPath = Path.Combine(temp.Path, customerTable.TableFile);
        Assert.True(File.Exists(customerPath));
        var customerScript = await File.ReadAllTextAsync(customerPath);
        Assert.Contains("CREATE TABLE [dbo].[Customer]", customerScript);
        Assert.Contains("CONSTRAINT [PK_Customer]", customerScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CONSTRAINT [FK_Customer_CityId]", customerScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(Environment.NewLine + "            PRIMARY KEY CLUSTERED,", customerScript);
        Assert.Contains("FOREIGN KEY ([CityId]) REFERENCES [dbo].[City] ([Id])", customerScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ON DELETE NO ACTION", customerScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ON UPDATE NO ACTION", customerScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DEFAULT ('')", customerScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CREATE UNIQUE INDEX [IDX_Customer_Email]", customerScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("EXEC sys.sp_addextendedproperty", customerScript, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("LegacyCode", customerScript, StringComparison.Ordinal);

        Assert.Contains(customerTable.Indexes, name => name.Equals("IDX_Customer_Email", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(customerTable.Indexes, name => name.Equals("IDX_Customer_Name", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(customerTable.ForeignKeys, name => name.Equals("FK_Customer_CityId", StringComparison.OrdinalIgnoreCase));
        Assert.True(customerTable.IncludesExtendedProperties);

        foreach (var entry in manifest.Tables)
        {
            var tablePath = Path.Combine(temp.Path, entry.TableFile);
            Assert.True(File.Exists(tablePath));
            var script = await File.ReadAllTextAsync(tablePath);
            if (entry.Indexes.Count == 0)
            {
                Assert.DoesNotContain("CREATE INDEX", script, StringComparison.OrdinalIgnoreCase);
            }
        }

        var manifestJsonPath = Path.Combine(temp.Path, "manifest.json");
        Assert.True(File.Exists(manifestJsonPath));
        var manifestJson = await File.ReadAllTextAsync(manifestJsonPath);
        Assert.Contains("Customer", manifestJson);
    }

    [Fact]
    public void FormatCreateTableScript_places_default_on_indented_line()
    {
        var method = typeof(SsdtEmitter)
            .GetMethod("FormatCreateTableScript", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var statement = new CreateTableStatement
        {
            Definition = new TableDefinition()
        };
        statement.Definition!.ColumnDefinitions.Add(new ColumnDefinition());

        var script = string.Join(Environment.NewLine, new[]
        {
            "CREATE TABLE [dbo].[Sample] (",
            "    [Flag] BIT NOT NULL DEFAULT ((0))",
            ")"
        });

        var formatted = (string)method!.Invoke(null, new object[] { script, statement })!;

        var expected = string.Join(Environment.NewLine, new[]
        {
            "CREATE TABLE [dbo].[Sample] (",
            "    [Flag] BIT NOT NULL",
            "        DEFAULT ((0))",
            ")"
        });

        Assert.Equal(expected, formatted);
    }

    [Fact]
    public void FormatCreateTableScript_indents_named_default_constraint_with_trailing_comma()
    {
        var method = typeof(SsdtEmitter)
            .GetMethod("FormatCreateTableScript", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var statement = new CreateTableStatement
        {
            Definition = new TableDefinition()
        };
        statement.Definition!.ColumnDefinitions.Add(new ColumnDefinition());
        statement.Definition.ColumnDefinitions.Add(new ColumnDefinition());

        var script = string.Join(Environment.NewLine, new[]
        {
            "CREATE TABLE [dbo].[Sample] (",
            "    [Id] INT NOT NULL,",
            "    [CreatedOn] DATETIME2 NOT NULL CONSTRAINT DF_Sample_CreatedOn DEFAULT (SYSUTCDATETIME()),",
            ")"
        });

        var formatted = (string)method!.Invoke(null, new object[] { script, statement })!;

        var expected = string.Join(Environment.NewLine, new[]
        {
            "CREATE TABLE [dbo].[Sample] (",
            "    [Id] INT NOT NULL,",
            "    [CreatedOn] DATETIME2 NOT NULL",
            "        CONSTRAINT DF_Sample_CreatedOn DEFAULT (SYSUTCDATETIME()),",
            ")"
        });

        Assert.Equal(expected, formatted);
    }

    [Fact]
    public void FormatCreateTableScript_does_not_modify_table_constraints()
    {
        var method = typeof(SsdtEmitter)
            .GetMethod("FormatCreateTableScript", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var statement = new CreateTableStatement
        {
            Definition = new TableDefinition()
        };
        statement.Definition!.ColumnDefinitions.Add(new ColumnDefinition());

        var script = string.Join(Environment.NewLine, new[]
        {
            "CREATE TABLE [dbo].[Sample] (",
            "    [Value] INT NOT NULL,",
            "    CONSTRAINT CK_Sample_Value CHECK ([Value] > 0)",
            ")"
        });

        var formatted = (string)method!.Invoke(null, new object[] { script, statement })!;

        Assert.Equal(script, formatted);
    }

    [Fact]
    public void FormatForeignKeyConstraints_moves_trailing_comma_after_on_clauses()
    {
        var method = typeof(SsdtEmitter)
            .GetMethod("FormatForeignKeyConstraints", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var script = string.Join(Environment.NewLine, new[]
        {
            "CREATE TABLE [dbo].[Sample] (",
            "    [Id] INT NOT NULL,",
            "    CONSTRAINT FK_Sample_Primary FOREIGN KEY ([PrimaryId]) REFERENCES [dbo].[Primary] ([Id]) ON DELETE CASCADE,",
            "    CONSTRAINT FK_Sample_Secondary FOREIGN KEY ([SecondaryId]) REFERENCES [dbo].[Secondary] ([Id])",
            ")"
        });

        var formatted = (string)method!.Invoke(null, new object[] { script })!;

        var expected = string.Join(Environment.NewLine, new[]
        {
            "CREATE TABLE [dbo].[Sample] (",
            "    [Id] INT NOT NULL,",
            "    CONSTRAINT FK_Sample_Primary",
            "        FOREIGN KEY ([PrimaryId]) REFERENCES [dbo].[Primary] ([Id])",
            "            ON DELETE CASCADE,",
            "    CONSTRAINT FK_Sample_Secondary",
            "        FOREIGN KEY ([SecondaryId]) REFERENCES [dbo].[Secondary] ([Id])",
            ")"
        });

        Assert.Equal(expected, formatted);
    }

    [Fact]
    public async Task EmitAsync_skips_platform_auto_indexes_when_option_disabled()
    {
        var model = ModelFixtures.LoadModel("model.edge-case.json");
        var snapshot = ProfileFixtures.LoadSnapshot(FixtureProfileSource.EdgeCase);
        var options = TighteningOptions.Default;
        var policy = new TighteningPolicy();
        var decisions = policy.Decide(model, snapshot, options);
        var smoOptions = SmoBuildOptions.FromEmission(options.Emission);
        var smoModel = new SmoModelFactory().Create(
            model,
            decisions,
            profile: snapshot,
            options: smoOptions);

        using var temp = new TempDirectory();
        var emitter = new SsdtEmitter();
        var manifest = await emitter.EmitAsync(smoModel, temp.Path, smoOptions, null);

        var jobRun = manifest.Tables.Single(t => t.Table.Equals("JobRun", StringComparison.Ordinal));
        Assert.DoesNotContain(jobRun.Indexes, name => name.Contains("OSIDX", StringComparison.OrdinalIgnoreCase));

        var jobRunScript = await File.ReadAllTextAsync(Path.Combine(temp.Path, jobRun.TableFile));
        Assert.DoesNotContain("OSIDX", jobRunScript, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EmitAsync_includes_platform_auto_indexes_when_enabled()
    {
        var model = ModelFixtures.LoadModel("model.edge-case.json");
        var snapshot = ProfileFixtures.LoadSnapshot(FixtureProfileSource.EdgeCase);
        var options = CreateOptionsWithPlatformIndexesEnabled();
        var policy = new TighteningPolicy();
        var decisions = policy.Decide(model, snapshot, options);
        var smoOptions = SmoBuildOptions.FromEmission(options.Emission);
        var smoModel = new SmoModelFactory().Create(
            model,
            decisions,
            profile: snapshot,
            options: smoOptions);

        using var temp = new TempDirectory();
        var emitter = new SsdtEmitter();
        var manifest = await emitter.EmitAsync(smoModel, temp.Path, smoOptions, null);

        var jobRun = manifest.Tables.Single(t => t.Table.Equals("JobRun", StringComparison.Ordinal));
        Assert.Contains(jobRun.Indexes, name => name.Equals("OSIDX_JobRun_CreatedOn", StringComparison.OrdinalIgnoreCase));

        var script = await File.ReadAllTextAsync(Path.Combine(temp.Path, jobRun.TableFile));
        Assert.Contains("CREATE INDEX [OSIDX_JobRun_CreatedOn]", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EmitAsync_emitBareTableOnly_suppresses_inline_extras()
    {
        var model = ModelFixtures.LoadModel("model.edge-case.json");
        var snapshot = ProfileFixtures.LoadSnapshot(FixtureProfileSource.EdgeCase);
        var defaults = TighteningOptions.Default;
        var bareEmission = EmissionOptions.Create(
            defaults.Emission.PerTableFiles,
            defaults.Emission.IncludePlatformAutoIndexes,
            defaults.Emission.SanitizeModuleNames,
            emitBareTableOnly: true,
            defaults.Emission.NamingOverrides).Value;
        var bareOptions = TighteningOptions.Create(
            defaults.Policy,
            defaults.ForeignKeys,
            defaults.Uniqueness,
            defaults.Remediation,
            bareEmission,
            defaults.Mocking).Value;
        var policy = new TighteningPolicy();
        var decisions = policy.Decide(model, snapshot, bareOptions);
        var smoOptions = SmoBuildOptions.FromEmission(bareOptions.Emission);
        var factory = new SmoModelFactory();
        var smoModel = factory.Create(
            model,
            decisions,
            profile: snapshot,
            options: smoOptions);

        using var temp = new TempDirectory();
        var emitter = new SsdtEmitter();
        var manifest = await emitter.EmitAsync(smoModel, temp.Path, smoOptions, null);

        Assert.True(manifest.Options.EmitBareTableOnly);
        var customerEntry = manifest.Tables.Single(t => t.Table.Equals("Customer", StringComparison.Ordinal));
        Assert.Empty(customerEntry.Indexes);
        Assert.Empty(customerEntry.ForeignKeys);
        Assert.False(customerEntry.IncludesExtendedProperties);

        var script = await File.ReadAllTextAsync(Path.Combine(temp.Path, customerEntry.TableFile));
        Assert.Contains("CREATE TABLE [dbo].[Customer]", script);
        Assert.DoesNotContain("DEFAULT", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("FOREIGN KEY", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CREATE INDEX", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sp_addextendedproperty", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EmitAsync_honors_unique_index_policy_flags()
    {
        var model = ModelFixtures.LoadModel("model.micro-unique.json");
        var policy = new TighteningPolicy();
        var defaults = TighteningOptions.Default;
        var smoOptions = SmoBuildOptions.FromEmission(defaults.Emission);
        var factory = new SmoModelFactory();
        var emitter = new SsdtEmitter();

        using var temp = new TempDirectory();

        var cleanSnapshot = ProfileFixtures.LoadSnapshot(FixtureProfileSource.MicroUnique);
        var cleanDecisions = policy.Decide(model, cleanSnapshot, defaults);
        var cleanOut = Path.Combine(temp.Path, "clean");
        Directory.CreateDirectory(cleanOut);
        var cleanModel = factory.Create(
            model,
            cleanDecisions,
            profile: cleanSnapshot,
            options: smoOptions);
        var cleanManifest = await emitter.EmitAsync(cleanModel, cleanOut, smoOptions, null);
        var cleanEntry = cleanManifest.Tables.Single(t => t.Table.Equals("User", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(cleanEntry.Indexes, name => name.Equals("UX_User_Email", StringComparison.OrdinalIgnoreCase));
        var cleanScript = await File.ReadAllTextAsync(Path.Combine(cleanOut, cleanEntry.TableFile));
        Assert.Contains("CREATE UNIQUE INDEX [UX_User_Email]", cleanScript, StringComparison.OrdinalIgnoreCase);

        var duplicateSnapshot = ProfileFixtures.LoadSnapshot(FixtureProfileSource.MicroUniqueWithDuplicates);
        var duplicateDecisions = policy.Decide(model, duplicateSnapshot, defaults);
        var duplicateOut = Path.Combine(temp.Path, "duplicates");
        Directory.CreateDirectory(duplicateOut);
        var duplicateModel = factory.Create(
            model,
            duplicateDecisions,
            profile: duplicateSnapshot,
            options: smoOptions);
        var duplicateManifest = await emitter.EmitAsync(duplicateModel, duplicateOut, smoOptions, null);
        var duplicateEntry = duplicateManifest.Tables.Single(t => t.Table.Equals("User", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(duplicateEntry.Indexes, name => name.Equals("UX_User_Email", StringComparison.OrdinalIgnoreCase));
        var duplicateScript = await File.ReadAllTextAsync(Path.Combine(duplicateOut, duplicateEntry.TableFile));
        Assert.Contains("CREATE INDEX [UX_User_Email]", duplicateScript, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CREATE UNIQUE INDEX [UX_User_Email]", duplicateScript, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EmitAsync_applies_table_override_across_artifacts()
    {
        var model = ModelFixtures.LoadModel("model.edge-case.json");
        var snapshot = ProfileFixtures.LoadSnapshot(FixtureProfileSource.EdgeCase);
        var options = TighteningOptions.Default;
        var policy = new TighteningPolicy();
        var decisions = policy.Decide(model, snapshot, options);
        var report = PolicyDecisionReporter.Create(decisions);

        var overrideResult = NamingOverrideRule.Create("dbo", "OSUSR_ABC_CUSTOMER", null, null, "CUSTOMER_PORTAL");
        Assert.True(overrideResult.IsSuccess);
        var overrides = NamingOverrideOptions.Create(new[] { overrideResult.Value });
        Assert.True(overrides.IsSuccess);

        var smoOptions = SmoBuildOptions.FromEmission(options.Emission).WithNamingOverrides(overrides.Value);
        var smoModel = new SmoModelFactory().Create(
            model,
            decisions,
            profile: snapshot,
            options: smoOptions);

        using var temp = new TempDirectory();
        var emitter = new SsdtEmitter();
        var manifest = await emitter.EmitAsync(smoModel, temp.Path, smoOptions, report);

        var renamedEntry = manifest.Tables.FirstOrDefault(t => t.Table.Equals("CUSTOMER_PORTAL", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(renamedEntry);
        Assert.Equal($"dbo.CUSTOMER_PORTAL.sql", Path.GetFileName(renamedEntry!.TableFile));

        var tablePath = Path.Combine(temp.Path, renamedEntry.TableFile);
        var tableScript = await File.ReadAllTextAsync(tablePath);
        Assert.Contains("CREATE TABLE [dbo].[CUSTOMER_PORTAL]", tableScript, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("OSUSR_ABC_CUSTOMER", tableScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PK_CUSTOMER_PORTAL", tableScript, StringComparison.OrdinalIgnoreCase);

        Assert.All(renamedEntry.Indexes, name => Assert.Contains("CUSTOMER_PORTAL", name, StringComparison.OrdinalIgnoreCase));
        Assert.All(renamedEntry.ForeignKeys, name => Assert.Contains("CUSTOMER_PORTAL", name, StringComparison.OrdinalIgnoreCase));

        var allSqlFiles = Directory.GetFiles(temp.Path, "*.sql", SearchOption.AllDirectories);
        Assert.Contains(allSqlFiles, path =>
        {
            var content = File.ReadAllText(path);
            return content.Contains("CUSTOMER_PORTAL", StringComparison.OrdinalIgnoreCase);
        });

        foreach (var sqlPath in allSqlFiles)
        {
            var content = File.ReadAllText(sqlPath);
            Assert.DoesNotContain("OSUSR_ABC_CUSTOMER", content, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task EmitAsync_applies_entity_override_across_artifacts()
    {
        var model = ModelFixtures.LoadModel("model.edge-case.json");
        var snapshot = ProfileFixtures.LoadSnapshot(FixtureProfileSource.EdgeCase);
        var options = TighteningOptions.Default;
        var policy = new TighteningPolicy();
        var decisions = policy.Decide(model, snapshot, options);
        var report = PolicyDecisionReporter.Create(decisions);

        var overrideResult = NamingOverrideRule.Create(null, null, null, "Customer", "CUSTOMER_EXTERNAL");
        Assert.True(overrideResult.IsSuccess);
        var overrides = NamingOverrideOptions.Create(new[] { overrideResult.Value });
        Assert.True(overrides.IsSuccess);

        var smoOptions = SmoBuildOptions.FromEmission(options.Emission).WithNamingOverrides(overrides.Value);
        var smoModel = new SmoModelFactory().Create(
            model,
            decisions,
            profile: snapshot,
            options: smoOptions);

        using var temp = new TempDirectory();
        var emitter = new SsdtEmitter();
        var manifest = await emitter.EmitAsync(smoModel, temp.Path, smoOptions, report);

        var renamedEntry = manifest.Tables.FirstOrDefault(t => t.Table.Equals("CUSTOMER_EXTERNAL", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(renamedEntry);
        Assert.Equal($"dbo.CUSTOMER_EXTERNAL.sql", Path.GetFileName(renamedEntry!.TableFile));

        var tablePath = Path.Combine(temp.Path, renamedEntry.TableFile);
        var tableScript = await File.ReadAllTextAsync(tablePath);
        Assert.Contains("CREATE TABLE [dbo].[CUSTOMER_EXTERNAL]", tableScript, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("OSUSR_ABC_CUSTOMER", tableScript, StringComparison.OrdinalIgnoreCase);

        Assert.All(renamedEntry.Indexes, name => Assert.Contains("CUSTOMER_EXTERNAL", name, StringComparison.OrdinalIgnoreCase));
        Assert.All(renamedEntry.ForeignKeys, name => Assert.Contains("CUSTOMER_EXTERNAL", name, StringComparison.OrdinalIgnoreCase));

        var allSqlFiles = Directory.GetFiles(temp.Path, "*.sql", SearchOption.AllDirectories);
        foreach (var sqlPath in allSqlFiles)
        {
            var content = File.ReadAllText(sqlPath);
            Assert.DoesNotContain("OSUSR_ABC_CUSTOMER", content, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task EmitAsync_applies_module_scoped_entity_override_with_sanitized_module_name()
    {
        var model = ModelFixtures.LoadModel("model.edge-case.json");
        var module = model.Modules.First(m => string.Equals(m.Name.Value, "AppCore", StringComparison.OrdinalIgnoreCase));

        var renamedModuleName = ModuleName.Create("App Core");
        Assert.True(renamedModuleName.IsSuccess);

        var renamedEntities = module.Entities
            .Select(e => e with { Module = renamedModuleName.Value })
            .ToImmutableArray();
        var mutatedModule = module with { Name = renamedModuleName.Value, Entities = renamedEntities };
        var mutatedModel = model with { Modules = model.Modules.Replace(module, mutatedModule) };

        var snapshot = ProfileFixtures.LoadSnapshot(FixtureProfileSource.EdgeCase);
        var options = TighteningOptions.Default;
        var policy = new TighteningPolicy();
        var decisions = policy.Decide(mutatedModel, snapshot, options);
        var report = PolicyDecisionReporter.Create(decisions);

        var overrideResult = NamingOverrideRule.Create(null, null, "App Core", "Customer", "CUSTOMER_EXTERNAL");
        Assert.True(overrideResult.IsSuccess);
        var overrides = NamingOverrideOptions.Create(new[] { overrideResult.Value });
        Assert.True(overrides.IsSuccess);

        var smoOptions = SmoBuildOptions.FromEmission(options.Emission).WithNamingOverrides(overrides.Value);
        var smoModel = new SmoModelFactory().Create(
            mutatedModel,
            decisions,
            profile: snapshot,
            options: smoOptions);

        using var temp = new TempDirectory();
        var emitter = new SsdtEmitter();
        var manifest = await emitter.EmitAsync(smoModel, temp.Path, smoOptions, report);

        var renamedEntry = manifest.Tables.FirstOrDefault(
            t => t.Table.Equals("CUSTOMER_EXTERNAL", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(renamedEntry);
        Assert.Contains("Modules/App_Core", renamedEntry!.TableFile, StringComparison.OrdinalIgnoreCase);

        var tablePath = Path.Combine(temp.Path, renamedEntry.TableFile);
        var tableScript = await File.ReadAllTextAsync(tablePath);
        Assert.Contains("CREATE TABLE [dbo].[CUSTOMER_EXTERNAL]", tableScript, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("OSUSR_ABC_CUSTOMER", tableScript, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EmitAsync_applies_module_scoped_override_without_affecting_other_entities()
    {
        var categoryColumns = ImmutableArray.Create(
            new SmoColumnDefinition("Id", "Id", DataType.BigInt, Nullable: false, IsIdentity: true, IdentitySeed: 1, IdentityIncrement: 1, IsComputed: false, ComputedExpression: null, DefaultExpression: null, Collation: null, Description: null));
        var categoryIndexes = ImmutableArray.Create(
            new SmoIndexDefinition("PK_Category", IsUnique: true, IsPrimaryKey: true, IsPlatformAuto: false, ImmutableArray.Create(new SmoIndexColumnDefinition("Id", 1, IsIncluded: false, IsDescending: false))));
        var categoryForeignKeys = ImmutableArray<SmoForeignKeyDefinition>.Empty;

        var inventoryCategory = new SmoTableDefinition(
            "Inventory",
            "Inventory",
            "OSUSR_INV_CATEGORY",
            "dbo",
            "OutSystems",
            "Category",
            Description: null,
            categoryColumns,
            categoryIndexes,
            categoryForeignKeys);

        var catalogCategory = new SmoTableDefinition(
            "Catalog",
            "Catalog",
            "OSUSR_CAT_CATEGORY",
            "dbo",
            "OutSystems",
            "Category",
            Description: null,
            categoryColumns,
            categoryIndexes,
            categoryForeignKeys);

        var productColumns = ImmutableArray.Create(
            new SmoColumnDefinition("Id", "Id", DataType.BigInt, Nullable: false, IsIdentity: true, IdentitySeed: 1, IdentityIncrement: 1, IsComputed: false, ComputedExpression: null, DefaultExpression: null, Collation: null, Description: null),
            new SmoColumnDefinition("CategoryId", "CategoryId", DataType.BigInt, Nullable: false, IsIdentity: false, IdentitySeed: 0, IdentityIncrement: 0, IsComputed: false, ComputedExpression: null, DefaultExpression: null, Collation: null, Description: null));
        var productIndexes = ImmutableArray.Create(
            new SmoIndexDefinition("PK_Product", IsUnique: true, IsPrimaryKey: true, IsPlatformAuto: false, ImmutableArray.Create(new SmoIndexColumnDefinition("Id", 1, IsIncluded: false, IsDescending: false))));
        var productForeignKeys = ImmutableArray.Create(
            new SmoForeignKeyDefinition(
                "FK_Product_CategoryId",
                "CategoryId",
                "Inventory",
                "OSUSR_INV_CATEGORY",
                "dbo",
                "Id",
                "Category",
                ForeignKeyAction.NoAction));

        var productTable = new SmoTableDefinition(
            "Catalog",
            "Catalog",
            "OSUSR_CAT_PRODUCT",
            "dbo",
            "OutSystems",
            "Product",
            Description: null,
            productColumns,
            productIndexes,
            productForeignKeys);

        var smoModel = SmoModel.Create(ImmutableArray.Create(inventoryCategory, catalogCategory, productTable));

        var overrideResult = NamingOverrideRule.Create(null, null, "Inventory", "Category", "CATEGORY_STATIC");
        Assert.True(overrideResult.IsSuccess);
        var overrides = NamingOverrideOptions.Create(new[] { overrideResult.Value });
        Assert.True(overrides.IsSuccess);

        var options = new SmoBuildOptions(
            "OutSystems",
            IncludePlatformAutoIndexes: false,
            EmitBareTableOnly: false,
            SanitizeModuleNames: true,
            NamingOverrides: overrides.Value);

        using var temp = new TempDirectory();
        var emitter = new SsdtEmitter();
        var manifest = await emitter.EmitAsync(smoModel, temp.Path, options);

        var renamedTable = Directory.GetFiles(temp.Path, "dbo.CATEGORY_STATIC.sql", SearchOption.AllDirectories).Single();
        var otherTable = Directory.GetFiles(temp.Path, "dbo.Category.sql", SearchOption.AllDirectories).Single();

        var renamedScript = await File.ReadAllTextAsync(renamedTable);
        Assert.Contains("CATEGORY_STATIC", renamedScript, StringComparison.OrdinalIgnoreCase);

        var productEntry = manifest.Tables.First(t =>
            t.Module.Equals("Catalog", StringComparison.OrdinalIgnoreCase) &&
            t.TableFile.Contains("Product", StringComparison.OrdinalIgnoreCase));
        var productTablePath = Path.Combine(temp.Path, productEntry.TableFile);
        var productScript = await File.ReadAllTextAsync(productTablePath);
        Assert.Contains("REFERENCES [dbo].[CATEGORY_STATIC]", productScript, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("OSUSR_INV_CATEGORY", productScript, StringComparison.OrdinalIgnoreCase);

        var otherScript = await File.ReadAllTextAsync(otherTable);
        Assert.Contains("CREATE TABLE [dbo].[Category]", otherScript, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName());
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }

    private static TighteningOptions CreateOptionsWithPlatformIndexesEnabled()
    {
        var defaults = TighteningOptions.Default;
        var emissionResult = EmissionOptions.Create(
            defaults.Emission.PerTableFiles,
            includePlatformAutoIndexes: true,
            defaults.Emission.SanitizeModuleNames,
            defaults.Emission.EmitBareTableOnly,
            defaults.Emission.NamingOverrides);

        Assert.True(emissionResult.IsSuccess);

        var optionsResult = TighteningOptions.Create(
            defaults.Policy,
            defaults.ForeignKeys,
            defaults.Uniqueness,
            defaults.Remediation,
            emissionResult.Value,
            defaults.Mocking);

        Assert.True(optionsResult.IsSuccess);
        return optionsResult.Value;
    }
}
