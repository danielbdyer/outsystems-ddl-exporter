using System;
using System.Linq;
using System.Text.Json;
using CsCheck;
using Xunit;
using static Estate.Kernel.Tests.ElementSets;

namespace Estate.Kernel.Tests;

/// <summary>
/// The change between two models names what was created, dropped, renamed (a pair the refactorlog records, whose
/// children and references move with it) and altered, property by property and relationship by relationship, the
/// deploy scripts included. Generated sets pin the algebra; the sample changes pin what each one names.
/// </summary>
public sealed class ChangeTests
{
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

    [Fact]
    [Trait("Category", "fast")]
    public void A_rename_renders_and_serializes_the_key_before_and_the_key_after()
    {
        var rename = Ok(Rename.Of(SampleChanges.Email, "EmailAddress"));

        var line = $"{rename}";

        Assert.Contains("Column [dbo].[Customer].[Email]", line, StringComparison.Ordinal);
        Assert.Contains("Column [dbo].[Customer].[EmailAddress]", line, StringComparison.Ordinal);
        Assert.Contains("\"EmailAddress\"", JsonSerializer.Serialize(rename), StringComparison.Ordinal);
        Assert.Equal(rename, rename.Inverted().Inverted());
        Assert.All(Between("rename a table and a column").Renamed, r => Assert.Contains(r.After.ToString(), $"{r}", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Category", "fast")]
    public void Make_mandatory_changes_the_nullable_property_of_the_one_column_and_nothing_else()
    {
        var change = Between("make-mandatory");

        Assert.Equal(Altered(SampleChanges.Email, [new Change.Property("Nullable", Bool(true), Bool(false))]), change);
        var line = change.Altered.SelectMany(a => a.Properties.Select(p => a.Key + ": " + p.Name + " " + p.Before + " → " + p.After));
        Assert.Equal("Column [dbo].[Customer].[Email]: Nullable true → false", Assert.Single(line));
    }

    [Fact]
    [Trait("Category", "fast")]
    public void Adding_a_column_adds_it_and_appends_it_to_its_table_s_columns()
    {
        var (_, after, _) = SampleChanges.Pair("add a column");
        var phone = after.Single(e => e.Key.Name.Base == "Phone");

        Assert.Equal(
            new Change([phone], [], [], [Columns(["Id", "Email", "Notes"], ["Id", "Email", "Notes", "Phone"])]),
            Between("add a column"));
    }

    [Fact]
    [Trait("Category", "fast")]
    public void Dropping_a_column_removes_it_and_takes_it_out_of_its_table_s_columns()
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
        Assert.Equal(Script(Element.PostDeploymentScript, "(2, N'Closed')"), Between("a post-deploy seed edit"));

    [Fact]
    [Trait("Category", "fast")]
    public void A_pre_deploy_edit_changes_the_pre_deploy_script_alone() =>
        Assert.Equal(Script(Element.PreDeploymentScript, "Customer"), Between("a pre-deploy edit"));

    [Fact]
    [Trait("Category", "fast")]
    public void Two_elements_of_one_model_with_one_key_fail_with_change_duplicate_key()
    {
        var twice = SortedArray.Of(New(SampleChanges.Customer, [("IsMemoryOptimized", Bool(false))]), New(SampleChanges.Customer, []));

        Assert.Equal("change.duplicate-key", Assert.IsType<Result<Change>.Failed>(Change.Between(twice, [], [])).Error.Code);
        Assert.Equal("change.duplicate-key", Assert.IsType<Result<Change>.Failed>(Change.Between([], twice, [])).Error.Code);
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
        var (before, after, _) = SampleChanges.Pair(type == Element.PreDeploymentScript ? "a pre-deploy edit" : "a post-deploy seed edit");
        var (was, now) = (before.Single(e => e.Key.Type == type), after.Single(e => e.Key.Type == type));
        Assert.Contains(edit, ((Value.Script)now["Text"]!).Content, StringComparison.Ordinal);
        return Altered(was.Key, [new Change.Property("Text", was["Text"], now["Text"])]);
    }
}
