using System;
using System.Linq;
using System.Text.Json;
using CsCheck;
using DbChange.Tests;
using Xunit;
using static DbChange.Kernel.Tests.KernelProperties;

namespace DbChange.Kernel.Tests;

/// <summary>
/// The change between two models names what was created, dropped, renamed (a pair the refactorlog records, whose
/// children and references move with it) and altered, property by property and relationship by relationship, the
/// deploy scripts included. Generated sets pin the algebra; the sample changes pin what each one names.
/// </summary>
public sealed class ChangeTests
{
    /// <summary>
    /// A change names only what its two models hold: each element it creates is one of the second model's, each it drops one of the
    /// first's, and each element it alters is keyed as one of either.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_change_names_only_elements_its_two_models_hold() =>
        Changes.Sample(c => c.Change.Created.All(c.After.Contains) && c.Change.Dropped.All(c.Before.Contains)
            && c.Change.Altered.All(a => c.Before.Concat(c.After).Any(e => e.Key == a.Key)));

    [Fact]
    [Trait("Category", "fast")]
    public void The_change_between_equal_models_is_empty_whatever_the_refactorlog_holds() =>
        Gen.Select(Sets, Renames).Sample((set, renames) =>
            Ok(Change.Between(set, SortedArray.Of(set.Reverse().Select(Rebuilt)), renames)).IsEmpty
            && Ok(Change.Between(SampleChanges.Model(), SampleChanges.Model(), renames)).IsEmpty);

    [Fact]
    [Trait("Category", "fast")]
    public void The_change_from_one_model_to_another_mirrors_the_change_back_through_the_inverted_renames()
    {
        Gen.Select(Sets, Sets).Sample((a, b) => Mirror(Ok(Change.Between(a, b, []))) == Ok(Change.Between(b, a, [])));
        Renamings.Sample(
            r => Mirror(Ok(Change.Between(r.Before, r.After, r.Renames))) == Ok(Change.Between(r.After, r.Before, Inverted(r.Renames))),
            print: Print, iter: 1000);
    }

    [Fact]
    [Trait("Category", "fast")]
    public void Renames_made_one_at_a_time_are_reported_as_renames_whichever_keys_their_entries_were_recorded_under() =>
        Renamings.Sample(
            r => Ok(Change.Between(r.Before, r.After, r.Renames)) is var change
                && change.Renamed == r.Expected
                && change.Dropped == SortedArray.Of(r.Dropped)
                && change.Created == SortedArray.Of(r.After.Where(e => !r.Kept.Any(k => Final(k, r.Entries) == e.Key))),
            print: Print, iter: 1000);

    [Fact]
    [Trait("Category", "fast")]
    public void A_single_generated_edit_yields_exactly_that_one_change_and_undoing_it_the_mirror() =>
        Edits.Sample(
            (before, edit) =>
                Ok(Change.Between(before, edit.After, edit.Renames)) == edit.Expected
                && Ok(Change.Between(edit.After, before, Inverted(edit.Renames))) == Mirror(edit.Expected),
            print: x => x.Item2.Kind, iter: 1000);

    /// <summary>
    /// Decision 2.26: one element's own name flipped in case on a database. Under a case-insensitive collation the pair is one name
    /// (DacFx plans nothing for it: measured on SQL_Latin1_General_CP1_CI_AS), so the change is empty but for the one case-only pair,
    /// the element's children and the references to it moving with it; under a case-sensitive one the database reads two names, so
    /// the element and what is keyed under it are dropped and created, and only relationships that named them are altered. The two
    /// models fingerprint apart under either, since equality and the fingerprint stay ordinal (law 3′).
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_case_only_rename_is_a_note_under_a_case_insensitive_collation_and_a_drop_and_a_create_under_a_case_sensitive_one() =>
        Flips.Sample(f =>
        {
            var (insensitive, sensitive) = (Ok(Change.Between(f.Before, f.After, [], CaseInsensitive)), Ok(Change.Between(f.Before, f.After, [], Collation.CaseSensitive)));
            var moved = f.Before.Where(e => e.Key == f.Flip.Before || Beneath(e.Key, f.Flip.Before)).ToList();
            var referrers = f.Before.Where(e => !moved.Contains(e) && e.Relationships.Any(r => r.Targets.Any(t => moved.Any(m => m.Key == t.Key)))).Select(e => e.Key).ToHashSet();

            Assert.True(insensitive.IsEmpty, "under a case-insensitive collation the flip is a change: " + insensitive);
            Assert.Equal([f.Flip], insensitive.CaseOnlyRenamed);
            Assert.Equal(moved.Select(e => e.Key), sensitive.Dropped.Select(e => e.Key));
            Assert.Equal(moved.Count, sensitive.Created.Count);
            Assert.Empty(sensitive.Renamed);
            Assert.Empty(sensitive.CaseOnlyRenamed);
            Assert.All(sensitive.Altered, a => Assert.True(a.Properties.Count == 0 && referrers.Contains(a.Key), a.Key + " is altered by other than a reference to the flipped element"));
            Assert.NotEqual(Fingerprint.Of(f.Before), Fingerprint.Of(f.After));
            Assert.Equal(Mirror(insensitive), Ok(Change.Between(f.After, f.Before, [], CaseInsensitive)));
        }, print: f => f.Flip.ToString(), iter: 500);

    /// <summary>The mirror property under a case-insensitive collation, over case-distinct sets: the case-only pairs invert with the rest.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void The_change_between_case_distinct_models_under_a_case_insensitive_collation_mirrors_back() =>
        Gen.Select(CaseDistinctSets, CaseDistinctSets).Sample((a, b) => Mirror(Ok(Change.Between(a, b, [], CaseInsensitive))) == Ok(Change.Between(b, a, [], CaseInsensitive)));

    /// <summary>A model holding two keys that differ in letter case alone is refused under a case-insensitive collation, which reads them as one name, and read under a case-sensitive one.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void Two_elements_whose_keys_differ_in_case_alone_are_one_key_under_a_case_insensitive_collation()
    {
        var twice = SortedArray.Of(New(Key("Table", "dbo", "Customer"), []), New(Key("Table", "dbo", "CUSTOMER"), []));

        Expect.Failed(Change.Between(twice, [], [], CaseInsensitive), "model.duplicate-key");
        Assert.True(Ok(Change.Between(twice, twice, [], Collation.CaseSensitive)).IsEmpty);
    }

    [Theory]
    [Trait("Category", "fast")]
    [InlineData("SQL_Latin1_General_CP1_CI_AS", false, true)]
    [InlineData("Latin1_General_CS_AS", true, true)]
    [InlineData("Latin1_General_BIN2", true, true)]
    [InlineData("Latin1_General_100_CI_AI_SC_UTF8", false, false)]
    [InlineData("Japanese_XJIS_140_CS_AI", true, false)]
    public void A_collation_s_name_says_whether_names_compare_with_case(string name, bool caseSensitive, bool accentSensitive)
    {
        var collation = Ok(Collation.Of(name));

        Assert.Equal((name, caseSensitive, accentSensitive), (collation.Name, collation.IsCaseSensitive, collation.IsAccentSensitive));
        Assert.Equal(!caseSensitive, Key("Table", "dbo", "Customer").Matches(Key("Table", "dbo", "customer"), collation));
        Assert.False(Key("Table", "dbo", "Customer").Matches(Key("View", "dbo", "Customer"), collation));
    }

    [Theory]
    [Trait("Category", "fast")]
    [InlineData("Latin1_General")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("Latin1_General_CS_AS; DROP TABLE x")]
    public void A_collation_name_with_no_case_rule_is_refused(string? name) => Expect.Failed(Collation.Of(name), "model.collation");

    /// <summary>Whether <paramref name="key"/> lies under <paramref name="ancestor"/>.</summary>
    private static bool Beneath(ElementKey key, ElementKey ancestor) => key.Parent is { } parent && (parent == ancestor || Beneath(parent, ancestor));

    [Fact]
    [Trait("Category", "fast")]
    public void A_rename_renders_and_serializes_the_key_before_and_the_key_after()
    {
        var rename = Ok(Rename.Of(SampleChanges.Email, "EmailAddress"));

        var line = $"{rename}";

        Assert.Contains("Column [dbo].[Customer].[Email]", line, StringComparison.Ordinal);
        Assert.Contains("Column [dbo].[Customer].[EmailAddress]", line, StringComparison.Ordinal);
        Assert.Contains("\"EmailAddress\"", JsonSerializer.Serialize(rename), StringComparison.Ordinal);
        Assert.All(Between("rename a table and a column").Renamed, r => Assert.Contains(r.After.ToString(), $"{r}", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Category", "fast")]
    public void Make_mandatory_changes_the_nullable_property_of_the_one_column_and_nothing_else() =>
        Assert.Equal(Altered(SampleChanges.Email, [new Change.Property("Nullable", Bool(true), Bool(false))]), Between("make-mandatory"));

    [Fact]
    [Trait("Category", "fast")]
    public void Adding_a_column_reports_it_created_and_appends_it_to_its_table_s_columns()
    {
        var (_, after, _) = SampleChanges.Pair("add-a-nullable-column");
        var nickname = after.Single(e => e.Key.Name.Base == "Nickname");

        Assert.Equal(
            new Change([nickname], [], [], [Columns(["Id", "Email", "Notes"], ["Id", "Email", "Notes", "Nickname"])]),
            Between("add-a-nullable-column"));
    }

    [Fact]
    [Trait("Category", "fast")]
    public void Dropping_a_column_reports_it_dropped_and_takes_it_out_of_its_table_s_columns()
    {
        var (before, _, _) = SampleChanges.Pair("drop a column");
        var notes = before.Single(e => e.Key.Name.Base == "Notes");

        Assert.Equal(
            new Change([], [notes], [], [Columns(["Id", "Email", "Notes"], ["Id", "Email"])]),
            Between("drop a column"));
    }

    [Fact]
    [Trait("Category", "fast")]
    public void Renaming_a_column_with_its_refactorlog_entry_is_one_rename_not_a_drop_and_an_add()
    {
        var rename = new Rename(SampleChanges.Email, Key(SampleChanges.Customer, "Column", "EmailAddress"));
        var (before, after, renames) = SampleChanges.Pair("rename a column");

        Assert.Equal(new Change([SampleChanges.EmailEntry], [], [rename], []), Between("rename a column"));
        Assert.Equal(
            new Change([], [SampleChanges.EmailEntry], [rename.Inverted()], []),
            Ok(Change.Between(after, before, Inverted(renames))));
    }

    [Fact]
    [Trait("Category", "fast")]
    public void Renaming_a_table_moves_its_columns_and_index_and_the_references_to_them_with_it() =>
        Assert.Equal(
            new Change([SampleChanges.TableEntry], [], [new Rename(SampleChanges.Customer, Key("Table", "dbo", "Client"))], []),
            Between("rename a table"));

    [Fact]
    [Trait("Category", "fast")]
    public void Renaming_a_column_then_its_table_is_two_renames_though_the_column_s_entry_names_the_table_s_old_key()
    {
        var client = Key("Table", "dbo", "Client");
        SortedArray<Rename> renames = [new Rename(SampleChanges.Customer, client), new Rename(SampleChanges.Email, Key(client, "Column", "EmailAddress"))];
        var (before, after, entries) = SampleChanges.Pair("rename a table and a column");

        Assert.Equal(new Change([SampleChanges.EmailEntry, SampleChanges.TableEntry], [], renames, []), Between("rename a table and a column"));
        Assert.Equal(
            new Change([], [SampleChanges.EmailEntry, SampleChanges.TableEntry], Inverted(renames), []),
            Ok(Change.Between(after, before, Inverted(entries))));
    }

    [Fact]
    [Trait("Category", "fast")]
    public void A_rename_without_its_refactorlog_entry_is_a_drop_and_an_add()
    {
        var (before, after, _) = SampleChanges.Pair("rename a column");
        var address = Key(SampleChanges.Customer, "Column", "EmailAddress");
        var change = Ok(Change.Between(before, SortedArray.Of(after.Where(e => e != SampleChanges.EmailEntry)), []));

        Assert.Equal(new[] { after.Single(e => e.Key == address) }, change.Created);
        Assert.Equal(new[] { before.Single(e => e.Key == SampleChanges.Email) }, change.Dropped);
        Assert.Empty(change.Renamed);
        Assert.Equal(new[] { SampleChanges.Customer, SampleChanges.EmailIndex }, change.Altered.Select(a => a.Key));
    }

    [Fact]
    [Trait("Category", "fast")]
    public void A_post_deploy_seed_edit_changes_the_post_deploy_script_alone() =>
        Assert.Equal(Script(Element.PostDeploymentScript, "(2, N'Closed')"), Between("edit-the-post-deploy-seed"));

    [Fact]
    [Trait("Category", "fast")]
    public void A_pre_deploy_edit_changes_the_pre_deploy_script_alone() =>
        Assert.Equal(Script(Element.PreDeploymentScript, "Customer"), Between("edit-the-pre-deploy-script"));

    [Fact]
    [Trait("Category", "fast")]
    public void Two_elements_of_one_model_with_one_key_fail_as_change_duplicate_key()
    {
        var twice = SortedArray.Of(New(SampleChanges.Customer, [("IsMemoryOptimized", Bool(false))]), New(SampleChanges.Customer, []));

        Expect.Failed(Change.Between(twice, [], []), "model.duplicate-key");
        Expect.Failed(Change.Between([], twice, []), "model.duplicate-key");
    }

    private static SortedArray<Rename> Inverted(SortedArray<Rename> renames) => SortedArray.Of(renames.Select(r => r.Inverted()));

    // A failing renaming, printed as its models' keys and its entries in the order they were made.
    private static string Print(Renaming r) =>
        string.Join(", ", r.Before.Select(e => e.Key)) + " => " + string.Join(", ", r.After.Select(e => e.Key)) + " by " + string.Join("; ", r.Entries);

    private static Change Between(string sample)
    {
        var (before, after, renames) = SampleChanges.Pair(sample);
        return Ok(Change.Between(before, after, renames));
    }

    private static Change.Alteration Columns(string[] before, string[] after) => new(
        SampleChanges.Customer,
        [],
        [new Change.Relationship("Columns", Targets(before), Targets(after))]);

    private static SortedArray<Element.Relationship.Target> Targets(string[] columns) =>
        Element.Relationship.Of("Columns", columns.Select(c => Key(SampleChanges.Customer, "Column", c))).Targets;

    // The one change a script edit makes: its Text, before and after, and nothing else.
    private static Change Script(string type, string edit)
    {
        var (before, after, _) = SampleChanges.Pair(type == Element.PreDeploymentScript ? "edit-the-pre-deploy-script" : "edit-the-post-deploy-seed");
        var (was, now) = (before.Single(e => e.Key.Type == type), after.Single(e => e.Key.Type == type));
        Assert.Contains(edit, ((Value.Script)now["Text"]!).Content, StringComparison.Ordinal);
        return Altered(was.Key, [new Change.Property("Text", was["Text"], now["Text"])]);
    }
}
