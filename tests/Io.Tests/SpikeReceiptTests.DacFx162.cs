using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DbChange.Tests;
using Xunit;

namespace DbChange.Io.Tests;

/// <summary>
/// Spikes S9, S10 and S11 on DacFx 162.5.57, the release the pipeline is presumed to run until spike S7 names it, through the child process
/// tests/DacFx162/ builds (<see cref="DacFx162Child"/>): its schema reads, extract and constraints, its script with the deploy report beside
/// it, and its publish. Each receipt is the same as on 170.5.96 in this class's other half; the catalog is read in this process, since what
/// sys.foreign_keys and sys.check_constraints hold does not depend on the DacFx that published it.
/// </summary>
public sealed partial class SpikeReceiptTests
{
    [Fact]
    [Trait("Category", "fixture")]
    public async Task Spike_S9_on_DacFx_162_an_extract_keeps_both_states_and_its_publish_reproduces_them()
    {
        await using var environment = await HoldingAnUntrustedKeyAndADisabledCheck();
        var reader = await ReadOnlyPrincipal.CreateAsync(environment);
        using var scratch = ScratchFolder.Temporary("spike-s9-162");
        var (extracted, head) = (scratch.Under("environment.dacpac"), await Head());

        var read = DacFx162Child.Succeeded(["constraints"], reader.ConnectionString).Output;
        DacFx162Child.Succeeded(["extract", environment.Name, extracted], reader.ConnectionString);
        await using var fresh = await SqlServerFixture.RegisterAsync();
        DacFx162Child.Succeeded(["publish", GoldenProject.Profile, fresh.Name, extracted], fresh.ConnectionString);
        var (script, report) = Planned162(scratch, environment.Name, reader.ConnectionString, ("head", head))[0];

        Assert.Contains("CheckConstraint [dbo].[" + Check + "] Disabled=True", read, StringComparison.Ordinal);
        Assert.Contains("ForeignKeyConstraint [dbo].[" + Key + "] Disabled=False", read, StringComparison.Ordinal);
        Assert.Contains("ForeignKeyConstraint [dbo].[" + CategoryKey + "] Disabled=True", read, StringComparison.Ordinal);
        KeepsADisabledCheckAndAnUntrustedKey(extracted);
        Assert.Equal((3, 1, 3), await Trusts(fresh));
        Assert.Equal(DisabledCheckAndUndeclaredKey, Operations(report).Order());
        Assert.DoesNotContain(Key, script, StringComparison.Ordinal);
        DacFx162Child.Succeeded(["publish", GoldenProject.Profile, environment.Name, head], environment.ConnectionString);
        Assert.Equal((0, 1, Absent), await Trusts(environment));
    }

    [Fact]
    [Trait("Category", "fixture")]
    public async Task Spike_S9_on_DacFx_162_a_release_that_leaves_a_declared_key_unchanged_also_adds_it_again_where_it_is_disabled()
    {
        var (last, head) = (await GoldenProject.Built(DeclaresTheCategoryKey),
            await GoldenProject.Built(DeclaresTheCategoryKey, GoldenProject.Change("add-a-nullable-column")));
        await using var clean = await WithTheCategoryKeyDisabled(last, orphaned: false);
        await using var orphaned = await WithTheCategoryKeyDisabled(last, orphaned: true);
        using var scratch = ScratchFolder.Temporary("spike-s9-162-enabled");

        var (script, report) = Planned162(scratch, clean.Name, clean.ConnectionString, ("head", head))[0];
        DacFx162Child.Succeeded(["publish", GoldenProject.Profile, clean.Name, head], clean.ConnectionString);
        var failed = DacFx162Child.Run(["publish", GoldenProject.Profile, orphaned.Name, head], orphaned.ConnectionString);

        Assert.Equal(TheCategoryKeyAddedAgain, Operations(report).Order());
        ReEnablesAfterTheSeed(script);
        Assert.Equal((1, 0), (await Nicknamed(clean), await Trust(clean.ConnectionString, CategoryKey)));
        Assert.True(failed.Code == 1 && failed.Errors.Contains("Msg 547", StringComparison.Ordinal), failed.Output + failed.Errors);
        Assert.Equal((1, 1), (await Nicknamed(orphaned), await Trust(orphaned.ConnectionString, CategoryKey)));
    }

    [Fact]
    [Trait("Category", "fixture")]
    public async Task Spike_S10_on_DacFx_162_constraint_validation_also_runs_after_the_post_deploy_seed()
    {
        var (basePackage, head) = (await GoldenProject.Built(),
            await GoldenProject.Built(GoldenProject.Change("add-a-foreign-key-whose-parent-the-seed-adds")));
        await using var copy = await SqlServerFixture.RegisterAsync();
        await using var orphaned = await SqlServerFixture.RegisterAsync();
        DacFx162Child.Succeeded(["publish", GoldenProject.Profile, copy.Name, basePackage], copy.ConnectionString);
        DacFx162Child.Succeeded(["publish", GoldenProject.Profile, orphaned.Name, basePackage], orphaned.ConnectionString);
        await SqlServerFixture.ExecuteAsync(orphaned.ConnectionString, SecondOrphan);
        using var scratch = ScratchFolder.Temporary("spike-s10-162");

        ValidatesAfterTheSeed(Planned162(scratch, copy.Name, copy.ConnectionString, ("head", head))[0].Script);
        DacFx162Child.Succeeded(["publish", GoldenProject.Profile, copy.Name, head], copy.ConnectionString);
        var failed = DacFx162Child.Run(["publish", GoldenProject.Profile, orphaned.Name, head], orphaned.ConnectionString);

        Assert.Equal(0, await Trust(copy.ConnectionString, Key));
        Assert.True(failed.Code == 1 && failed.Errors.Contains("Msg 547", StringComparison.Ordinal), failed.Output + failed.Errors);
        Assert.Equal((1, 1, 1), await AfterTheFailedValidation(orphaned));
    }

    [Fact]
    [Trait("Category", "fixture")]
    public async Task Spike_S11_on_DacFx_162_the_head_plans_agree_but_for_system_names_and_a_check_written_with_IN_is_dropped_and_created()
    {
        var (unnamed, head) = (await GoldenProject.Built(GoldenProject.Change("add-unnamed-constraints")),
            await GoldenProject.Built(GoldenProject.Change("change-an-unnamed-default")));
        await using var environment = await WithHistory();
        await using var copy = await SqlServerFixture.RegisterAsync();
        DacFx162Child.Succeeded(["publish", GoldenProject.Profile, environment.Name, unnamed], environment.ConnectionString);
        DacFx162Child.Succeeded(["publish", GoldenProject.Profile, copy.Name, unnamed], copy.ConnectionString);
        using var scratch = ScratchFolder.Temporary("spike-s11-162");

        var named = await SystemNamesDiffer(environment, copy);
        var onEnvironment = Planned162(scratch, environment.Name, environment.ConnectionString, ("head", head))[0];
        var onCopy = Planned162(scratch, copy.Name, copy.ConnectionString, ("head", head), ("unnamed", unnamed));

        Assert.Equal(Operations(onCopy[0].Report), Operations(onEnvironment.Report));
        Assert.Contains("DROP CONSTRAINT [" + named + "]", onEnvironment.Script, StringComparison.Ordinal);
        Assert.Equal(Normalized(onCopy[0].Script, copy.Name), Normalized(onEnvironment.Script, environment.Name));
        Assert.Equal(CheckWrittenWithInOnNote, Operations(onCopy[1].Report).Order());
        Assert.Contains("ADD CHECK (Pinned IN (0, 1))", onCopy[1].Script, StringComparison.Ordinal);
    }

    /// <summary>The child's script of each named package against the database, into a folder of the scratch folder named after the
    /// database: each plan's script and deploy report, in the order given.</summary>
    private static (string Script, string Report)[] Planned162(ScratchFolder scratch, string database, string connection,
        params (string Name, string Dacpac)[] heads)
    {
        var output = scratch.Folder(database);
        DacFx162Child.Succeeded(["script", GoldenProject.Profile, database, output, .. heads.Select(h => h.Name + "=" + h.Dacpac)], connection);
        return [.. heads.Select(h => (File.ReadAllText(Path.Combine(output, h.Name + ".sql")), File.ReadAllText(Path.Combine(output, h.Name + ".xml"))))];
    }
}
