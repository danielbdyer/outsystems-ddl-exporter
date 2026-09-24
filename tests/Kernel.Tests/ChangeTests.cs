using System;
using System.Linq;
using CsCheck;
using Xunit;
using static Estate.Kernel.Tests.ElementSets;

namespace Estate.Kernel.Tests;

/// <summary>
/// The change between two reads names what was added, removed, renamed (a pair the refactorlog records, whose
/// children and references move with it) and changed, property by property and relationship by relationship, the
/// deploy scripts included. Generated sets pin the algebra; the archetype pairs pin what each archetype names.
/// </summary>
public sealed class ChangeTests
{
    [Fact]
    [Trait("Category", "fast")]
    public void The_change_between_equal_reads_is_empty_whatever_the_refactorlog_holds() =>
        Gen.Select(Sets, Renames).Sample((set, renames) =>
            Ok(Change.Between(set, Seq.Of(set.Reverse().Select(Rebuilt)), renames)).IsEmpty
            && Ok(Change.Between(Archetypes.Model(), Archetypes.Model(), renames)).IsEmpty);

    [Fact]
    [Trait("Category", "fast")]
    public void The_change_from_one_read_to_another_mirrors_the_change_back() =>
        Gen.Select(Sets, Sets).Sample((a, b) => Mirror(Ok(Change.Between(a, b, []))) == Ok(Change.Between(b, a, [])));

    [Fact]
    [Trait("Category", "fast")]
    public void A_single_generated_edit_yields_exactly_that_one_change_and_undoing_it_the_mirror() =>
        Edits.Sample(
            (before, edit) =>
                Ok(Change.Between(before, edit.After, edit.Renames)) == edit.Expected
                && Ok(Change.Between(edit.After, before, Seq.Of(edit.Renames.Select(r => r.Inverse)))) == Mirror(edit.Expected),
            print: x => x.Item2.Kind, iter: 1000);

    [Fact]
    [Trait("Category", "fast")]
    public void Make_mandatory_changes_the_nullable_property_of_the_one_column_and_nothing_else()
    {
        var change = Between("make-mandatory");

        Assert.Equal(Altered(Archetypes.Email, [new Change.Property("Nullable", Bool(true), Bool(false))]), change);
        var line = change.Changed.SelectMany(a => a.Properties.Select(p => a.Key + ": " + p.Name + " " + p.Before + " → " + p.After));
        Assert.Equal("Column [dbo].[Customer].[Email]: Nullable true → false", Assert.Single(line));
    }

    [Fact]
    [Trait("Category", "fast")]
    public void Adding_a_column_adds_it_and_appends_it_to_its_table_s_columns()
    {
        var (_, after, _) = Archetypes.Pair("add a column");
        var phone = after.Single(e => e.Key.Name.Base == "Phone");

        Assert.Equal(
            new Change([phone], [], [], [Columns(["Id", "Email", "Notes"], ["Id", "Email", "Notes", "Phone"])]),
            Between("add a column"));
    }

    [Fact]
    [Trait("Category", "fast")]
    public void Dropping_a_column_removes_it_and_takes_it_out_of_its_table_s_columns()
    {
        var (before, _, _) = Archetypes.Pair("drop a column");
        var notes = before.Single(e => e.Key.Name.Base == "Notes");

        Assert.Equal(
            new Change([], [notes], [], [Columns(["Id", "Email", "Notes"], ["Id", "Email"])]),
            Between("drop a column"));
    }

    [Fact]
    [Trait("Category", "fast")]
    public void Renaming_a_column_with_its_refactorlog_entry_is_one_rename_not_a_drop_and_an_add()
    {
        var rename = new Rename(Archetypes.Email, Key(Archetypes.Customer, "Column", "EmailAddress"));
        var (before, after, renames) = Archetypes.Pair("rename a column");

        Assert.Equal(new Change([Archetypes.EmailEntry], [], [rename], []), Between("rename a column"));
        Assert.Equal(
            new Change([], [Archetypes.EmailEntry], [rename.Inverse], []),
            Ok(Change.Between(after, before, Seq.Of(renames.Select(r => r.Inverse)))));
    }

    [Fact]
    [Trait("Category", "fast")]
    public void Renaming_a_table_moves_its_columns_and_index_and_the_references_to_them_with_it() =>
        Assert.Equal(
            new Change([Archetypes.TableEntry], [], [new Rename(Archetypes.Customer, Key("Table", "dbo", "Client"))], []),
            Between("rename a table"));

    [Fact]
    [Trait("Category", "fast")]
    public void A_rename_without_its_refactorlog_entry_is_a_drop_and_an_add()
    {
        var (before, after, _) = Archetypes.Pair("rename a column");
        var address = Key(Archetypes.Customer, "Column", "EmailAddress");
        var change = Ok(Change.Between(before, Seq.Of(after.Where(e => e != Archetypes.EmailEntry)), []));

        Assert.Equal(new[] { after.Single(e => e.Key == address) }, change.Added);
        Assert.Equal(new[] { before.Single(e => e.Key == Archetypes.Email) }, change.Removed);
        Assert.Empty(change.Renamed);
        Assert.Equal(new[] { Archetypes.Customer, Archetypes.EmailIndex }, change.Changed.Select(a => a.Key));
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
    public void Two_elements_of_one_read_with_one_key_are_refused()
    {
        var twice = Seq.Of(New(Archetypes.Customer, [("IsMemoryOptimized", Bool(false))]), New(Archetypes.Customer, []));

        Assert.Equal("change.duplicate-key", Assert.IsType<Result<Change>.Refused>(Change.Between(twice, [], [])).Refusal.Code);
        Assert.Equal("change.duplicate-key", Assert.IsType<Result<Change>.Refused>(Change.Between([], twice, [])).Refusal.Code);
    }

    private static Change Between(string archetype)
    {
        var (before, after, renames) = Archetypes.Pair(archetype);
        return Ok(Change.Between(before, after, renames));
    }

    private static Change.Altered Columns(string[] before, string[] after) => new(
        Archetypes.Customer,
        [],
        [new Change.Relationship("Columns", Targets(before), Targets(after))]);

    private static Seq<Element.Relationship.Target> Targets(string[] columns) =>
        Element.Relationship.Of("Columns", columns.Select(c => Key(Archetypes.Customer, "Column", c))).Targets;

    // The one change a script edit makes: its Text, before and after, and nothing else.
    private static Change Script(string type, string edit)
    {
        var (before, after, _) = Archetypes.Pair(type == Element.PreDeploymentScript ? "a pre-deploy edit" : "a post-deploy seed edit");
        var (was, now) = (before.Single(e => e.Key.Type == type), after.Single(e => e.Key.Type == type));
        Assert.Contains(edit, ((Value.Text)now["Text"]!).Content, StringComparison.Ordinal);
        return Altered(was.Key, [new Change.Property("Text", was["Text"], now["Text"])]);
    }
}
