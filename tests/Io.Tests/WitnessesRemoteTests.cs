using System.Collections.Generic;
using System.Linq;
using DbChange.Kernel;
using DbChange.Tests;
using SqlServerVersion = Microsoft.SqlServer.Dac.Model.SqlServerVersion;
using TSqlModel = Microsoft.SqlServer.Dac.Model.TSqlModel;
using TSqlModelOptions = Microsoft.SqlServer.Dac.Model.TSqlModelOptions;
using Xunit;
using static DbChange.Tests.Expect;
using Elements = DbChange.Kernel.Tests.KernelProperties;

namespace DbChange.Io.Tests;

/// <summary>
/// PAR-033 and VALUES.md P1 beyond the model's T-SQL modules: a declared block runs as one batch on the copy's connection, with triggers
/// firing, so an object that reaches another server reaches it for the block whatever made it. A module bound to an assembly
/// (AS EXTERNAL NAME) holds no T-SQL that shows what it reaches, and an EXTERNAL_ACCESS or UNSAFE assembly can open any connection, so
/// it reads as reaching another server; and a view, a function, a procedure or a DML trigger a pre- or post-deploy script creates is
/// read as the model's own are, as the script's CREATE SYNONYM is.
/// </summary>
public sealed class WitnessesRemoteTests
{
    /// <summary>
    /// The model's tables dbo.Customer and dbo.Audit, and three modules bound to the assembly Remote, as elements: the procedure dbo.Pull
    /// with no Definition, the function dbo.Lookup whose Definition is CREATE FUNCTION ... AS EXTERNAL NAME, and the trigger
    /// dbo.Audit_Sent on dbo.Audit with no Definition. (DacFx 170.5.96 refuses a model whose module names an assembly it does not hold,
    /// so the elements are written here.) A block calling dbo.Lookup or writing dbo.Audit is witness.not-dml by its line, naming the
    /// object, dbo.Pull is among the repository's remote objects, and a block writing dbo.Customer enters.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    [Trait("Value", "P1")]
    public void A_module_bound_to_an_assembly_reads_as_reaching_another_server_to_a_declared_witness()
    {
        using var model = new TSqlModel(SqlServerVersion.Sql160, new TSqlModelOptions());
        model.AddObjects("CREATE TABLE dbo.Customer (Id INT NOT NULL PRIMARY KEY, Name NVARCHAR(40) NULL);");
        model.AddObjects("CREATE TABLE dbo.Audit (Id INT NOT NULL PRIMARY KEY, Name NVARCHAR(40) NULL);");
        var tables = Value(Ssdt.ReadModel(model));
        Element[] elements =
        [
            .. tables,
            Elements.New(Elements.Key("Procedure", "dbo", "Pull"), []),
            Elements.New(Elements.Key("ScalarFunction", "dbo", "Lookup"), [("Definition", new Value.Script(
                "CREATE FUNCTION dbo.Lookup (@Id INT) RETURNS INT AS EXTERNAL NAME [Remote].[Remote.Calls].[Lookup];"))]),
            Elements.New(Elements.Key("DmlTrigger", "dbo", "Audit_Sent"), [],
                [("TriggerObject", [Elements.Key("Table", "dbo", "Audit")])]),
        ];
        var repository = Witnesses.Repository.Of(elements, Value(Schema.Of(tables)));

        Assert.All(new[]
        {
            ("INSERT INTO dbo.Customer (Id) VALUES (dbo.Lookup(1));", "the function [dbo].[Lookup], which reaches another server"),
            ("INSERT INTO dbo.Audit (Id, Name) VALUES (1, N'w1');", "a write to [dbo].[Audit], whose trigger [dbo].[Audit_Sent]"),
        }, refused => Assert.StartsWith("dbchange/witnesses.sql line 2 holds " + refused.Item2,
            Failed(Witnesses.Parse("-- plants: [dbo].[Customer]\n" + refused.Item1, repository), "witness.not-dml").Message,
            System.StringComparison.Ordinal));
        Assert.Contains(repository.Remotes, r => r.Key.ToString() == "Procedure [dbo].[Pull]");
        Assert.IsType<Result<IReadOnlyList<Witnesses.Block>>.Ok>(
            Witnesses.Parse("-- plants: [dbo].[Customer]\nINSERT INTO dbo.Customer (Id, Name) VALUES (1, N'w2');", repository));
    }

    /// <summary>
    /// A post-deploy script creates the view dbo.RemoteView over [prod-sql].[Catalog].[dbo].[Customer], CREATE OR ALTER the function
    /// dbo.RemoteRows over OPENQUERY, the procedure dbo.Ship that executes AT [prod-sql], the trigger dbo.Audit_Copied on dbo.Audit that
    /// runs dbo.Ship, a CLR procedure dbo.Pull, and the view dbo.LocalView over dbo.Customer. A block reading the view or the function or
    /// writing dbo.Audit is witness.not-dml naming it, and the procedures are among the repository's remote objects; a block reading
    /// dbo.LocalView enters.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    [Trait("Value", "P1")]
    public void A_view_function_procedure_or_trigger_a_deploy_script_creates_is_read_as_the_model_s_own_are()
    {
        using var model = new TSqlModel(SqlServerVersion.Sql160, new TSqlModelOptions());
        model.AddObjects("CREATE TABLE dbo.Customer (Id INT NOT NULL PRIMARY KEY, Name NVARCHAR(40) NULL);");
        model.AddObjects("CREATE TABLE dbo.Audit (Id INT NOT NULL PRIMARY KEY, Name NVARCHAR(40) NULL);");
        var schema = Value(Ssdt.ReadModel(model));
        var script = string.Join("\nGO\n",
            "CREATE VIEW dbo.RemoteView AS SELECT Name FROM [prod-sql].[Catalog].[dbo].[Customer];",
            "CREATE OR ALTER FUNCTION dbo.RemoteRows() RETURNS TABLE AS RETURN SELECT q.Name FROM OPENQUERY([prod-sql], "
                + "'SELECT Name FROM Catalog.dbo.Customer') AS q;",
            "CREATE PROCEDURE dbo.Ship AS EXEC ('INSERT INTO Catalog.dbo.Audit (Name) VALUES (1);') AT [prod-sql];",
            "CREATE TRIGGER dbo.Audit_Copied ON dbo.Audit AFTER INSERT AS EXEC dbo.Ship;",
            "CREATE PROCEDURE dbo.Pull @Id INT AS EXTERNAL NAME [Remote].[Remote.Calls].[Pull];",
            "CREATE VIEW dbo.LocalView AS SELECT Id, Name FROM dbo.Customer;") + "\nGO\n";
        Element[] elements = [.. schema, Element.PostDeploy(script)];
        var repository = Witnesses.Repository.Of(elements, Value(Schema.Of(schema)));
        Result<IReadOnlyList<Witnesses.Block>> Declared(string statement) =>
            Witnesses.Parse("-- plants: [dbo].[Customer]\n" + statement, repository);

        Assert.All(new[]
        {
            ("INSERT INTO dbo.Customer (Id, Name) SELECT 1, v.Name FROM dbo.RemoteView AS v;", "the view [dbo].[RemoteView]"),
            ("INSERT INTO dbo.Customer (Id, Name) SELECT 1, r.Name FROM dbo.RemoteRows() AS r;", "the function [dbo].[RemoteRows]"),
            ("INSERT INTO dbo.Audit (Id, Name) VALUES (1, N'w1');", "a write to [dbo].[Audit], whose trigger [dbo].[Audit_Copied]"),
        }, refused => Assert.StartsWith("dbchange/witnesses.sql line 2 holds " + refused.Item2 + ", which reaches another server",
            Failed(Declared(refused.Item1), "witness.not-dml").Message, System.StringComparison.Ordinal));
        Assert.Equal(["Procedure [dbo].[Pull]", "Procedure [dbo].[Ship]"],
            repository.Remotes.Where(r => r.Key.Type == "Procedure").Select(r => r.Key.ToString()));
        Assert.IsType<Result<IReadOnlyList<Witnesses.Block>>.Ok>(
            Declared("INSERT INTO dbo.Customer (Id, Name) SELECT v.Id, v.Name FROM dbo.LocalView AS v;"));
    }
}
