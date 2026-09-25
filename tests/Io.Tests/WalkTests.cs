using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Estate.Budgets.Tests;
using Estate.Kernel;
using Microsoft.SqlServer.Dac;
using Microsoft.SqlServer.Dac.Model;
using Xunit;
using Xunit.Abstractions;

namespace Estate.Io.Tests;

/// <summary>
/// io/Ssdt's walk (WP 1.2) against real builds: law 3′'s io half (M1 exit 4) and WP 1.3's archetype properties, each
/// archetype an edited copy of the proving ground built against dist/estate/ and walked. Package walks are compared with
/// package walks only. Make-mandatory edits Customer.Email, the proving ground's own populated nullable column (the seed
/// plants rows with and without an Email).
/// </summary>
[Collection(PublishedToolCollection.Name)]
public sealed class WalkTests(ProvingGroundWalks walks, ITestOutputHelper output) : IClassFixture<ProvingGroundWalks>
{
    /// <summary>What each archetype's change names, one line per element created, dropped, renamed or altered.</summary>
    private static readonly Dictionary<string, string[]> Named = new()
    {
        ["make-mandatory"] = ["Column [dbo].[Customer].[Email]: Nullable true → false"],
        ["add a nullable column"] = ["created Column [dbo].[Customer].[Nickname]", "Table [dbo].[Customer]: Columns"],
        ["drop a column"] = ["dropped Column [dbo].[Product].[LegacyCode]", "dropped DefaultConstraint [dbo].[DF_Product_LegacyCode]", "Table [dbo].[Product]: Columns"],
        ["widen a column"] = ["Column [dbo].[Product].[Code]: Length 50 → 100"],
        ["add a check constraint"] = ["created CheckConstraint [dbo].[CK_Product_Code]"],
        ["add a foreign key"] = ["created ForeignKeyConstraint [dbo].[FK_Order_Customer_CustomerId]"],
        ["a seed edit"] = ["PostDeploymentScript [PostDeploy]: Text"],
        ["a pre-deploy edit"] = ["PreDeploymentScript [PreDeploy]: Text"],
        ["rename a column"] = [
            "created RefactorLogOperation [" + ProvingGroundWalks.RenameKey + "]",
            "renamed Column [dbo].[Customer].[ContactPhone] to Column [dbo].[Customer].[MobileNumber]"],
    };

    private const string RenameKey = "0b6f2d7e-4c1a-4e3b-8d5f-7a9c1e2b3d40";

    /// <summary>The refactorlog SSDT writes when column dbo.T.C is renamed A.</summary>
    private const string RenameCToA = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n"
        + "<Operations Version=\"1.0\" xmlns=\"http://schemas.microsoft.com/sqlserver/dac/Serialization/2012/02\">\n"
        + "  <Operation Name=\"Rename Refactor\" Key=\"" + RenameKey + "\" ChangeDateTime=\"09/25/2026 10:00:00\">\n"
        + "    <Property Name=\"ElementName\" Value=\"[dbo].[T].[C]\" />\n    <Property Name=\"ElementType\" Value=\"SqlSimpleColumn\" />\n"
        + "    <Property Name=\"ParentElementName\" Value=\"[dbo].[T]\" />\n    <Property Name=\"ParentElementType\" Value=\"SqlTable\" />\n"
        + "    <Property Name=\"NewName\" Value=\"[A]\" />\n  </Operation>\n</Operations>\n";

    public static TheoryData<string> Archetypes => new(Named.Keys);

    [Fact]
    [Trait("Category", "fast")]
    [Trait("Law", "3′ the read is complete")]
    public void Two_builds_of_the_proving_ground_walk_to_equal_reads_and_one_fingerprint()
    {
        var (first, second) = (walks.Reads["base"], walks.Reads["again"]);

        Assert.Equal(first, second);
        Assert.Equal(Fingerprint.Of(first.Elements), Fingerprint.Of(second.Elements));
        Assert.Contains(first.Elements, e => e.Key.ToString() == "Column [dbo].[Customer].[Email]" && e["Nullable"] == new Value.Boolean(true));
        Assert.Contains("MERGE dbo.Customer AS target", Text(first, Element.PostDeploymentScript), StringComparison.Ordinal);
        Assert.Contains("Pre-deploy: no backfill active.", Text(first, Element.PreDeploymentScript), StringComparison.Ordinal);
        Assert.DoesNotContain(first.Elements.SelectMany(e => e.Properties), p => p.Value is Value.Text { Content: var t } && t.Contains('\r', StringComparison.Ordinal));
        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"walk of the proving ground: {first.Elements.Count} elements, {first.Elements.Sum(e => e.Properties.Count)} properties, "
            + $"{first.Elements.Sum(e => e.Relationships.Sum(r => r.Targets.Count))} relationship targets, {walks.WalkTime.TotalMilliseconds:0} ms"));
    }

    [Theory]
    [Trait("Category", "fast")]
    [MemberData(nameof(Archetypes))]
    [Trait("Law", "3′ the read is complete")]
    public void Each_archetype_edit_to_the_proving_ground_changes_the_fingerprint(string archetype) =>
        Assert.NotEqual(Fingerprint.Of(walks.Reads["base"].Elements), Fingerprint.Of(walks.Reads[archetype].Elements));

    [Theory]
    [Trait("Category", "fast")]
    [MemberData(nameof(Archetypes))]
    public void Each_archetype_s_change_between_real_walks_names_exactly_what_the_archetype_edits(string archetype) =>
        Assert.Equal(Named[archetype].Order(StringComparer.Ordinal), Lines(Between("base", archetype)).Order(StringComparer.Ordinal));

    /// <summary>M1 exit 2's shape: one line, and nothing else.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void Make_mandatory_between_real_walks_is_Customer_Email_s_Nullable_true_to_false_and_nothing_else() =>
        Assert.Equal("Column [dbo].[Customer].[Email]: Nullable true → false", Assert.Single(Lines(Between("base", "make-mandatory"))));

    [Fact]
    [Trait("Category", "fast")]
    public void A_rename_with_its_refactorlog_entry_is_one_rename_and_without_the_entry_a_drop_and_an_add()
    {
        var (before, after) = (walks.Reads["base"], walks.Reads["rename a column"]);
        var phone = Key(before, "Column [dbo].[Customer].[ContactPhone]");
        var mobile = Key(after, "Column [dbo].[Customer].[MobileNumber]");

        Assert.Equal([new Rename(phone, mobile)], after.Renames);
        Assert.Equal([new Rename(phone, mobile)], Between("base", "rename a column").Renamed);
        var lost = Ok(Change.Between(before.Elements, SortedArray.Of(after.Elements.Where(e => e.Key.Type != Element.RefactorLogOperation)), []));
        Assert.Equal([mobile], lost.Created.Select(e => e.Key));
        Assert.Equal([phone], lost.Dropped.Select(e => e.Key));
    }

    /// <summary>
    /// The rename archetype beside a table dbo.AAA with a column Host and an unnamed CHECK on two columns, whose key spells the column's path
    /// ([dbo].[AAA].[Host]) and sorts before it: an entry's model.xml type pairs with the named objects only, so the column
    /// rename stays one rename and is not read as a drop and an add.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_rename_beside_an_unnamed_check_whose_key_spells_a_column_s_path_is_still_one_rename()
    {
        var (before, after) = (walks.Reads["base"], walks.Reads["rename beside a Host column"]);
        var rename = new Rename(Key(before, "Column [dbo].[Customer].[ContactPhone]"), Key(after, "Column [dbo].[Customer].[MobileNumber]"));
        var change = Between("base", "rename beside a Host column");

        Assert.Contains(after.Elements, e => e.Key.ToString() == "CheckConstraint [dbo].[AAA].[Host]");
        Assert.Contains(after.Elements, e => e.Key.ToString() == "Column [dbo].[AAA].[Host]");
        Assert.Equal([rename], after.Renames);
        Assert.Equal([rename], change.Renamed);
        Assert.Empty(change.Dropped);
    }

    /// <summary>
    /// The proving ground with two roles granted SELECT on dbo.Account, one also INSERT, and both VIEW DEFINITION on the
    /// database. A permission's name ends with its securable's name, which tells two grants on one securable nothing, so each
    /// is keyed under its securable by the name parts the securable's name does not hold: the permission, the grantee, the grantor.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_package_granting_two_roles_on_one_table_and_on_the_database_walks_to_one_key_per_grant()
    {
        var read = walks.Reads["grants"].Elements;
        var permissions = read.Where(e => e.Key.Type == "Permission").Select(e => e.Key.ToString()).ToList();
        output.WriteLine(string.Join('\n', permissions));

        Assert.Equal(5, permissions.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(read.Count, read.Select(e => e.Key).Distinct().Count());
        Assert.True(Ok(Change.Between(read, read, [])).IsEmpty);
        Assert.Contains("Permission [dbo].[Account].[Grant.Select.Object].[AppReader].[dbo]", permissions);
        Assert.Contains("Permission [DatabaseOptions].[Grant.ViewDefinition.Database].[AppWriter].[dbo]", permissions);
    }

    /// <summary>
    /// A model built in memory with grants on one table (two to one grantee, a GRANT and a DENY to another), on a schema, and
    /// on the database to two users, and an extended property on the table and on a column: each is keyed under its securable
    /// or host by the name parts that name does not hold, wherever in the name it sits, so no two share a key.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void Grants_on_a_table_a_schema_and_the_database_and_extended_properties_each_walk_to_a_key_of_their_own()
    {
        using var model = Model(
            "CREATE TABLE dbo.P (Id INT NOT NULL PRIMARY KEY, A INT NULL);", "CREATE ROLE r1;", "CREATE ROLE r2;", "CREATE USER u1 WITHOUT LOGIN;", "CREATE USER u2 WITHOUT LOGIN;",
            "GRANT SELECT ON dbo.P TO r1;", "GRANT INSERT ON dbo.P TO r1;", "GRANT SELECT ON dbo.P TO r2;", "DENY DELETE ON dbo.P TO r2;",
            "GRANT SELECT ON SCHEMA::dbo TO r1;", "GRANT EXECUTE ON SCHEMA::dbo TO r2;", "GRANT VIEW DEFINITION TO u1;", "GRANT VIEW DEFINITION TO u2;",
            "EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'P', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'P';",
            "EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'A', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'P', @level2type = N'COLUMN', @level2name = N'A';");
        var read = Ok(Ssdt.Walk(model));
        var keyed = read.Where(e => e.Key.Type is "Permission" or "ExtendedProperty").Select(e => e.Key.ToString()).ToList();
        output.WriteLine(string.Join('\n', keyed));

        Assert.Equal(10, keyed.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(read.Count, read.Select(e => e.Key).Distinct().Count());
        Assert.True(Ok(Change.Between(read, read, [])).IsEmpty);
        Assert.Superset(
            new HashSet<string>([
                "Permission [dbo].[P].[Grant.Select.Object].[r1].[dbo]", "Permission [dbo].[P].[Deny.Delete.Object].[r2].[dbo]", "Permission [dbo].[Grant.Select.Schema].[r1].[dbo]",
                "Permission [DatabaseOptions].[Grant.ViewDefinition.Database].[u2].[dbo]", "ExtendedProperty [dbo].[P].[A].[SqlColumn].[MS_Description]"]),
            keyed.ToHashSet());
    }

    /// <summary>
    /// SQL Server grants VIEW ANY COLUMN ENCRYPTION KEY DEFINITION and VIEW ANY COLUMN MASTER KEY DEFINITION to public in every new
    /// database, so a database holds them whether its project does or not. The walk leaves out those two grants to public and keeps
    /// the same permissions granted to a role, and any other grant to public.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void The_two_grants_to_public_SQL_Server_makes_in_every_database_are_left_out_and_other_grants_kept()
    {
        using var model = Model(
            "CREATE TABLE dbo.T (Id INT NOT NULL PRIMARY KEY);", "CREATE ROLE r1;", "GRANT VIEW ANY COLUMN ENCRYPTION KEY DEFINITION TO public;",
            "GRANT VIEW ANY COLUMN MASTER KEY DEFINITION TO public;", "GRANT VIEW ANY COLUMN MASTER KEY DEFINITION TO r1;", "GRANT SELECT ON dbo.T TO public;");

        var permissions = Ok(Ssdt.Walk(model)).Where(e => e.Key.Type == "Permission").Select(e => e.Key.ToString());

        Assert.Equal(["Permission [DatabaseOptions].[Grant.ViewAnyColumnMasterKeyDefinition.Database].[r1].[dbo]", "Permission [dbo].[T].[Grant.Select.Object].[public].[dbo]"], permissions);
    }

    /// <summary>
    /// Two unnamed checks on one column are keyed under it and reference the same things, so their number falls to their own
    /// values: the one whose Expression sorts first ordinally is ExpressionDependencies 1, whichever order the source declares them in.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void Unnamed_checks_on_one_column_are_numbered_by_their_own_values_whatever_order_the_source_declares_them_in()
    {
        string[] checks = ["ALTER TABLE dbo.T ADD CHECK (A > 0);", "ALTER TABLE dbo.T ADD CHECK (A < 100);"];
        using var inOrder = Model(["CREATE TABLE dbo.T (Id INT NOT NULL PRIMARY KEY, A INT NULL);", .. checks]);
        using var reversed = Model(["CREATE TABLE dbo.T (Id INT NOT NULL PRIMARY KEY, A INT NULL);", .. checks.Reverse()]);
        var (forward, backward) = (Ok(Ssdt.Walk(inOrder)), Ok(Ssdt.Walk(reversed)));

        Assert.Equal(forward, backward);
        Assert.True(Ok(Change.Between(forward, backward, [])).IsEmpty);
        Assert.Contains("A < 100", Expression(forward.Single(e => e.Key.ToString() == "CheckConstraint [dbo].[T].[A].[ExpressionDependencies 1]")), StringComparison.Ordinal);
        Assert.Contains("A > 0", Expression(forward.Single(e => e.Key.ToString() == "CheckConstraint [dbo].[T].[A].[ExpressionDependencies 2]")), StringComparison.Ordinal);
    }

    /// <summary>
    /// A model built in memory: two unnamed defaults DacFx meets in one order, then in the other. An unnamed default, or a check on
    /// one column, is keyed under that column; the primary key is keyed by its table and its relationship, never by DacFx's order
    /// or a generated name. An index key column's direction is a property of the index.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void An_unnamed_constraint_is_keyed_under_its_column_or_its_table_whatever_order_DacFx_meets_it()
    {
        using var inOrder = Model(reverse: false);
        using var reversed = Model(reverse: true);
        var (forward, backward) = (Ok(Ssdt.Walk(inOrder)), Ok(Ssdt.Walk(reversed)));

        Assert.Equal(forward, backward);
        var keys = forward.Select(e => e.Key.ToString()).ToList();
        Assert.Superset(
            new HashSet<string>(["PrimaryKeyConstraint [dbo].[T].[Host]", "CheckConstraint [dbo].[T].[B].[ExpressionDependencies]", "DefaultConstraint [dbo].[T].[A].[TargetColumn]",
                "DefaultConstraint [dbo].[T].[B].[TargetColumn]", "DatabaseOptions [DatabaseOptions]"]),
            keys.ToHashSet());
        var onA = forward.Single(e => e.Key.ToString() == "DefaultConstraint [dbo].[T].[A].[TargetColumn]");
        Assert.Equal(new Value.Text("(0)"), onA["Expression"]);
        var index = forward.Single(e => e.Key.ToString() == "Index [dbo].[T].[IX_T_A]");
        Assert.Equal((new Value.Boolean(true), new Value.Boolean(false)), (index["Columns[0].Ascending"], index["Columns[1].Ascending"]));
    }

    /// <summary>
    /// The M1 alignment review's case (2026-09-24): dbo.T (B INT DEFAULT 0, C INT DEFAULT 5) becomes (B INT DEFAULT 0, A INT DEFAULT 5)
    /// with the refactorlog's entry renaming C to A. Keyed by position, the two defaults swapped keys and read as two altered
    /// Expressions; keyed under their columns, the change is the rename and the refactorlog's new entry, nothing else.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_column_rename_beside_another_column_s_unnamed_default_is_one_rename_and_the_refactorlog_s_entry()
    {
        var before = PackageRead(null, "CREATE TABLE dbo.T (B INT DEFAULT 0, C INT DEFAULT 5);");
        var after = PackageRead(RenameCToA, "CREATE TABLE dbo.T (B INT DEFAULT 0, A INT DEFAULT 5);");

        var change = Ok(Change.Between(before.Elements, after.Elements, after.Renames));

        Assert.Equal(["renamed Column [dbo].[T].[C] to Column [dbo].[T].[A]"], change.Renamed.Select(r => "renamed " + r.Before + " to " + r.After));
        Assert.Equal(["created RefactorLogOperation [" + RenameKey + "]"], Lines(change).Where(line => !line.StartsWith("renamed ", StringComparison.Ordinal)));
    }

    /// <summary>
    /// Two unnamed checks, on B and on C, each keyed under its column; C is renamed A, which sorts before B by name. The check on
    /// the renamed column moves with it, the check on B keeps its key, and the one difference besides the rename is the moved
    /// check's own text, which names the column.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void An_unnamed_check_on_a_renamed_column_moves_with_it_and_only_its_own_text_changes()
    {
        var before = PackageRead(null, "CREATE TABLE dbo.T (B INT NULL CHECK (B > 0), C INT NULL CHECK (C > 5));");
        var after = PackageRead(RenameCToA, "CREATE TABLE dbo.T (B INT NULL CHECK (B > 0), A INT NULL CHECK (A > 5));");

        var change = Ok(Change.Between(before.Elements, after.Elements, after.Renames));

        Assert.Contains("C > 5", Expression(before.Elements.Single(e => e.Key.ToString() == "CheckConstraint [dbo].[T].[C].[ExpressionDependencies]")), StringComparison.Ordinal);
        Assert.Contains("A > 5", Expression(after.Elements.Single(e => e.Key.ToString() == "CheckConstraint [dbo].[T].[A].[ExpressionDependencies]")), StringComparison.Ordinal);
        Assert.Equal(["CheckConstraint [dbo].[T].[A].[ExpressionDependencies]: Expression"], Lines(change).Where(line => !line.StartsWith("renamed ", StringComparison.Ordinal) && !line.StartsWith("created ", StringComparison.Ordinal)));
        Assert.Single(change.Renamed);
    }

    /// <summary>
    /// The case from the review of H1's second round (2026-09-25): dbo.T (Id, B INT NULL CHECK (B &gt; 0), A INT NULL CHECK (A &gt; 5)) becomes
    /// (Id, B …, X INT NULL CHECK (X &lt; 9), A …), a column carrying an unnamed check inserted ahead of another checked column. Each
    /// check is keyed under its column, so the change is the new column, its check and the table's column list, and neither
    /// existing check reads as altered.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_checked_column_inserted_before_another_checked_column_adds_its_check_and_changes_no_other()
    {
        var before = Packaged("CREATE TABLE dbo.T (Id INT NOT NULL PRIMARY KEY, B INT NULL CHECK (B > 0), A INT NULL CHECK (A > 5));");
        var after = Packaged("CREATE TABLE dbo.T (Id INT NOT NULL PRIMARY KEY, B INT NULL CHECK (B > 0), X INT NULL CHECK (X < 9), A INT NULL CHECK (A > 5));");

        Assert.Equal(
            ["Table [dbo].[T]: Columns", "created CheckConstraint [dbo].[T].[X].[ExpressionDependencies]", "created Column [dbo].[T].[X]"],
            Lines(Ok(Change.Between(before, after, []))).Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// A model built in memory: an unnamed unique and an unnamed foreign key constraint on one column each are keyed under that
    /// column by Columns; two unnamed unique constraints and two unnamed checks on two columns each are keyed by the table and
    /// numbered in the order of the names they reference.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void Unnamed_constraints_on_one_column_are_keyed_under_it_and_those_on_several_by_the_table_in_the_order_of_their_references()
    {
        using var model = Model(
            "CREATE TABLE dbo.P (Id INT NOT NULL PRIMARY KEY);",
            "CREATE TABLE dbo.T (Id INT NOT NULL PRIMARY KEY, A INT NULL, B INT NULL, C INT NULL, PId INT NULL REFERENCES dbo.P (Id), Code INT NULL UNIQUE,"
            + " UNIQUE (C, B), UNIQUE (A, B), CHECK (C > B), CHECK (A > B));");
        var read = Ok(Ssdt.Walk(model));

        string Referenced(string key) => string.Join(", ", read.Single(e => e.Key.ToString() == key).Relationships
            .Where(r => r.Name is "Columns" or "ExpressionDependencies").SelectMany(r => r.Targets.Select(t => t.Key.Name.Base)).Order(StringComparer.Ordinal));
        Assert.Equal("Code", Referenced("UniqueConstraint [dbo].[T].[Code].[Columns]"));
        Assert.Equal("PId", Referenced("ForeignKeyConstraint [dbo].[T].[PId].[Columns]"));
        Assert.Equal(("A, B", "B, C"), (Referenced("UniqueConstraint [dbo].[T].[Host 1]"), Referenced("UniqueConstraint [dbo].[T].[Host 2]")));
        Assert.Equal(("A, B", "B, C"), (Referenced("CheckConstraint [dbo].[T].[Host 1]"), Referenced("CheckConstraint [dbo].[T].[Host 2]")));
    }

    /// <summary>
    /// A model holding one of each object that carries a secret, each planted with one value: a login's, a contained user's and an
    /// application role's password; an asymmetric key's, a certificate's (its key's and its private key file's) and a symmetric
    /// key's password; a symmetric key's KEY_SOURCE and IDENTITY_VALUE, from which SQL Server derives the key; a server credential's
    /// and a database scoped credential's secret; the database master key's password; a signature's password; a linked server
    /// login's password; a linked server's provider string (sp_addlinkedserver's @provstr) and an external data source's
    /// CONNECTION_OPTIONS, each an ODBC or OLE DB connection string carrying PWD=. DacFx gives the planted value back from every
    /// property <see cref="Ssdt.Secrets"/> lists, and the walk carries it in no property.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_secret_planted_in_every_property_that_holds_one_reaches_no_walked_property()
    {
        const string Planted = "Pl4nted!secret#7f3a";
        using var model = Model(new TSqlModelOptions { Containment = Containment.Partial },
            "CREATE TABLE dbo.T (Id INT NOT NULL PRIMARY KEY);",
            "CREATE PROCEDURE dbo.P AS SELECT 1 AS One;",
            "CREATE LOGIN L WITH PASSWORD = '" + Planted + "';",
            "CREATE USER U WITH PASSWORD = '" + Planted + "';",
            "CREATE APPLICATION ROLE AR WITH PASSWORD = '" + Planted + "';",
            "CREATE MASTER KEY ENCRYPTION BY PASSWORD = '" + Planted + "';",
            "CREATE ASYMMETRIC KEY AK WITH ALGORITHM = RSA_2048 ENCRYPTION BY PASSWORD = '" + Planted + "';",
            "CREATE CERTIFICATE C1 ENCRYPTION BY PASSWORD = '" + Planted + "' WITH SUBJECT = 'c1';",
            "CREATE CERTIFICATE C2 FROM FILE = 'c2.cer' WITH PRIVATE KEY (FILE = 'c2.pvk', DECRYPTION BY PASSWORD = '" + Planted + "', ENCRYPTION BY PASSWORD = '" + Planted + "');",
            "CREATE SYMMETRIC KEY SK WITH ALGORITHM = AES_256, KEY_SOURCE = '" + Planted + "ks', IDENTITY_VALUE = '" + Planted + "iv' ENCRYPTION BY PASSWORD = '" + Planted + "';",
            "CREATE CREDENTIAL CR WITH IDENTITY = 'i', SECRET = '" + Planted + "';",
            "CREATE DATABASE SCOPED CREDENTIAL DC WITH IDENTITY = 'i', SECRET = '" + Planted + "';",
            "CREATE EXTERNAL DATA SOURCE EDS WITH (LOCATION = 'sqlserver://remote', CONNECTION_OPTIONS = 'Server=remote;UID=u;PWD=" + Planted + "', CREDENTIAL = DC);",
            "ADD SIGNATURE TO dbo.P BY CERTIFICATE C1 WITH PASSWORD = '" + Planted + "';",
            "EXECUTE sp_addlinkedserver @server = N'LS', @srvproduct = N'', @provider = N'MSOLEDBSQL', @datasrc = N'remote', @provstr = N'UID=u;PWD=" + Planted + "';",
            "EXECUTE sp_addlinkedsrvlogin @rmtsrvname = N'LS', @useself = N'FALSE', @locallogin = NULL, @rmtuser = N'u', @rmtpassword = N'" + Planted + "';");

        var read = Ok(Ssdt.Walk(model));

        Assert.All(Ssdt.Secrets, secret => Assert.True(Held(model, secret, Planted), Qualified(secret) + " holds no planted value"));
        Assert.Contains(read, e => e.Key.ToString() == "Login [L]");
        Assert.Contains(read, e => e.Key.ToString() == "LinkedServer [LS]");
        Assert.DoesNotContain(read.SelectMany(e => e.Properties), p => p.Value is Value.Text { Content: var text } && text.Contains(Planted, StringComparison.Ordinal));
    }

    /// <summary>
    /// Every string-typed property DacFx 170.5.96's model declares, each reviewed on 2026-09-25 as a secret or as none, with the
    /// reason. A property a later DacFx adds is on neither list, and the test names it; it is reviewed and listed before the upgrade
    /// lands. The review covers string-typed properties only: a SqlScriptProperty-typed one (Parameter.DefaultExpression,
    /// ExtendedProperty.Value, Table.QueryScript and the rest) is schema text, and the walk prints it as written, as it does a
    /// module's Definition and a deploy script, until the operator's decision 2.27 on schema text that sets a password.
    /// </summary>
    private static readonly Dictionary<string, string[]> NotSecret = new(StringComparer.Ordinal)
    {
        ["a name, an address, a file or a path the definition gives, which SQL Server shows to a reader holding VIEW DEFINITION"] =
        [
            "Aggregate.ClassName", "AsymmetricKey.ExecutableFile", "AsymmetricKey.File", "AsymmetricKey.ProviderKeyName", "BrokerPriority.RemoteServiceName",
            "Certificate.ExistingKeysFilePath", "Certificate.PrivateKeyFilePath", "Certificate.Subject", "ClrTableOption.ClassName", "ClrTypeMethod.Name",
            "ClrTypeMethodParameter.Name", "ClrTypeProperty.Name", "Column.EncryptionAlgorithmName", "ColumnMasterKey.KeyPath", "ColumnMasterKey.KeyStoreProviderName",
            "Credential.Identity", "CryptographicProvider.DllPath", "DatabaseCredential.Identity", "DatabaseDdlTrigger.ClassName", "DatabaseDdlTrigger.MethodName",
            "DatabaseEventNotification.BrokerInstanceSpecifier", "DatabaseEventNotification.BrokerService", "DatabaseOptions.DefaultFullTextLanguage",
            "DatabaseOptions.DefaultLanguage", "DatabaseOptions.FileStreamDirectoryName", "DmlTrigger.ClassName", "DmlTrigger.MethodName", "ErrorMessage.Language",
            "EventSessionAction.ActionName", "EventSessionAction.EventPackageName", "EventSessionDefinitions.EventName", "EventSessionDefinitions.EventPackageName",
            "EventSessionSetting.SettingName", "EventSessionTarget.EventPackageName", "EventSessionTarget.TargetName", "ExternalDataSource.DatabaseName",
            "ExternalDataSource.Location", "ExternalDataSource.ResourceManagerLocation", "ExternalDataSource.ShardMapName", "ExternalLanguage.LanguageName",
            "ExternalLanguageFile.FileName", "ExternalLanguageFile.Path", "ExternalLanguageFile.Platform", "ExternalLibrary.Language", "ExternalLibrary.LibraryName",
            "ExternalLibraryFile.Path", "ExternalLibraryFile.Platform", "ExternalModel.LocalRuntimePath", "ExternalModel.Location", "ExternalModel.ModelNameExternal",
            "ExternalStream.Location", "ExternalTable.ExternalObjectName", "ExternalTable.ExternalSchemaName", "ExternalTable.Location", "ExternalTable.RejectedRowLocation",
            "FileTable.FileTableDirectory", "FullTextCatalog.Path", "HttpProtocolSpecifier.AuthenticationRealm", "HttpProtocolSpecifier.DefaultLogonDomain",
            "HttpProtocolSpecifier.Path", "HttpProtocolSpecifier.Website", "LinkedServer.Catalog", "LinkedServer.DataSource", "LinkedServer.Location",
            "LinkedServer.ProductName", "LinkedServer.ProviderName", "LinkedServerLogin.LinkedServerLoginName", "Login.DefaultDatabase", "Login.DefaultLanguage",
            "Procedure.ClassName", "Procedure.MethodName", "PromotedNodePathForSqlType.NodePath", "PromotedNodePathForXQueryType.NodePath",
            "PromotedNodePathForXQueryType.Type", "QueueEventNotification.BrokerInstanceSpecifier", "QueueEventNotification.BrokerService", "RemoteServiceBinding.Service",
            "Route.Address", "Route.BrokerInstance", "Route.MirrorAddress", "Route.ServiceName", "ScalarFunction.ClassName", "ScalarFunction.FillRowMethodName",
            "ScalarFunction.MethodName", "SearchProperty.Description", "ServerAudit.FilePath", "ServerAudit.Path", "ServerDdlTrigger.ClassName", "ServerDdlTrigger.MethodName",
            "ServerEventNotification.BrokerInstanceSpecifier", "ServerEventNotification.BrokerService", "SoapLanguageSpecifier.DatabaseName", "SoapLanguageSpecifier.Namespace",
            "SoapLanguageSpecifier.WsdlSpName", "SoapMethodSpecification.WebMethodAlias", "SoapMethodSpecification.WebMethodNamespace", "SqlFile.FileName",
            "SymmetricKey.ProviderKeyName", "Table.LedgerHistoryTableName", "Table.LedgerHistoryTableSchemaName", "Table.LedgerViewName",
            "Table.LedgerViewOperationTypeColumnName", "Table.LedgerViewOperationTypeDescColumnName", "Table.LedgerViewSchemaName", "Table.LedgerViewSequenceNumberColumnName",
            "Table.LedgerViewTransactionIdColumnName", "TableValuedFunction.ClassName", "TableValuedFunction.FillRowMethodName", "TableValuedFunction.MethodName",
            "TableValuedFunction.ReturnTableVariableName", "TcpProtocolSpecifier.ListenerIPv4", "TcpProtocolSpecifier.ListenerIPv6", "User.DefaultLanguage",
            "UserDefinedType.ClassName", "UserDefinedType.ValidationMethodName", "WorkloadClassifier.MemberName", "WorkloadClassifier.WlmContext",
            "WorkloadClassifier.WlmLabel", "XmlNamespace.NamespaceUri", "XmlNamespace.Prefix",
        ],
        ["a setting, a number, a date, a label or a statement the definition states, with no password or key in its documented form"] =
        [
            "Certificate.ExpiryDate", "Certificate.StartDate", "Column.Collation", "Column.IdentityIncrement", "Column.IdentitySeed", "Column.MaskingFunction",
            "Column.SensitivityInformationType", "Column.SensitivityInformationTypeId", "Column.SensitivityLabel", "Column.SensitivityLabelId",
            "ColumnEncryptionKeyValue.EncryptionAlgorithm", "DatabaseOptions.Collation", "ErrorMessage.MessageText", "ExternalFileFormat.DataCompression",
            "ExternalFileFormat.DateFormat", "ExternalFileFormat.Encoding", "ExternalFileFormat.FieldTerminator", "ExternalFileFormat.ParserVersion",
            "ExternalFileFormat.SerDeMethod", "ExternalFileFormat.StringDelimiter", "ExternalLanguageFile.EnvironmentVariables", "ExternalLanguageFile.Parameters",
            "ExternalModel.ApiFormat", "ExternalModel.Parameters", "ExternalStream.InputOptions", "ExternalStream.OutputOptions", "ExternalStreamingJob.Statement",
            "ExternalTable.TableOptions", "FileTable.FileTableCollateFilename", "LinkedServer.CollationName", "Sequence.IncrementValue", "Sequence.MaxValue",
            "Sequence.MinValue", "Sequence.StartValue", "TableTypeColumn.Collation", "TableTypeColumn.IdentityIncrement", "TableTypeColumn.IdentitySeed",
            "WorkloadClassifier.EndTime", "WorkloadClassifier.StartTime",
        ],
        ["a value SQL Server generates and shows in its catalog views: a GUID, a SID, a signature, a statistics blob, a key value encrypted by its column master key"] =
        [
            "ColumnEncryptionKeyValue.EncryptedValue", "ColumnMasterKey.Signature", "EventSessionAction.EventModuleGuid", "EventSessionDefinitions.EventModuleGuid",
            "EventSessionTarget.EventModuleGuid", "Login.Sid", "SearchProperty.PropertySetGuid", "ServerAudit.AuditGuid", "SignatureEncryptionMechanism.SignedBlob",
            "Statistics.StatsStream", "User.Sid",
        ],
    };

    [Fact]
    [Trait("Category", "fast")]
    public void Every_text_property_DacFx_declares_is_a_listed_secret_or_reviewed_as_not_a_secret()
    {
        var reviewed = NotSecret.Values.SelectMany(names => names).ToList();
        var declared = typeof(ModelSchema).GetFields(BindingFlags.Public | BindingFlags.Static).Select(f => f.GetValue(null)).OfType<ModelTypeClass>()
            .SelectMany(t => t.Properties.Concat(t.Relationships.SelectMany(r => r.Properties))).Where(p => p.DataType == typeof(string)).Distinct().ToList();

        Assert.Empty(declared.Where(p => !Ssdt.Secrets.Contains(p) && !reviewed.Contains(Qualified(p))).Select(Qualified));
        Assert.Empty(reviewed.Except(declared.Select(Qualified)));
        Assert.Empty(reviewed.Intersect(Ssdt.Secrets.Select(Qualified)));
        Assert.Equal(reviewed.Count, reviewed.Distinct().Count());
    }

    /// <summary>A property as Type.Property, a relationship's as Relationship.Property.</summary>
    private static string Qualified(ModelPropertyClass p) => (p.OwningType?.Name ?? p.OwningRelationship?.Name) + "." + p.Name;

    /// <summary>
    /// A security policy composes its predicates, and DacFx also lists each predicate among the top-level objects: the walk
    /// reads an object once however many paths reach it, so the read is whole and each key names one object.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_security_policy_s_predicates_which_two_paths_reach_are_each_read_once()
    {
        using var model = Model(
            "CREATE TABLE dbo.T (Id INT NOT NULL PRIMARY KEY, Owner INT NULL);",
            "CREATE FUNCTION dbo.fn(@Owner INT) RETURNS TABLE WITH SCHEMABINDING AS RETURN SELECT 1 AS ok WHERE @Owner = 1;",
            "CREATE SECURITY POLICY dbo.SP ADD FILTER PREDICATE dbo.fn(Owner) ON dbo.T, ADD BLOCK PREDICATE dbo.fn(Owner) ON dbo.T AFTER INSERT;");
        var read = Ok(Ssdt.Walk(model));
        var predicates = read.Where(e => e.Key.Type == "SecurityPredicate").Select(e => e.Key.ToString()).ToList();
        output.WriteLine(string.Join('\n', predicates));

        Assert.Equal(read.Count, read.Select(e => e.Key).Distinct().Count());
        Assert.Equal(["SecurityPredicate [dbo].[SP].[Predicates 1]", "SecurityPredicate [dbo].[SP].[Predicates 2]"], predicates);
        Assert.True(Ok(Change.Between(read, read, [])).IsEmpty);
    }

    /// <summary>
    /// DacFx declares no property holding a procedure's, a trigger's or a function's body, so the walk reads each such module's
    /// Definition, the script DacFx gives it; a view's body is its SelectStatement property and is read once, there. An edit to
    /// the body alone, walked from a package on each side, changes the fingerprint and is that one property.
    /// </summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("Procedure [dbo].[P]", "Definition", "CREATE PROCEDURE dbo.P AS SELECT {0} AS One;")]
    [InlineData("DmlTrigger [dbo].[TR]", "Definition", "CREATE TRIGGER dbo.TR ON dbo.T AFTER INSERT AS SELECT {0} AS One;")]
    [InlineData("DatabaseDdlTrigger [TD]", "Definition", "CREATE TRIGGER TD ON DATABASE FOR CREATE_TABLE AS SELECT {0} AS One;")]
    [InlineData("ScalarFunction [dbo].[S]", "Definition", "CREATE FUNCTION dbo.S() RETURNS INT AS BEGIN RETURN {0}; END")]
    [InlineData("TableValuedFunction [dbo].[F]", "Definition", "CREATE FUNCTION dbo.F() RETURNS TABLE AS RETURN SELECT {0} AS One;")]
    [InlineData("View [dbo].[V]", "SelectStatement", "CREATE VIEW dbo.V AS SELECT {0} AS One;")]
    public void A_module_s_body_edit_changes_the_fingerprint_and_is_one_property_of_the_module(string key, string property, string module)
    {
        const string table = "CREATE TABLE dbo.T (Id INT NOT NULL PRIMARY KEY);";
        var (before, after) = (Packaged(table, string.Format(CultureInfo.InvariantCulture, module, 1)), Packaged(table, string.Format(CultureInfo.InvariantCulture, module, 2)));

        Assert.NotEqual(Fingerprint.Of(before), Fingerprint.Of(after));
        Assert.Equal([key + ": " + property], Lines(Ok(Change.Between(before, after, []))));
        Assert.Contains(" 2", ((Value.Text)after.Single(e => e.Key.ToString() == key)[property]!).Content, StringComparison.Ordinal);
        Assert.DoesNotContain(after, e => e.Key.Type == "Table" && e["Definition"] is not null);
    }

    /// <summary>
    /// Two packages whose procedures differ only in RAISERROR's message, 'The user''s password has expired' against 'was reset':
    /// the walk reads each module's Definition as DacFx gives it, so the change between them is that one Definition, and each
    /// walked Definition is its procedure's text as written. A walk that rewrote text holding the word password would read both
    /// messages alike and give an empty change.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    [Trait("Law", "3′ the read is complete")]
    public void Procedures_differing_only_in_a_message_that_names_a_password_walk_as_written_and_differ_in_their_Definition()
    {
        const string Expired = "CREATE PROCEDURE dbo.P AS RAISERROR('The user''s password has expired', 16, 1);";
        const string Reset = "CREATE PROCEDURE dbo.P AS RAISERROR('The user''s password was reset', 16, 1);";
        var (before, after) = (Packaged(Expired), Packaged(Reset));

        Assert.Equal(["Procedure [dbo].[P]: Definition"], Lines(Ok(Change.Between(before, after, []))));
        Assert.Equal(new Value.Text(Expired), before.Single(e => e.Key.ToString() == "Procedure [dbo].[P]")["Definition"]);
        Assert.Equal(new Value.Text(Reset), after.Single(e => e.Key.ToString() == "Procedure [dbo].[P]")["Definition"]);
    }

    /// <summary>
    /// The proving ground with a table of unnamed inline constraints, two of them checks on one column, and a procedure over it,
    /// published to a registered copy and read back through SqlServer.Model: the database walk keys every object as the package
    /// walk does, though SQL Server named each constraint, and each unnamed key names the same constraint in both (the same
    /// targets; the tied checks the same text once SQL Server's brackets and parentheses are set aside). Their values differ (SQL
    /// Server stores a check's text as it normalized it), which is why walk fingerprints are compared only between like sources.
    /// The procedure's Definition reads alike from both: SQL Server keeps a module's text as the publish sent it.
    /// </summary>
    [Fact]
    [Trait("Category", "fixture")]
    public async Task A_package_and_the_database_it_was_published_to_key_every_object_alike_unnamed_constraints_included()
    {
        var package = walks.Reads["unnamed constraints"].Elements
            .Where(e => e.Key.Type is not (Element.PreDeploymentScript or Element.PostDeploymentScript or Element.RefactorLogOperation)).ToList();
        var database = await PublishedAndRead(walks.Dacpacs["unnamed constraints"]);

        Assert.Contains(package, e => e.Key.ToString() == "DefaultConstraint [dbo].[Note].[Pinned].[TargetColumn]");
        Assert.Equal(package.Select(e => e.Key), database.Select(e => e.Key));
        var unnamed = package.Where(e => e.Key.Name.Base.Split(' ')[0] is "Host" or "TargetColumn" or "ExpressionDependencies" or "Columns").ToList();
        Assert.Equal(8, unnamed.Count(e => e.Key.Path.StartsWith("[dbo].[Note].", StringComparison.Ordinal)));
        Assert.All(unnamed, e => Assert.Equal(e.Relationships, database.Single(d => d.Key == e.Key).Relationships));
        Assert.All(["CheckConstraint [dbo].[Note].[Score].[ExpressionDependencies 1]", "CheckConstraint [dbo].[Note].[Score].[ExpressionDependencies 2]"], (string key) =>
            Assert.Equal(Bare(Expression(package.Single(e => e.Key.ToString() == key))), Bare(Expression(database.Single(e => e.Key.ToString() == key)))));
        var check = package.Single(e => e.Key.ToString() == "CheckConstraint [dbo].[Note].[Pinned].[ExpressionDependencies]");
        Assert.NotEqual(check["Expression"], database.Single(e => e.Key == check.Key)["Expression"]);
        var procedure = package.Single(e => e.Key.ToString() == "Procedure [dbo].[NoteCount]");
        Assert.Contains("WHERE CustomerId = @CustomerId", Assert.IsType<Value.Text>(procedure["Definition"]).Content, StringComparison.Ordinal);
        Assert.Equal(procedure["Definition"], database.Single(e => e.Key == procedure.Key)["Definition"]);
    }

    /// <summary>
    /// The grants head (two roles granted SELECT on dbo.Account, one also INSERT, and both VIEW DEFINITION on the database)
    /// published to a registered copy and read back through SqlServer.Model holds the Permission keys the package's walk holds.
    /// DacFx 170.5.96's LoadFromDatabase leaves every permission out unless ModelExtractOptions.IgnorePermissions is set false.
    /// </summary>
    [Fact]
    [Trait("Category", "fixture")]
    public async Task A_package_s_grants_published_to_a_copy_read_back_as_the_same_Permission_keys()
    {
        var database = await PublishedAndRead(walks.Dacpacs["grants"]);

        var (packaged, read) = (Permissions(walks.Reads["grants"].Elements), Permissions(database));
        output.WriteLine(string.Join('\n', read));

        Assert.Contains("Permission [dbo].[Account].[Grant.Select.Object].[AppReader].[dbo]", packaged);
        Assert.Equal(packaged, read);
    }

    /// <summary>
    /// The case from the review of H1's first round (2026-09-25): dbo.T (Id, B INT NULL CHECK (B &gt; 0), A INT NULL CHECK (A &gt; 5)) packaged with
    /// the refactorlog's entry renaming C to A, published to a registered copy and read back through SqlServer.Model. A database
    /// has no refactorlog, so each unnamed check's key has to follow from what both reads hold: each CheckConstraint key names the
    /// check on the same column in the package's walk and in the database's walk.
    /// </summary>
    [Fact]
    [Trait("Category", "fixture")]
    public async Task Unnamed_checks_beside_a_renamed_column_are_keyed_alike_in_a_package_and_the_database_it_was_published_to()
    {
        var dacpac = Built(RenameCToA, "CREATE TABLE dbo.T (Id INT NOT NULL PRIMARY KEY, B INT NULL CHECK (B > 0), A INT NULL CHECK (A > 5));");
        try
        {
            SortedArray<Element> package;
            using (var loaded = Ok(Ssdt.Load(dacpac)))
            {
                package = Ok(Ssdt.Walk(loaded)).Elements;
            }

            var database = await PublishedAndRead(new DacDeployOptions(), dacpac);

            output.WriteLine(Checked(database));
            Assert.Contains("CheckConstraint [dbo].[T].[A].[ExpressionDependencies] → Column [dbo].[T].[A]", Checked(package), StringComparison.Ordinal);
            Assert.Equal(Checked(package), Checked(database));
        }
        finally
        {
            Delete(dacpac);
        }
    }

    /// <summary>
    /// The two cases from the review of H1's second round (2026-09-25): a first package published to a registered database, then a second
    /// that inserts a column carrying an unnamed check ahead of a checked column, published over it under the golden pipeline
    /// profile's deploy options. That profile sets IgnoreColumnOrder, so DacFx appends the new column and the database holds its
    /// columns in another order than the second package. Each CheckConstraint key names the check on the same column in the
    /// second package's walk and in the database's walk.
    /// </summary>
    [Theory]
    [Trait("Category", "fixture")]
    [InlineData("CREATE TABLE dbo.T (Id INT NOT NULL PRIMARY KEY, B INT NULL CHECK (B > 0));",
        "CREATE TABLE dbo.T (Id INT NOT NULL PRIMARY KEY, A INT NULL CHECK (A > 5), B INT NULL CHECK (B > 0));")]
    [InlineData("CREATE TABLE dbo.T (Id INT NOT NULL PRIMARY KEY, B INT NULL CHECK (B > 0), A INT NULL CHECK (A > 5));",
        "CREATE TABLE dbo.T (Id INT NOT NULL PRIMARY KEY, B INT NULL CHECK (B > 0), X INT NULL CHECK (X < 9), A INT NULL CHECK (A > 5));")]
    public async Task Unnamed_checks_are_keyed_alike_in_a_package_and_a_database_that_holds_its_columns_in_another_order(string first, string second)
    {
        var (v1, v2) = (Built(null, first), Built(null, second));
        try
        {
            SortedArray<Element> package;
            using (var loaded = Ok(Ssdt.Load(v2)))
            {
                package = Ok(Ssdt.Walk(loaded)).Elements;
            }

            var profile = DacProfile.Load(Path.Combine(Repository.Root, "tests", "Golden", "proving-ground", "profiles", "pipeline.publish.xml")).DeployOptions;
            Assert.True(profile.IgnoreColumnOrder);
            var database = await PublishedAndRead(profile, v1, v2);

            var columns = database.Single(e => e.Key.ToString() == "Table [dbo].[T]").Relationships.Single(r => r.Name == "Columns").Targets.Select(t => t.Key.Name.Base);
            Assert.NotEqual(package.Single(e => e.Key.ToString() == "Table [dbo].[T]").Relationships.Single(r => r.Name == "Columns").Targets.Select(t => t.Key.Name.Base), columns);
            output.WriteLine(Checked(database));
            Assert.Equal(Checked(package), Checked(database));
        }
        finally
        {
            Delete(v1);
            Delete(v2);
        }
    }

    /// <summary>Each CheckConstraint key of a read, with the keys of the objects its ExpressionDependencies names, one line each in key order.</summary>
    private static string Checked(IEnumerable<Element> read) => string.Join('\n', read.Where(e => e.Key.Type == "CheckConstraint").OrderBy(e => e.Key.ToString(), StringComparer.Ordinal)
        .Select(e => e.Key + " → " + string.Join(", ", e.Relationships.Where(r => r.Name == "ExpressionDependencies").SelectMany(r => r.Targets.Select(t => t.Key)))));

    private static List<string> Permissions(IEnumerable<Element> read) => [.. read.Where(e => e.Key.Type == "Permission").Select(e => e.Key.ToString())];

    /// <summary>A head's package published to a registered database under DacFx's default deploy options, then read back as io reads a copy; the database is dropped after.</summary>
    private static Task<SortedArray<Element>> PublishedAndRead(string dacpac) => PublishedAndRead(new DacDeployOptions(), dacpac);

    /// <summary>Packages published in turn to one registered database under the deploy options, then read back as io reads a copy; the database is dropped after.</summary>
    private static async Task<SortedArray<Element>> PublishedAndRead(DacDeployOptions options, params string[] dacpacs)
    {
        await using var database = await SqlServerFixture.RegisterAsync();
        foreach (var dacpac in dacpacs)
        {
            ProvingGround.Publish(dacpac, database, options);
        }

        return Ok(SqlServer.Model(new SqlServer.Copy(database.Name, await SqlServerFixture.ServerAsync(), Repository.Root)));
    }

    [Fact]
    [Trait("Category", "fast")]
    public void A_value_reads_as_the_kernel_s_closed_cases_a_double_as_its_invariant_string_and_a_property_DacFx_cannot_read_is_skipped()
    {
        using var model = Model(reverse: false);
        var table = model.GetObject(Table.TypeClass, new ObjectIdentifier("dbo", "T"), DacQueryScopes.UserDefined);
        var c = table.GetReferenced(Table.Columns).Single(column => column.Name.Parts[^1] == "C");
        var spatial = model.GetObjects(DacQueryScopes.UserDefined, SpatialIndex.TypeClass).Single();

        Assert.Equal(new Value.Boolean(true), Ssdt.ValueOf(c, Column.Nullable));
        Assert.Equal(new Value.Integer(10), Ssdt.ValueOf(c, Column.Length));
        Assert.Equal(new Value.Null(), Ssdt.ValueOf(c, Column.Collation));
        Assert.Equal(new Value.Enumeration("Durability", "SchemaAndData"), Ssdt.ValueOf(table, Table.Durability));
        Assert.Equal(new Value.Text("10.5"), Ssdt.ValueOf(spatial, SpatialIndex.XMax));
        Assert.Null(Ssdt.ValueOf(table, Column.Nullable));
    }

    private static TSqlModel Model(bool reverse)
    {
        string[] defaults = ["ALTER TABLE dbo.T ADD DEFAULT (0) FOR A;", "ALTER TABLE dbo.T ADD DEFAULT (5) FOR B;"];
        return Model(
        [
            "CREATE TABLE dbo.T (Id INT NOT NULL PRIMARY KEY, A INT NOT NULL, B INT NULL CHECK (B > 0), C NVARCHAR(10) NULL);",
            .. reverse ? defaults.Reverse() : defaults,
            "CREATE INDEX IX_T_A ON dbo.T (A, B DESC);",
            "CREATE TABLE dbo.S (Id INT NOT NULL CONSTRAINT PK_S PRIMARY KEY, G GEOMETRY NULL);",
            "CREATE SPATIAL INDEX SX ON dbo.S (G) WITH (BOUNDING_BOX = (0, 0, 10.5, 10));",
        ]);
    }

    /// <summary>A model built in memory from the scripts, each added in turn.</summary>
    private static TSqlModel Model(params string[] scripts) => Model(new TSqlModelOptions(), scripts);

    private static TSqlModel Model(TSqlModelOptions options, params string[] scripts)
    {
        var model = new TSqlModel(SqlServerVersion.Sql160, options);
        foreach (var script in scripts)
        {
            model.AddObjects(script);
        }

        return model;
    }

    /// <summary>A model built in memory from the scripts, packaged by DacFx, then loaded as Load loads a build's package and walked.</summary>
    private static SortedArray<Element> Packaged(params string[] scripts) => PackageRead(null, scripts).Elements;

    /// <summary>The scripts packaged by DacFx with the refactorlog's text as the package's refactor.xml, where one is given, then loaded as Load loads a build's package and walked.</summary>
    private static Ssdt.Read PackageRead(string? refactorlog, params string[] scripts)
    {
        var dacpac = Built(refactorlog, scripts);
        try
        {
            using var package = Ok(Ssdt.Load(dacpac));
            return Ok(Ssdt.Walk(package));
        }
        finally
        {
            Delete(dacpac);
        }
    }

    /// <summary>The scripts packaged by DacFx under the temp folder, with the refactorlog's text as the package's refactor.xml where one is given; the caller deletes it with <see cref="Delete"/>.</summary>
    private static string Built(string? refactorlog, params string[] scripts)
    {
        var path = Path.Combine(Path.GetTempPath(), "estate-walk-" + Guid.NewGuid().ToString("N"));
        if (refactorlog is not null)
        {
            File.WriteAllText(path + ".refactorlog", refactorlog);
        }

        using var model = Model(scripts);
        DacPackageExtensions.BuildPackage(path + ".dacpac", model, new PackageMetadata(), new PackageOptions { RefactorLogPath = refactorlog is null ? null : path + ".refactorlog" });
        return path + ".dacpac";
    }

    private static void Delete(string dacpac)
    {
        File.Delete(dacpac);
        File.Delete(Path.ChangeExtension(dacpac, ".refactorlog"));
    }

    /// <summary>Whether an object of the secret's type, top-level or composed by another (a symmetric key's password, a signature), gives back a value holding the planted one.</summary>
    private static bool Held(TSqlModel model, ModelPropertyClass secret, string planted) => model.GetObjects(DacQueryScopes.UserDefined).SelectMany(Composed)
        .Any(o => o.ObjectType == secret.OwningType && o.GetProperty(secret) is string value && value.Contains(planted, StringComparison.Ordinal));

    private static IEnumerable<TSqlObject> Composed(TSqlObject o) =>
        [o, .. o.ObjectType.Relationships.Where(r => r.Type == RelationshipType.Composing).SelectMany(r => o.GetReferenced(r, DacQueryScopes.All)).SelectMany(Composed)];

    private static string Expression(Element check) => ((Value.Text)check["Expression"]!).Content;

    /// <summary>A check's text with the brackets, parentheses and spaces SQL Server's normalization adds set aside: <c>([A]&lt;(100))</c> as <c>A&lt;100</c>.</summary>
    private static string Bare(string expression) => new([.. expression.Where(c => c is not ('[' or ']' or '(' or ')' or ' '))]);

    private Change Between(string before, string after) =>
        Ok(Change.Between(walks.Reads[before].Elements, walks.Reads[after].Elements, walks.Reads[after].Renames));

    /// <summary>A change as lines: each element created, dropped or renamed, and each property (with its values, a script's text left out) or relationship that is altered.</summary>
    private static IEnumerable<string> Lines(Change change) =>
        change.Created.Select(e => "created " + e.Key)
            .Concat(change.Dropped.Select(e => "dropped " + e.Key))
            .Concat(change.Renamed.Select(r => "renamed " + r.Before + " to " + r.After))
            .Concat(change.Altered.SelectMany(a => a.Properties
                .Select(p => a.Key + ": " + p.Name + (p.Before is Value.Text || p.After is Value.Text ? "" : " " + p.Before + " → " + p.After))
                .Concat(a.Relationships.Select(r => a.Key + ": " + r.Name))));

    private static ElementKey Key(Ssdt.Read read, string key) => read.Elements.Single(e => e.Key.ToString() == key).Key;

    private static string Text(Ssdt.Read read, string type) => ((Value.Text)read.Elements.Single(e => e.Key.Type == type)["Text"]!).Content;

    private static T Ok<T>(Result<T> result) => result.Match(value => value, error => throw new Xunit.Sdk.XunitException(error.Code + ": " + error.Message));
}

/// <summary>
/// The proving ground built twice from two copies, and once per head from a copy with the head's edits, all against
/// dist/estate/ and in parallel, each loaded and walked; then the base walked again, alone, for the walk's time. The tree
/// under .estate/walk/ is dropped after.
/// </summary>
public sealed class ProvingGroundWalks : IAsyncLifetime
{
    public const string RenameKey = "6d1c1b5e-3f0a-4c2e-9b7d-2a4f8e6c0d13";

    /// <summary>The rename archetype: Customer.ContactPhone renamed MobileNumber, with the refactorlog entry SSDT writes for it.</summary>
    private static readonly (string File, string From, string To)[] RenameEdits =
    [
        ("Modules/Customer.sql", "ContactPhone    NVARCHAR(40)    NULL,", "MobileNumber    NVARCHAR(40)    NULL,"),
        ("SampleCatalog.refactorlog", "</Operations>",
            "  <Operation Name=\"Rename Refactor\" Key=\"" + RenameKey + "\" ChangeDateTime=\"09/24/2026 10:00:00\">\n"
            + "    <Property Name=\"ElementName\" Value=\"[dbo].[Customer].[ContactPhone]\" />\n"
            + "    <Property Name=\"ElementType\" Value=\"SqlSimpleColumn\" />\n"
            + "    <Property Name=\"ParentElementName\" Value=\"[dbo].[Customer]\" />\n"
            + "    <Property Name=\"ParentElementType\" Value=\"SqlTable\" />\n"
            + "    <Property Name=\"NewName\" Value=\"[MobileNumber]\" />\n"
            + "  </Operation>\n</Operations>"),
    ];

    /// <summary>Each head's edits: a file, text that occurs in it exactly once, and its replacement.</summary>
    private static readonly Dictionary<string, (string File, string From, string To)[]> Edits = new()
    {
        ["base"] = [],
        ["again"] = [],
        ["make-mandatory"] = [("Modules/Customer.sql", "Email           NVARCHAR(256)   NULL,", "Email           NVARCHAR(256)   NOT NULL,")],
        ["add a nullable column"] = [("Modules/Customer.sql", "AccountId       INT             NULL,", "AccountId       INT             NULL,\n    Nickname        NVARCHAR(40)    NULL,")],
        ["drop a column"] = [("Modules/Product.sql", "LegacyCode NVARCHAR(40)  NOT NULL CONSTRAINT DF_Product_LegacyCode DEFAULT (N'LEGACY'),", "")],
        ["widen a column"] = [("Modules/Product.sql", "Code    NVARCHAR(50)    NOT NULL,", "Code    NVARCHAR(100)   NOT NULL,")],
        ["add a check constraint"] = [("Modules/Product.sql", "CONSTRAINT PK_Product_Id PRIMARY KEY CLUSTERED (Id)",
            "CONSTRAINT PK_Product_Id PRIMARY KEY CLUSTERED (Id),\n    CONSTRAINT CK_Product_Code CHECK (LEN(Code) > 0)")],
        ["add a foreign key"] = [("Modules/Order.sql", "CONSTRAINT PK_Order_Id PRIMARY KEY CLUSTERED (Id)",
            "CONSTRAINT PK_Order_Id PRIMARY KEY CLUSTERED (Id),\n    CONSTRAINT FK_Order_Customer_CustomerId FOREIGN KEY (CustomerId) REFERENCES dbo.Customer (Id)")],
        ["a seed edit"] = [("Data/Seed.sql", "(3, N'Initech',", "(3, N'Initech Ltd',")],
        ["a pre-deploy edit"] = [("Script.PreDeployment.sql", "PRINT 'Pre-deploy: no backfill active.", "PRINT 'Pre-deploy: still no backfill active.")],
        ["rename a column"] = RenameEdits,
        ["unnamed constraints"] = [("Modules/OrderStatusText.sql", "-- Intentionally no schema object. The column lives in Modules/Order.sql.",
            "CREATE TABLE dbo.Note (Id INT NOT NULL PRIMARY KEY, CustomerId INT NULL REFERENCES dbo.Customer (Id), Body NVARCHAR(200) NOT NULL DEFAULT (N''),"
            + " Pinned BIT NOT NULL DEFAULT (0) CHECK (Pinned IN (0, 1)), Code NVARCHAR(10) NULL UNIQUE, Score INT NULL, CHECK (Score > 0), CHECK (Score < 100));"
            + "\nGO\nCREATE PROCEDURE dbo.NoteCount @CustomerId INT\nAS\n    SELECT COUNT_BIG(*) AS Notes FROM dbo.Note WHERE CustomerId = @CustomerId;")],
        ["rename beside a Host column"] =
        [
            .. RenameEdits,
            ("Modules/OrderStatusText.sql", "-- Intentionally no schema object. The column lives in Modules/Order.sql.", "CREATE TABLE dbo.AAA (Id INT NOT NULL, Host INT NULL, CHECK (Id > 0 OR Host > 0));"),
        ],
        ["grants"] = [("Modules/OrderStatusText.sql", "-- Intentionally no schema object. The column lives in Modules/Order.sql.", string.Join("\nGO\n",
            "CREATE ROLE AppReader;", "CREATE ROLE AppWriter;", "GRANT SELECT ON dbo.Account TO AppReader;", "GRANT SELECT ON dbo.Account TO AppWriter;",
            "GRANT INSERT ON dbo.Account TO AppWriter;", "GRANT VIEW DEFINITION TO AppReader;", "GRANT VIEW DEFINITION TO AppWriter;"))],
    };

    private readonly string root = Path.Combine(Repository.Root, ".estate", "walk", Environment.ProcessId + "-" + Guid.NewGuid().ToString("N")[..8]);

    public Dictionary<string, Ssdt.Read> Reads { get; } = [];

    public Dictionary<string, string> Dacpacs { get; } = [];

    public TimeSpan WalkTime { get; private set; }

    public async Task InitializeAsync()
    {
        var tool = new PublishedTool();
        var golden = Path.Combine(Repository.Root, "tests", "Golden");
        Directory.CreateDirectory(root);
        foreach (var stop in (string[])["Directory.Build.props", "Directory.Packages.props"])
        {
            File.Copy(Path.Combine(golden, stop), Path.Combine(root, stop));
        }

        var reads = await Task.WhenAll(Edits.Select(head => Task.Run(() =>
        {
            var directory = Path.Combine(root, "heads", head.Key.Replace(' ', '-'));
            ToolFolderTests.Copy(Path.Combine(golden, "proving-ground"), directory);
            foreach (var (file, from, to) in head.Value)
            {
                var path = Path.Combine(directory, file);
                var text = File.ReadAllText(path);
                Assert.True(text.Split(from).Length == 2, path + " does not hold exactly one '" + from + "'");
                File.WriteAllText(path, text.Replace(from, to, StringComparison.Ordinal));
            }

            var dacpac = Ok(Ssdt.Build(Path.Combine(directory, "SampleCatalog.sqlproj"), tool.Folder, Path.Combine(root, "build", head.Key.Replace(' ', '-'))));
            using var package = Ok(Ssdt.Load(dacpac.Path));
            return (Head: head.Key, Dacpac: dacpac.Path, Read: Ok(Ssdt.Walk(package)));
        })));
        foreach (var (head, dacpac, read) in reads)
        {
            (Reads[head], Dacpacs[head]) = (read, dacpac);
        }

        // The base walked once more, alone and warm, for its time.
        using var again = Ok(Ssdt.Load(Dacpacs["base"]));
        var clock = Stopwatch.StartNew();
        Assert.Equal(Reads["base"], Ok(Ssdt.Walk(again)));
        WalkTime = clock.Elapsed;
    }

    public Task DisposeAsync()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }

        return Task.CompletedTask;
    }

    private static T Ok<T>(Result<T> result) => result.Match(value => value, error => throw new Xunit.Sdk.XunitException(error.Code + ": " + error.Message));
}
