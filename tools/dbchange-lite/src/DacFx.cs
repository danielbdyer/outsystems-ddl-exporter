using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml.Linq;
using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.Dac;

namespace DbChangeLite;

public sealed record PlanOperation(string Kind, string Type, string Name)
{
    public string Pair => Kind + " " + Type;
}

public sealed record Plan(IReadOnlyList<PlanOperation> Operations, string Script, string Report)
{
    public void Write(Run run, string name)
    {
        File.WriteAllText(Path.Combine(run.Folder, name + ".report.xml"), Report);
        File.WriteAllText(Path.Combine(run.Folder, name + ".script.sql"), Script);
    }
}

public sealed record Verdict(string Outcome, string? BlockedBy, IReadOnlyList<string> Messages);

public static class DacFx
{
    private static readonly XNamespace Report = "http://schemas.microsoft.com/sqlserver/dac/DeployReport/2012/02";
    private static readonly Regex Quoted = new(@"Msg (\d+), Level (\d+), State (\d+)", RegexOptions.CultureInvariant);

    public static DacPackage Load(string path) => Guard(() => DacPackage.Load(path, DacSchemaModelStorageType.Memory), "package.unreadable", path);

    public static string Text(Stream? script)
    {
        if (script is null)
        {
            return "";
        }

        using var reader = new StreamReader(script);
        return reader.ReadToEnd();
    }

    public static byte[] Extract(string connectionString, string database, CancellationToken cancel) => Guard(() =>
    {
        using var stream = new MemoryStream();
        var options = new DacExtractOptions
        {
            ExtractApplicationScopedObjectsOnly = true, IgnorePermissions = true, IgnoreUserLoginMappings = true, IgnoreExtendedProperties = false,
            Storage = DacSchemaModelStorageType.Memory, VerifyExtraction = false,
        };
        new DacServices(Named(connectionString)).Extract(stream, database, "dbchange-lite", new Version(1, 0), null, null, options, cancel);
        return stream.ToArray();
    }, "extract.failed", database);

    public static Plan Script(DacPackage change, DacPackage environment, string database, DacDeployOptions options) => Guard(() =>
    {
        var result = DacServices.Script(change, environment, database, new PublishOptions { GenerateDeploymentReport = true, GenerateDeploymentScript = true, DeployOptions = options });
        var root = XDocument.Parse(result.DeploymentReport).Root!;
        var operations = root.Elements(Report + "Operations").Elements(Report + "Operation")
            .SelectMany(o => o.Elements(Report + "Item").Select(i => new PlanOperation((string?)o.Attribute("Name") ?? "", (string?)i.Attribute("Type") ?? "", (string?)i.Attribute("Value") ?? "")))
            .ToList();
        return new Plan(operations, result.DatabaseScript ?? "", result.DeploymentReport);
    }, "plan.failed", database);

    public static Verdict Deploy(string connectionString, DacPackage package, string database, DacDeployOptions options, CancellationToken cancel)
    {
        try
        {
            new DacServices(Named(connectionString)).Deploy(package, database, true, options, cancel);
            return new Verdict("done", null, []);
        }
        catch (Exception e) when (e is DacServicesException or SqlException)
        {
            cancel.ThrowIfCancellationRequested();
            var messages = Messages(e);
            var numbers = Numbers(e, messages).ToList();
            if (numbers.Any(n => n.Number == 50000 && n.State == 127))
            {
                return new Verdict("blocked", "block-on-possible-data-loss", messages);
            }

            if (numbers.Any(n => n.Number is 547 or 1505 or 2627 or 2628 or 515 or 1946))
            {
                return new Verdict("blocked", "constraint-violation", messages);
            }

            throw Stop.Of("configuration-refused", "dacfx.failed", database, "DacFx failed the deploy: " + string.Join(" | ", messages.Take(5)), "Correct the change or the rows at the object the first message names, then run prove again.");
        }
    }

    public static IReadOnlyList<string> Messages(Exception e)
    {
        var messages = new List<string>();
        for (var at = e; at is not null; at = at.InnerException)
        {
            if (at is DacServicesException dac)
            {
                messages.AddRange(dac.Messages.Select(m => m.ToString()));
            }
            else if (at is SqlException sql)
            {
                messages.AddRange(sql.Errors.Cast<SqlError>().Select(error => "Msg " + error.Number + ", Level " + error.Class + ", State " + error.State + ": " + error.Message));
            }
            else
            {
                messages.Add(at.Message);
            }
        }

        return messages.Distinct(StringComparer.Ordinal).ToList();
    }

    private static IEnumerable<(int Number, int State)> Numbers(Exception e, IEnumerable<string> messages)
    {
        for (var at = e; at is not null; at = at.InnerException)
        {
            if (at is SqlException sql)
            {
                foreach (var error in sql.Errors.Cast<SqlError>())
                {
                    yield return (error.Number, error.State);
                }
            }
        }

        foreach (var message in messages)
        {
            foreach (Match match in Quoted.Matches(message))
            {
                yield return (int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), int.Parse(match.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture));
            }
        }
    }

    internal static T Guard<T>(Func<T> act, string code, string subject)
    {
        try
        {
            return act();
        }
        catch (ServerProbeRefused)
        {
            throw Stop.Of("configuration-refused", "dacfx.failed", subject, "DacFx " + DacFxRelease.Running + " sent exec xp_msver past the refusal dbchange-lite installs, so the read stops: dbchange-lite sends no EXEC to an environment.",
                "Run the dbchange-lite build whose DacFx is the release dbchange/ledgers/toolchain.md pins, and report this answer to the maintainer of dbchange-lite.");
        }
        catch (Exception e) when (e is DacServicesException or Microsoft.SqlServer.Dac.Model.DacModelException or SqlException or IOException or InvalidOperationException)
        {
            var sql = Chain(e).OfType<SqlException>().FirstOrDefault();
            if (sql is not null && (sql.Number is 53 or 40 or 10061 or 11001 or 2 or -2 or 18456 or 4060 or 233 or 1225 or 258))
            {
                throw Stop.Of("unreachable", "environment.unreachable", subject, "SQL Server did not answer for " + subject + ": Msg " + sql.Number + ".",
                    "Check the connection the environments file references, the login's access and the network, then run the verb again.");
            }

            throw Stop.Of("configuration-refused", code, subject, "DacFx failed: " + string.Join(" | ", Messages(e).Take(5)), "Correct the input the first message names, then run the verb again.");
        }
    }

    private static string Named(string connectionString) => new SqlConnectionStringBuilder(connectionString) { ApplicationName = "dbchange-lite" }.ConnectionString;

    private static IEnumerable<Exception> Chain(Exception e)
    {
        for (var at = e; at is not null; at = at.InnerException)
        {
            yield return at;
        }
    }
}
