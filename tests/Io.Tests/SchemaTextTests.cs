using System;
using System.Collections.Generic;
using System.Linq;
using Estate.Kernel;
using Estate.Tests;
using Xunit;

namespace Estate.Io.Tests;

/// <summary>
/// io/SchemaText, the printer schema text passes through before it leaves the tool (decision 2.27, VALUES.md X2): each value a known
/// password form sets is printed as left out and the form is named; a SQLCMD variable in a value's place is no literal and names no
/// form; text ScriptDom cannot parse is left out whole, with where the parse stopped; an expression prints as written.
/// </summary>
public sealed class SchemaTextTests
{
    private const string Planted = PlantedValue.PasswordText;

    /// <summary>One sample script per form, the planted value where the form sets a password; a form without a sample fails the theory.</summary>
    private static readonly Dictionary<string, string> Samples = new(StringComparer.Ordinal)
    {
        ["CreateLogin"] = "CREATE LOGIN [svc] WITH PASSWORD = '" + Planted + "', DEFAULT_DATABASE = [master];",
        ["AlterLogin"] = "ALTER LOGIN [svc] WITH PASSWORD = '" + Planted + "' OLD_PASSWORD = '" + Planted + "';",
        ["CreateUser"] = "CREATE USER [app] WITH PASSWORD = '" + Planted + "';",
        ["AlterUser"] = "ALTER USER [app] WITH PASSWORD = '" + Planted + "' OLD_PASSWORD = '" + Planted + "';",
        ["CreateApplicationRole"] = "CREATE APPLICATION ROLE [ar] WITH PASSWORD = '" + Planted + "', DEFAULT_SCHEMA = [dbo];",
        ["AlterApplicationRole"] = "ALTER APPLICATION ROLE [ar] WITH PASSWORD = '" + Planted + "';",
        ["CreateMasterKey"] = "CREATE MASTER KEY ENCRYPTION BY PASSWORD = '" + Planted + "';",
        ["AlterMasterKey"] = "ALTER MASTER KEY REGENERATE WITH ENCRYPTION BY PASSWORD = '" + Planted + "';",
        ["OpenMasterKey"] = "OPEN MASTER KEY DECRYPTION BY PASSWORD = '" + Planted + "';",
        ["BackupMasterKey"] = "BACKUP MASTER KEY TO FILE = 'c:\\keys\\master.key' ENCRYPTION BY PASSWORD = '" + Planted + "';",
        ["RestoreMasterKey"] = "RESTORE MASTER KEY FROM FILE = 'c:\\keys\\master.key' DECRYPTION BY PASSWORD = '" + Planted + "' ENCRYPTION BY PASSWORD = '" + Planted + "';",
        ["CreateAsymmetricKey"] = "CREATE ASYMMETRIC KEY [ak] WITH ALGORITHM = RSA_2048 ENCRYPTION BY PASSWORD = '" + Planted + "';",
        ["AlterAsymmetricKey"] = "ALTER ASYMMETRIC KEY [ak] WITH PRIVATE KEY (ENCRYPTION BY PASSWORD = '" + Planted + "');",
        ["CreateCertificate"] = "CREATE CERTIFICATE [c1] FROM FILE = 'c:\\keys\\c1.cer' WITH PRIVATE KEY (FILE = 'c:\\keys\\c1.pvk', DECRYPTION BY PASSWORD = '" + Planted + "');",
        ["AlterCertificate"] = "ALTER CERTIFICATE [c1] WITH PRIVATE KEY (ENCRYPTION BY PASSWORD = '" + Planted + "');",
        ["BackupCertificate"] = "BACKUP CERTIFICATE [c1] TO FILE = 'c:\\keys\\c1.cer' WITH PRIVATE KEY (FILE = 'c:\\keys\\c1.pvk', ENCRYPTION BY PASSWORD = '" + Planted + "');",
        ["CreateSymmetricKey"] = "CREATE SYMMETRIC KEY [sk] WITH ALGORITHM = AES_256, KEY_SOURCE = '" + Planted + "', IDENTITY_VALUE = '" + Planted + "' ENCRYPTION BY PASSWORD = '" + Planted + "';",
        ["AlterSymmetricKey"] = "ALTER SYMMETRIC KEY [sk] ADD ENCRYPTION BY PASSWORD = '" + Planted + "';",
        ["OpenSymmetricKey"] = "OPEN SYMMETRIC KEY [sk] DECRYPTION BY PASSWORD = '" + Planted + "';",
        ["AddSignature"] = "ADD SIGNATURE TO dbo.P BY CERTIFICATE [c1] WITH PASSWORD = '" + Planted + "';",
        ["AddCounterSignature"] = "ADD COUNTER SIGNATURE TO dbo.P BY CERTIFICATE [c1] WITH PASSWORD = '" + Planted + "';",
        ["CreateCredential"] = "CREATE CREDENTIAL [cr] WITH IDENTITY = 'svc', SECRET = '" + Planted + "';",
        ["AlterCredential"] = "ALTER CREDENTIAL [cr] WITH IDENTITY = 'svc', SECRET = '" + Planted + "';",
        ["CreateDatabaseScopedCredential"] = "CREATE DATABASE SCOPED CREDENTIAL [dc] WITH IDENTITY = 'svc', SECRET = '" + Planted + "';",
        ["AlterDatabaseScopedCredential"] = "ALTER DATABASE SCOPED CREDENTIAL [dc] WITH IDENTITY = 'svc', SECRET = '" + Planted + "';",
        ["CreateExternalDataSource"] = "CREATE EXTERNAL DATA SOURCE [eds] WITH (LOCATION = 'sqlserver://remote', CONNECTION_OPTIONS = 'Server=remote;UID=u;PWD=" + Planted + "', CREDENTIAL = [dc]);",
        ["Restore"] = "RESTORE DATABASE [Estate] FROM DISK = 'c:\\b\\estate.bak' WITH PASSWORD = '" + Planted + "', MEDIAPASSWORD = '" + Planted + "';",
        ["OpenRowset"] = "SELECT a.* FROM OPENROWSET('MSOLEDBSQL', 'remote'; 'u'; '" + Planted + "', 'SELECT 1') AS a;",
        ["OpenDataSource"] = "SELECT a.* FROM OPENDATASOURCE('MSOLEDBSQL', 'Data Source=remote;User ID=u;Password=" + Planted + "').Estate.dbo.T AS a;",
        ["SpPassword"] = "EXEC sp_password '" + Planted + "', '" + Planted + "', 'svc';",
        ["SpAddLogin"] = "EXEC sp_addlogin 'svc', '" + Planted + "', 'master';",
        ["SpAppRolePassword"] = "EXEC sp_approlepassword 'ar', '" + Planted + "';",
        ["SpAddAppRole"] = "EXEC sp_addapprole 'ar', '" + Planted + "';",
        ["SpSetAppRole"] = "EXEC sp_setapprole @rolename = 'ar', @password = '" + Planted + "';",
        ["SpAddLinkedSrvLogin"] = "EXEC sp_addlinkedsrvlogin @rmtsrvname = N'LS', @useself = N'FALSE', @locallogin = NULL, @rmtuser = N'u', @rmtpassword = N'" + Planted + "';",
        ["SpAddLinkedServer"] = "EXEC sp_addlinkedserver @server = N'LS', @srvproduct = N'', @provider = N'MSOLEDBSQL', @datasrc = N'remote', @provstr = N'UID=u;PWD=" + Planted + "';",
        ["SpControlDbMasterKeyPassword"] = "EXEC sp_control_dbmasterkey_password @db_name = N'Estate', @password = N'" + Planted + "', @action = N'add';",
        ["VariableOrParameter"] = "CREATE PROCEDURE dbo.Rotate @ServicePassword NVARCHAR(128) = N'" + Planted + "' AS DECLARE @pwd NVARCHAR(128) = N'" + Planted + "'; SET @pwd = N'" + Planted + "'; SELECT 1;",
        ["SqlCmdConnect"] = ":connect remote -U u -P " + Planted + "\nSELECT 1;",
    };

    public static TheoryData<string> Forms => new(PasswordForm.All.Select(f => f.Name));

    [Theory]
    [Trait("Category", "fast")]
    [Trait("Value", "X2")]
    [MemberData(nameof(Forms))]
    public void Each_password_form_planted_in_a_script_is_printed_as_left_out_and_named(string form)
    {
        Assert.True(Samples.TryGetValue(form, out var sample), form + " has no planted sample; add one to SchemaTextTests.Samples");

        var printed = Assert.IsType<PrintedScript.Printed>(SchemaText.Print(sample!));

        Assert.DoesNotContain(Planted, printed.Text, StringComparison.Ordinal);
        Assert.Contains(SchemaText.LeftOut, printed.Text, StringComparison.Ordinal);
        Assert.Equal([form], printed.Forms.Select(f => f.Name));
    }

    [Fact]
    [Trait("Category", "fast")]
    public void Every_form_has_exactly_one_sample_and_every_sample_a_form() =>
        Assert.Equal(PasswordForm.All.Select(f => f.Name).Order(StringComparer.Ordinal), Samples.Keys.Order(StringComparer.Ordinal));

    /// <summary>A SQLCMD variable where the literal would stand is no literal: the script prints unchanged and names no form.</summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("CREATE LOGIN [svc] WITH PASSWORD = '$(ServicePassword)';")]
    [InlineData("CREATE LOGIN [svc] WITH PASSWORD = $(ServicePassword);")]
    [InlineData("EXEC sp_addapprole 'ar', $(ArPassword);")]
    [InlineData("CREATE CREDENTIAL [cr] WITH IDENTITY = 'svc', SECRET = '$(Secret)';")]
    public void A_value_that_is_a_SQLCMD_variable_stays_and_names_no_form(string script)
    {
        var printed = Assert.IsType<PrintedScript.Printed>(SchemaText.Print(script));

        Assert.Equal(script, printed.Text);
        Assert.Empty(printed.Forms);
    }

    /// <summary>A password in double quotes, which SQL Server reads as a string under QUOTED_IDENTIFIER OFF, is left out as one in single quotes is.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_password_in_double_quotes_under_quoted_identifier_off_is_left_out()
    {
        var printed = Assert.IsType<PrintedScript.Printed>(SchemaText.Print("SET QUOTED_IDENTIFIER OFF;\nCREATE LOGIN [svc] WITH PASSWORD = \"" + Planted + "\";"));

        Assert.DoesNotContain(Planted, printed.Text, StringComparison.Ordinal);
        Assert.Equal([PasswordForm.CreateLogin], printed.Forms);
    }

    /// <summary>
    /// Text ScriptDom cannot parse is left out whole, since a password in it cannot be found, and the parse's first error says where it
    /// stopped. BACKUP … WITH PASSWORD, which SQL Server 2012 discontinued, is outside the 2022 grammar and so is such text.
    /// </summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("CREATE PROCEDURE dbo.P AS\n  SELECT 1 FROM;\n  CREATE LOGIN [svc] WITH PASSWORD = '" + Planted + "';", 2, 16)]
    [InlineData("BACKUP DATABASE [Estate] TO DISK = 'c:\\b\\estate.bak' WITH PASSWORD = '" + Planted + "';", 1, 59)]
    public void A_script_ScriptDom_cannot_parse_is_left_out_never_printed(string script, int line, int column)
    {
        var unparsed = Assert.IsType<PrintedScript.Unparsed>(SchemaText.Print(script));

        Assert.Equal((line, column), (unparsed.Line, unparsed.Column));
    }

    /// <summary>What DacFx keeps as an expression (a default, a check, a computed column) is no script and prints as written.</summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("(0)")]
    [InlineData("([Score]>(0) AND [Score]<(100))")]
    [InlineData("(N'')")]
    [InlineData("(getutcdate())")]
    [InlineData("([Price]*[Quantity])")]
    [InlineData("N'The user''s password has expired'")]
    public void An_expression_prints_as_written(string expression)
    {
        var printed = Assert.IsType<PrintedScript.Printed>(SchemaText.Print(expression));

        Assert.Equal(expression, printed.Text);
        Assert.Empty(printed.Forms);
    }

    /// <summary>
    /// VALUES.md X2 through estate diff: two packages whose procedures differ only in the literal of CREATE LOGIN … WITH PASSWORD
    /// diff as the procedure's Definition altered (law 3′: the model keeps the text as written), and diff --json prints both values
    /// with the literal left out, holds neither password, and warns once per side that the procedure sets a password with a literal.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    [Trait("Value", "X2")]
    [Trait("Law", "3′ the model is complete")]
    public void A_diff_of_two_procedures_differing_in_a_password_literal_alone_alters_the_Definition_and_prints_both_values_left_out()
    {
        using var scratch = ScratchFolder.Temporary("printed");
        var (before, after) = (scratch.Under("before.dacpac"), scratch.Under("after.dacpac"));
        Package(before, "CREATE PROCEDURE dbo.Rotate AS CREATE LOGIN [svc] WITH PASSWORD = '" + Planted + "1';");
        Package(after, "CREATE PROCEDURE dbo.Rotate AS CREATE LOGIN [svc] WITH PASSWORD = '" + Planted + "2';");

        using var output = new System.IO.MemoryStream();
        var exit = Cli.Program.Run(["diff", "--from", "dacpac:" + before, "--to", "dacpac:" + after, "--json"], output, new Cli.Checkout(scratch.Path, scratch.Path, null));
        var text = System.Text.Encoding.UTF8.GetString(output.ToArray());
        var answer = System.Text.Json.Nodes.JsonNode.Parse(text)!;

        Assert.Equal((0, "differs"), (exit, (string?)answer["outcome"]));
        var altered = Assert.Single(answer["diff"]!["change"]!["altered"]!.AsArray())!;
        var property = Assert.Single(altered["properties"]!.AsArray())!;
        Assert.Equal(("Procedure [dbo].[Rotate]", "Definition"), ((string?)altered["key"], (string?)property["name"]));
        Assert.All(new[] { (string?)property["before"], (string?)property["after"] }, value => Assert.Contains("WITH PASSWORD = " + SchemaText.LeftOut, value, StringComparison.Ordinal));
        PlantedValue.Password.AbsentFrom(text);
        var warning = Assert.Single(answer["findings"]!.AsArray())!;
        Assert.Equal(("schema.password-literal", "warning", "Procedure [dbo].[Rotate]"), ((string?)warning["code"], (string?)warning["severity"], (string?)warning["subject"]));
        Assert.Contains("CREATE LOGIN … WITH PASSWORD", (string?)warning["message"], StringComparison.Ordinal);
    }

    /// <summary>A package DacFx builds from the scripts, under the path given.</summary>
    private static void Package(string dacpac, params string[] scripts)
    {
        using var model = new Microsoft.SqlServer.Dac.Model.TSqlModel(Microsoft.SqlServer.Dac.Model.SqlServerVersion.Sql160, new Microsoft.SqlServer.Dac.Model.TSqlModelOptions());
        foreach (var script in scripts)
        {
            model.AddObjects(script);
        }

        Microsoft.SqlServer.Dac.DacPackageExtensions.BuildPackage(dacpac, model, new Microsoft.SqlServer.Dac.PackageMetadata());
    }

    /// <summary>A module whose text names a password without setting one prints as written, and a script setting two passwords in two forms names both, each once.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_script_that_names_a_password_without_setting_one_prints_as_written_and_two_forms_are_each_named_once()
    {
        const string Expired = "CREATE PROCEDURE dbo.P AS RAISERROR('The user''s password has expired', 16, 1);";
        var kept = Assert.IsType<PrintedScript.Printed>(SchemaText.Print(Expired));
        var two = Assert.IsType<PrintedScript.Printed>(SchemaText.Print("OPEN MASTER KEY DECRYPTION BY PASSWORD = '" + Planted + "';\nOPEN SYMMETRIC KEY [sk] DECRYPTION BY PASSWORD = '" + Planted + "';\nOPEN MASTER KEY DECRYPTION BY PASSWORD = '" + Planted + "2';"));

        Assert.Equal((Expired, 0), (kept.Text, kept.Forms.Count));
        Assert.Equal([PasswordForm.OpenMasterKey, PasswordForm.OpenSymmetricKey], two.Forms);
        Assert.Equal(3, two.Text.Split(SchemaText.LeftOut).Length - 1);
        Assert.DoesNotContain(Planted, two.Text, StringComparison.Ordinal);
    }
}
