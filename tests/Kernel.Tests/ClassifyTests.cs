using System.Linq;
using CsCheck;
using DbChange.Tests;
using Xunit;
using static DbChange.Kernel.Tests.KernelProperties;

namespace DbChange.Kernel.Tests;

/// <summary>
/// Classify (WP 2.4) over models the Facsimile's way of writing them builds, with operations recognized by recognize: blocks written here,
/// so each test states the block it reads: an operation by the entries of a change its clauses match; a rename made by typing, refused for
/// a column, a table and a schema move; a column-list change or a table rebuild on a table Change Data Capture tracks, refused; DML in a
/// changed pre-deploy script, a data-modifying step; a check written with IN, named with the OR form as its remedy; and a change no block
/// matches, unclassified. The seeded blocks under tests/Golden/patterns/ are held to the golden sample changes by Io.Tests'
/// ClassifyTests.
/// </summary>
public sealed class ClassifyTests
{
    private static readonly SortedArray<Pattern> Patterns = SortedArray.Of(
        Block("add-optional", "created Column Nullable=true Expression=NULL"),
        Block("add-mandatory", "created Column Nullable=false Expression=NULL"),
        Block("make-mandatory", "altered Column Nullable:true→false"),
        Block("widen", "altered Column Length:rises", "altered Column IsMax:false→true Length:any"),
        Block("narrow", "altered Column Length:falls", "altered Column IsMax:true→false Length:any"),
        Block("delete-attribute", "dropped Column"),
        Block("rename-attribute", "renamed Column"),
        Block("create-entity", "created Table"),
        Block("delete-entity", "dropped Table"));

    private static readonly Model Base = Model.Of(
            "Customer.Id int NOT NULL", "Customer.Name nvarchar(100) NOT NULL", "Customer.Phone nvarchar(40) NULL",
            "Customer.Region nvarchar(50) NULL", "Address.Id int NOT NULL", "Address.Line1 nvarchar(120) NOT NULL")
        .PrimaryKey("PK_Customer", "Customer", "Id").PrimaryKey("PK_Address", "Address", "Id");

    private static readonly Name Customer = Model.TableName("Customer");

    [Fact]
    [Trait("Category", "fast")]
    public void A_recognize_block_names_the_clauses_that_recognize_an_operation_and_how_it_ships()
    {
        var pattern = Ok(Pattern.Parse("widen",
            "# widen\n\nA column made longer.\n\n```text\nrecognize:\n  match: altered Column Length:rises\n"
                + "  match: altered Column IsMax:false→true Length:any\n  ships: one release, in place\n```\n\nMore prose.\n"));

        Assert.NotNull(pattern);
        Assert.Equal(("widen", "one release, in place", 2), (pattern.Operation, pattern.Ships, pattern.Clauses.Count));
        Assert.Null(Ok(Pattern.Parse("rebuild-index", "# rebuild-index\n\nMaintenance, which no change shows.\n")));
    }

    [Theory]
    [Trait("Category", "fast")]
    [InlineData("recognize:\n  match: frobbed Column\n  ships: one release\n", "line 3")]
    [InlineData("recognize:\n  match: created Column Length:rises\n  ships: one release\n", "line 3")]
    [InlineData("recognize:\n  match: altered\n  ships: one release\n", "line 3")]
    [InlineData("recognize:\n  match: created Column\n", "no ships: line")]
    [InlineData("recognize:\n  ships: one release\n", "no match: line")]
    [InlineData("recognize:\n  match: created Column\n  when: always\n  ships: one release\n", "line 4")]
    public void A_recognize_block_that_breaks_the_grammar_is_refused_naming_the_file_and_its_line(string block, string where)
    {
        var error = Expect.Failed(Pattern.Parse("add-optional", "# add-optional\n" + block), "pattern.malformed");

        Assert.Contains("add-optional", error.Message, System.StringComparison.Ordinal);
        Assert.Contains(where, error.Message, System.StringComparison.Ordinal);
    }

    /// <summary>A column created on a table the change does not create is the operation its clause matches, and the table's Columns
    /// relationship, which lists the new column, is part of it; a table created with its columns and its key is one create-entity.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void An_element_the_change_creates_is_one_entry_with_what_it_holds_and_what_names_only_it()
    {
        var added = Classified(Base, Base.With("Customer.Nickname nvarchar(40) NULL", "Customer.Tier int NOT NULL"));
        var created = Classified(Base, Base.With("Note.Id int NOT NULL", "Note.Body nvarchar(200) NULL").PrimaryKey("PK_Note", "Note", "Id"));

        Assert.Equal(["add-mandatory Column [dbo].[Customer].[Tier]", "add-optional Column [dbo].[Customer].[Nickname]"], added.Lines);
        Assert.Equal(["create-entity Table [dbo].[Note]"], created.Lines);
        Assert.Equal("provisional: create-entity", created.Summary);
    }

    /// <summary>A table whose columns stay but stand in another order, which DacFx plans as a rebuild of the table, and an index whose key
    /// gains a created column or puts its columns in another order, which DacFx drops and creates again, are each an altered entry of its
    /// own that no block here recognizes, not part of the created column's entry and not no change.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void An_alteration_that_reorders_its_targets_or_gains_a_created_element_it_does_not_hold_is_an_entry_of_its_own()
    {
        var reordered = Model.Of(
                "Customer.Id int NOT NULL", "Customer.Phone nvarchar(40) NULL", "Customer.Name nvarchar(100) NOT NULL",
                "Customer.Region nvarchar(50) NULL", "Address.Id int NOT NULL", "Address.Line1 nvarchar(120) NOT NULL")
            .PrimaryKey("PK_Customer", "Customer", "Id").PrimaryKey("PK_Address", "Address", "Id");
        var indexed = Base.Index("IX_C", "Customer", ["Id", "Name"]);

        var moved = Classified(Base, reordered);
        var extended = Classified(indexed, Base.With("Customer.Tier int NULL").Index("IX_C", "Customer", ["Id", "Name", "Tier"]));
        var swapped = Classified(indexed, Base.Index("IX_C", "Customer", ["Name", "Id"]));

        Assert.Equal(["unclassified change: prove it, altered Table [dbo].[Customer]"], moved.Lines);
        Assert.Equal("provisional: unclassified change: prove it", moved.Summary);
        Assert.Equal(["add-optional Column [dbo].[Customer].[Tier]", "unclassified change: prove it, altered Index [dbo].[Customer].[IX_C]"],
            extended.Lines);
        Assert.Equal(["unclassified change: prove it, altered Index [dbo].[Customer].[IX_C]"], swapped.Lines);
    }

    /// <summary>A clause that matches an alteration accounts for every property the alteration changes, so 50 to 100 is widen, 50 to max is
    /// widen by its second clause, max to 50 is narrow, and a column made longer and NOT NULL at once matches no block.</summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("Customer.Phone nvarchar(80) NULL", "widen Column [dbo].[Customer].[Phone]")]
    [InlineData("Customer.Phone nvarchar(max) NULL", "widen Column [dbo].[Customer].[Phone]")]
    [InlineData("Customer.Phone nvarchar(20) NULL", "narrow Column [dbo].[Customer].[Phone]")]
    [InlineData("Customer.Phone nvarchar(40) NOT NULL", "make-mandatory Column [dbo].[Customer].[Phone]")]
    [InlineData("Customer.Phone nvarchar(80) NOT NULL", "unclassified change: prove it, altered Column [dbo].[Customer].[Phone]")]
    public void A_clause_recognizes_an_alteration_only_when_it_accounts_for_every_property_the_alteration_changes(string column,
        string line)
    {
        Assert.Equal(line, Classified(Base, Base.With(column)).Lines.First());
        Assert.Equal("narrow Column [dbo].[Customer].[Phone]", Classified(Base.With("Customer.Phone nvarchar(max) NULL"), Base).Lines.First());
    }

    [Fact]
    [Trait("Category", "fast")]
    public void A_change_no_block_matches_is_an_unclassified_change_to_prove_with_a_warning_that_names_it()
    {
        var classification = Classified(Base, Base.Check("CK_Customer_Region", "Customer", "([Region]<>N'')", ["Region"]));

        Assert.Equal(["unclassified change: prove it, created CheckConstraint [dbo].[CK_Customer_Region]"], classification.Lines);
        Assert.Equal("provisional: unclassified change: prove it", classification.Summary);
        var warning = Assert.Single(classification.Findings);
        Assert.Equal(("classify.unclassified", Severity.Warning), (warning.Code, warning.Severity));
        Assert.Contains("dbchange prove", warning.Remedy, System.StringComparison.Ordinal);
    }

    /// <summary>
    /// The finding no publish can raise, for a column: a dropped and a created column of one type at one ordinal of one table, with no
    /// refactorlog entry, is a rename made by typing, refused with the remedy that writes the entry; the same rename with its entry is
    /// rename-attribute, and a pair at two ordinals or of two types is a drop and an add.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_column_renamed_by_typing_without_its_refactorlog_entry_is_refused_with_the_remedy_that_writes_the_entry()
    {
        var typed = Base.RenameColumn("Customer.Phone", "Mobile");
        var refused = Classified(Base, typed);
        var entry = Ok(Rename.Of(Model.ColumnKey("Customer.Phone"), "Mobile"));

        Assert.Equal(["error rename.column-without-entry Column [dbo].[Customer].[Mobile]"], refused.Lines);
        Assert.True(refused.Refused);
        Assert.StartsWith("provisional: refused: rename.column-without-entry", refused.Summary, System.StringComparison.Ordinal);
        var finding = Assert.Single(refused.Findings);
        Assert.Contains("dbchange diff --refactorlog", finding.Remedy, System.StringComparison.Ordinal);
        Assert.Contains("[dbo].[Customer].[Phone]", finding.Message, System.StringComparison.Ordinal);
        Assert.Equal(["rename-attribute Column [dbo].[Customer].[Mobile]"], Classified(Base, typed, renames: [entry]).Lines);
        Assert.Equal(["add-optional Column [dbo].[Customer].[Mobile]", "delete-attribute Column [dbo].[Customer].[Phone]"],
            Classified(Base, Base.WithoutColumn("Customer.Phone").With("Customer.Mobile nvarchar(40) NULL")).Lines);
        Assert.False(Classified(Base, Base.RenameColumn("Customer.Phone", "Mobile").With("Customer.Mobile nvarchar(41) NULL")).Refused);
    }

    /// <summary>The finding no publish can raise, for a table: a dropped and a created table with the same columns, in order and of the
    /// same types, with no refactorlog entry, is refused; its key goes with it, so no other entry is left over.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_table_renamed_by_typing_without_its_refactorlog_entry_is_refused_with_the_remedy_that_writes_the_entry()
    {
        var refused = Classified(Base, Base.MoveTable("Address", "dbo", "Location"));
        var entry = Ok(Rename.Of(Model.TableKey("Address"), "Location"));

        Assert.Equal(["error rename.table-without-entry Table [dbo].[Location]"], refused.Lines);
        Assert.Contains("dbchange diff --refactorlog", Assert.Single(refused.Findings).Remedy, System.StringComparison.Ordinal);
        Assert.False(Classified(Base, Base.MoveTable("Address", "dbo", "Location"), renames: [entry]).Refused);
        Assert.Equal(["create-entity Table [dbo].[Location]", "delete-entity Table [dbo].[Address]"],
            Classified(Base, Base.MoveTable("Address", "dbo", "Location").With("Location.City nvarchar(80) NULL")).Lines);
    }

    /// <summary>The finding no publish can raise, for a schema move: a same-named table dropped from one schema and created in another,
    /// with no refactorlog entry, is refused whatever its columns, since the publish creates the moved table empty.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_table_moved_to_another_schema_without_its_refactorlog_entry_is_refused_with_the_remedy_that_writes_the_entry()
    {
        var refused = Classified(Base, Base.MoveTable("Address", "sales").With("sales.Address.City nvarchar(80) NULL"));

        Assert.Equal(["error rename.schema-move-without-entry Table [sales].[Address]"], refused.Lines);
        var finding = Assert.Single(refused.Findings);
        Assert.Contains("dbchange diff --refactorlog", finding.Remedy, System.StringComparison.Ordinal);
        Assert.Contains("[sales]", finding.Message, System.StringComparison.Ordinal);
    }

    /// <summary>
    /// On a table dbchange/ledgers/cdc-tracked.md names, a column created, dropped or renamed is refused, since a capture instance keeps
    /// the column list it was enabled with; so is a rebuild of the table in the environment's plan, which drops the capture instance bound
    /// to the old table's object_id. A table no ledger row names is neither.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_column_list_change_or_a_rebuild_of_a_table_change_data_capture_tracks_is_refused()
    {
        var tracked = Classify.Inputs.None with { CdcTracked = [Customer] };
        var added = Classified(Base, Base.With("Customer.Nickname nvarchar(40) NULL"), tracked);
        var rebuilt = Classified(Base, Base.With("Customer.Phone nvarchar(40) NOT NULL"),
            tracked with { Plan = [new ScriptStatement.Rebuilds(Customer)] });

        Assert.Equal(["add-optional Column [dbo].[Customer].[Nickname]", "error cdc.column-list Table [dbo].[Customer]"], added.Lines);
        Assert.Equal(["make-mandatory Column [dbo].[Customer].[Phone]", "error cdc.table-rebuild Table [dbo].[Customer]"], rebuilt.Lines);
        Assert.True(Classified(Base, Base.RenameColumn("Customer.Phone", "Mobile"), tracked,
            [Ok(Rename.Of(Model.ColumnKey("Customer.Phone"), "Mobile"))]).Refused);
        Assert.False(Classified(Base, Base.With("Address.City nvarchar(80) NULL"),
            tracked with { Plan = [new ScriptStatement.Rebuilds(Model.TableName("Address"))] }).Refused);
    }

    /// <summary>DML in a pre-deploy script the change creates or alters is a data-modifying step, which a failed deploy does not roll back;
    /// a pre-deploy script edited without DML is a step alone, and one the change leaves as it was raises nothing.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void DML_in_a_pre_deploy_script_the_change_edits_is_a_data_modifying_step()
    {
        var update = Classify.Inputs.None with { PreDeploy = [new ScriptStatement.ModifiesRows(Customer, "UPDATE")] };
        var before = SortedArray.Of(Base.Elements.Append(Element.PreDeploy("PRINT N'No backfill.';\n")));
        var after = SortedArray.Of(Base.Elements.Append(Element.PreDeploy("UPDATE dbo.Customer SET Region = N'West';\n")));

        var edited = Classify.Of(before, after, Ok(Change.Between(before, after, [])), Patterns, update);

        Assert.Equal(["pre-deploy step PreDeploymentScript [PreDeploy]",
            "warning classify.data-modifying-step PreDeploymentScript [PreDeploy]"], edited.Lines);
        Assert.Contains("UPDATE", Assert.Single(edited.Findings).Message, System.StringComparison.Ordinal);
        Assert.Equal("provisional: a pre-deploy step", edited.Summary);
        Assert.Equal(["pre-deploy step PreDeploymentScript [PreDeploy]"],
            Classify.Of(before, after, Ok(Change.Between(before, after, [])), Patterns, Classify.Inputs.None).Lines);
        Assert.Empty(Classify.Of(after, after, Ok(Change.Between(after, after, [])), Patterns, update).Lines);
    }

    /// <summary>By the operator's ruling of 2026-10-19, a check written with IN, which SQL Server stores as a chain of ORs so that every
    /// plan drops and re-creates it, is named with its remedy; the OR form, and IN inside a string literal, are not.</summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("([Region] IN (N'West', N'East'))", true)]
    [InlineData("([Region] not in(N'West'))", true)]
    [InlineData("([Region]=N'West' OR [Region]=N'East')", false)]
    [InlineData("([Region]<>N'IN (A)')", false)]
    public void A_check_written_with_IN_is_named_with_the_OR_form_SQL_Server_stores_as_its_remedy(string expression, bool named)
    {
        var classification = Classified(Base, Base.Check("CK_Customer_Region", "Customer", expression, ["Region"]));
        var finding = classification.Findings.SingleOrDefault(f => f.Code == "classify.check-in-list");

        Assert.Equal(named, finding is not null);
        Assert.True(finding is null || finding.Remedy!.Contains("OR", System.StringComparison.Ordinal));
    }

    /// <summary>Whatever the change, the classification's first word is provisional: only a proof may drop it.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void The_first_word_of_every_classification_is_provisional() =>
        Changes.Sample((before, after, change) => Classify.Of(before, after, change, Patterns, Classify.Inputs.None).Summary
            .StartsWith("provisional: ", System.StringComparison.Ordinal), iter: 500);

    private static ChangeClassification Classified(Model before, Model after, Classify.Inputs? inputs = null,
        SortedArray<Rename> renames = default) =>
        Classify.Of(before.Elements, after.Elements, Ok(Change.Between(before.Elements, after.Elements, renames)), Patterns,
            inputs ?? Classify.Inputs.None);

    private static Pattern Block(string operation, params string[] clauses) => Ok(Pattern.Parse(operation,
        "recognize:\n" + string.Concat(clauses.Select(c => "  match: " + c + "\n")) + "  ships: as written\n"))!;
}
