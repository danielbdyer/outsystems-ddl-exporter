using System.Collections.Generic;
using System.Linq;
using Xunit;
using static DbChange.Kernel.Tests.ElementSets;

namespace DbChange.Kernel.Tests;

/// <summary>
/// The sample changes, hand-built as io/Ssdt.ReadModel reads them: a Customer table with its columns in order, an index
/// on Email, a primary key, both deploy scripts, and a refactorlog holding one old rename (Mail to Email) that every
/// pair carries, so a rename the refactorlog already applied is shown to change nothing. Each sample change is the same
/// model built again with one edit.
/// </summary>
internal static class SampleChanges
{
    public const string PreDeploy = "PRINT N'Checking the Status table';\n";
    public const string Seed = "MERGE INTO [dbo].[Status] AS t\nUSING (VALUES (1, N'Active')) AS s ([Id], [Name]) ON t.[Id] = s.[Id]\nWHEN NOT MATCHED THEN INSERT ([Id], [Name]) VALUES (s.[Id], s.[Name]);\n";

    public static readonly ElementKey Customer = Key("Table", "dbo", "Customer");
    public static readonly ElementKey Email = Key(Customer, "Column", "Email");
    public static readonly ElementKey EmailIndex = Key(Customer, "Index", "IX_Customer_Email");
    public static readonly Element EmailEntry = Entry("7f1a2c3e-0b4d-4e5f-8a9b-0c1d2e3f4a5b", "[dbo].[Customer].[Email]", "SqlSimpleColumn", "[dbo].[Customer]", "[EmailAddress]");
    public static readonly Element TableEntry = Entry("5c4b3a29-1807-4f6e-9d8c-7b6a5f4e3d2c", "[dbo].[Customer]", "SqlTable", "[dbo]", "[Client]");

    private static readonly ElementKey Dbo = Ok(ElementKey.Of("Schema", Ok(Name.Of("dbo"))));
    private static readonly ElementKey IntType = Key("DataType", "sys", "int");
    private static readonly ElementKey NVarCharType = Key("DataType", "sys", "nvarchar");
    private static readonly Element OldEntry = Entry("0d1c7b1e-2a3f-4b5c-9d8e-7f6a5b4c3d2e", "[dbo].[Customer].[Mail]", "SqlSimpleColumn", "[dbo].[Customer]", "[Email]");
    private static readonly Rename OldRename = Ok(Rename.Of(Key(Customer, "Column", "Mail"), "Email"));

    public static TheoryData<string> Names => new(
        "make-mandatory", "add a column", "drop a column", "rename a column", "rename a table", "rename a table and a column",
        "a post-deploy seed edit", "a pre-deploy edit");

    /// <summary>A sample change's model before and after, and the renames the refactorlog after it pairs.</summary>
    public static (SortedArray<Element> Before, SortedArray<Element> After, SortedArray<Rename> Renames) Pair(string sample) => sample switch
    {
        "make-mandatory" => (Model(), Model(emailNullable: false), [OldRename]),
        "add a column" => (Model(), Model(phone: true), [OldRename]),
        "drop a column" => (Model(), Model(notes: false), [OldRename]),
        "rename a column" => (Model(), Model(email: "EmailAddress", entries: [EmailEntry]), [OldRename, Ok(Rename.Of(Email, "EmailAddress"))]),
        "rename a table" => (Model(), Model(table: "Client", entries: [TableEntry]), [OldRename, Ok(Rename.Of(Customer, "Client"))]),
        // The column renamed while its table was still Customer, then the table: SSDT records the column's entry
        // under the table's old name.
        "rename a table and a column" => (
            Model(),
            Model(table: "Client", email: "EmailAddress", entries: [EmailEntry, TableEntry]),
            [OldRename, Ok(Rename.Of(Email, "EmailAddress")), Ok(Rename.Of(Customer, "Client"))]),
        "a post-deploy seed edit" => (Model(), Model(post: Seed.Replace("(1, N'Active')", "(1, N'Active'), (2, N'Closed')", System.StringComparison.Ordinal)), [OldRename]),
        "a pre-deploy edit" => (Model(), Model(pre: "PRINT N'Checking the Status and Customer tables';\n"), [OldRename]),
        _ => throw new KeyNotFoundException(sample),
    };

    /// <summary>The model, with each sample change's edit as a parameter.</summary>
    public static SortedArray<Element> Model(
        string table = "Customer", string email = "Email", bool emailNullable = true, bool notes = true, bool phone = false,
        string pre = PreDeploy, string post = Seed, Element[]? entries = null)
    {
        var t = Key("Table", "dbo", table);
        var id = Key(t, "Column", "Id");
        var mail = Key(t, "Column", email);
        var columns = new[] { id, mail }.Concat(notes ? [Key(t, "Column", "Notes")] : []).Concat(phone ? [Key(t, "Column", "Phone")] : []).ToArray();
        var elements = new List<Element>
        {
            New(t, [("IsMemoryOptimized", Bool(false))], [("Schema", [Dbo]), ("Columns", columns)]),
            New(id, [("Nullable", Bool(false)), ("IsIdentity", Bool(true))], [("DataType", [IntType])]),
            New(mail, [("Nullable", Bool(emailNullable)), ("Length", Int(256))], [("DataType", [NVarCharType])]),
            New(Key(t, "Index", "IX_Customer_Email"), [("IsClustered", Bool(false))], [("Columns", [mail])]),
            New(Key("PrimaryKeyConstraint", "dbo", "PK_Customer"), [("IsClustered", Bool(true))], [("DefiningTable", [t]), ("Columns", [id])]),
            Element.PreDeploy(pre),
            Element.PostDeploy(post),
            OldEntry,
        };
        elements.AddRange(columns.Skip(2).Select(c => New(c, [("Nullable", Bool(true)), ("Length", Int(c.Name.Base == "Notes" ? -1 : 20))], [("DataType", [NVarCharType])])));
        elements.AddRange(entries ?? []);
        return SortedArray.Of(elements);
    }

    private static Element Entry(string key, string elementName, string elementType, string parent, string newName) =>
        Ok(Element.RefactorLogEntry(key, [
            new Element.Property("ElementName", Text(elementName)),
            new Element.Property("ElementType", Text(elementType)),
            new Element.Property("ParentElementName", Text(parent)),
            new Element.Property("NewName", Text(newName)),
        ]));
}
