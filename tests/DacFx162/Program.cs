using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.Dac;
using Microsoft.SqlServer.Dac.Model;

namespace DbChange.DacFx162;

/// <summary>
/// DacFx 162.5.57, the release the pipeline is presumed to run until spike S7 names it, run as a child process of the fixture tests
/// beside the committed 170.5.96: <c>dotnet DbChange.DacFx162.dll &lt;verb&gt; &lt;argument&gt;...</c>. The first line of the output is
/// <c>DacFx &lt;major.minor.build&gt;</c>, the file version of the Microsoft.SqlServer.Dac.dll this process loaded. The connection
/// string comes from the environment variable DBCHANGE_DACFX162_CONNECTION and never from the arguments, which io/Command prints.
/// Exit 0 when the verb ran, 1 when DacFx, SQL Server or a file failed (the message on standard error), 2 for an unknown verb,
/// arguments of the wrong shape or no connection; any other exception ends the process unhandled, with its stack on standard error.
/// Spikes S9 to S11 add the schema reads <c>extract</c> and <c>constraints</c>, the deploy report beside each script, and a failed
/// verb's DacFx messages on standard error.
/// </summary>
internal static class Program
{
    private const string ConnectionVariable = "DBCHANGE_DACFX162_CONNECTION";

    /// <summary>Each verb: its arguments' shape, as the usage line prints it, and the call, given the connection and the arguments.</summary>
    private static readonly Dictionary<string, (string Shape, Func<string, string[], int> Run)> Verbs = new(StringComparer.Ordinal)
    {
        ["script"] = ("<profile.publish.xml> <database> <output folder> <name>=<dacpac>...", Script),
        ["publish"] = ("<profile.publish.xml> <database> <dacpac>", Publish),
        ["extract"] = ("<database> <dacpac>", Extract),
        ["constraints"] = ("(no argument)", Constraints),
    };

    private static int Main(string[] args)
    {
        Environment.SetEnvironmentVariable("DACFX_TELEMETRY_OPTOUT", "1");
        var dacfx = FileVersionInfo.GetVersionInfo(typeof(DacServices).Assembly.Location);
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"DacFx {dacfx.FileMajorPart}.{dacfx.FileMinorPart}.{dacfx.FileBuildPart}"));
        if (args.Length == 0 || !Verbs.TryGetValue(args[0], out var verb))
        {
            return Usage("no verb " + (args.Length == 0 ? "given" : "named " + args[0]));
        }

        if (Environment.GetEnvironmentVariable(ConnectionVariable) is not { Length: > 0 } connection)
        {
            return Usage(ConnectionVariable + " is unset; it holds the connection string, which no argument carries");
        }

        try
        {
            return verb.Run(connection, args[1..]);
        }
        catch (Exception failure) when (failure is DacServicesException or SqlException or IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine(failure.GetType().Name + ": " + failure.Message);
            foreach (var message in (failure as DacServicesException)?.Messages ?? [])
            {
                Console.Error.WriteLine(message.ToString());
            }

            return 1;
        }
    }

    /// <summary>
    /// script: DacServices.Script of each package, loaded from a stream, against the database the connection reaches, under the profile's
    /// deploy options alone; each deployment script written to &lt;output folder&gt;/&lt;name&gt;.sql and each deploy report to
    /// &lt;name&gt;.xml, UTF-8 without a BOM, as DacFx returned them (CRLF line endings included). A package loaded by path would load the assemblies beside it into this process.
    /// DacFx writes the script to that file first, and a master script to &lt;name&gt;.master.sql beside it for a target that takes one:
    /// given no path, DacFx names the file after the database alone in the temporary folder, where a plan of the same database by the
    /// fixture's own process, at the same moment, can read or delete it (io/DacFx.Scripted).
    /// </summary>
    private static int Script(string connection, string[] args)
    {
        if (args.Length < 4 || args[3..].Any(a => a.IndexOf('=', StringComparison.Ordinal) < 1))
        {
            return Usage("script takes " + Verbs["script"].Shape);
        }

        var options = DacProfile.Load(args[0]).DeployOptions;
        var services = new DacServices(connection);
        foreach (var head in args[3..])
        {
            var split = head.IndexOf('=', StringComparison.Ordinal);
            var output = Path.Combine(args[2], head[..split]);
            using var stream = File.OpenRead(head[(split + 1)..]);
            using var package = DacPackage.Load(stream);
            var plan = services.Script(package, args[1], new PublishOptions { GenerateDeploymentScript = true, GenerateDeploymentReport = true,
                DeployOptions = options, DatabaseScriptPath = output + ".sql", MasterDbScriptPath = output + ".master.sql" });
            File.WriteAllText(output + ".sql", plan.DatabaseScript, new UTF8Encoding(false));
            File.WriteAllText(output + ".xml", plan.DeploymentReport, new UTF8Encoding(false));
        }

        return 0;
    }

    /// <summary>
    /// publish: DacServices.Publish of the package, loaded from a stream, to the database the connection reaches, under the profile's
    /// deploy options alone, so a copy carries what DacFx 162.5.57 makes of the model.
    /// </summary>
    private static int Publish(string connection, string[] args)
    {
        if (args.Length != 3)
        {
            return Usage("publish takes " + Verbs["publish"].Shape);
        }

        using var stream = File.OpenRead(args[2]);
        using var package = DacPackage.Load(stream);
        new DacServices(connection).Publish(package, args[1], new PublishOptions { DeployOptions = DacProfile.Load(args[0]).DeployOptions });
        return 0;
    }

    /// <summary>
    /// extract: DacServices.Extract of the database's schema into the package, under the options of io/DacFx.Extraction that DacFx
    /// 162.5.57 has: permissions, extended properties and user login mappings kept, application-scoped objects only, no usage property, no
    /// table data and no verification. Spike S9 reads what the package keeps of a disabled and an untrusted constraint.
    /// </summary>
    private static int Extract(string connection, string[] args)
    {
        if (args.Length != 2)
        {
            return Usage("extract takes " + Verbs["extract"].Shape);
        }

        new DacServices(connection).Extract(args[1], args[0], "dbchange-dacfx162", new Version(1, 0), "", null, new DacExtractOptions
        {
            IgnorePermissions = false,
            IgnoreExtendedProperties = false,
            IgnoreUserLoginMappings = false,
            ExtractApplicationScopedObjectsOnly = true,
            ExtractReferencedServerScopedElements = true,
            ExtractUsageProperties = false,
            ExtractAllTableData = false,
            VerifyExtraction = false,
            Storage = DacSchemaModelStorageType.Memory,
        });
        return 0;
    }

    /// <summary>
    /// constraints: TSqlModel.LoadFromDatabase of the database the connection reaches, then one line per check and foreign key constraint
    /// in ordinal order, <c>&lt;type&gt; &lt;name&gt; Disabled=&lt;True|False&gt;</c>, the type as DacFx names it and an unnamed
    /// constraint's name as <c>unnamed</c>. Disabled is the one property DacFx's public model gives a constraint's state; trust has none
    /// (spike S9).
    /// </summary>
    private static int Constraints(string connection, string[] args)
    {
        if (args.Length != 0)
        {
            return Usage("constraints takes no argument");
        }

        using var model = TSqlModel.LoadFromDatabase(connection, new ModelExtractOptions());
        var lines = model.GetObjects(DacQueryScopes.UserDefined, CheckConstraint.TypeClass)
            .Select(c => Line(c, c.GetProperty<bool>(CheckConstraint.Disabled)))
            .Concat(model.GetObjects(DacQueryScopes.UserDefined, ForeignKeyConstraint.TypeClass)
                .Select(c => Line(c, c.GetProperty<bool>(ForeignKeyConstraint.Disabled))))
            .Order(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            Console.WriteLine(line);
        }

        return 0;

        static string Line(TSqlObject constraint, bool disabled) => constraint.ObjectType.Name + " "
            + (constraint.Name.HasName ? constraint.Name.ToString() : "unnamed") + " Disabled=" + (disabled ? "True" : "False");
    }

    private static int Usage(string why)
    {
        Console.Error.WriteLine(why + ". Usage: DbChange.DacFx162 "
            + string.Join(" | ", Verbs.Select(v => v.Key + " " + v.Value.Shape)) + ", with " + ConnectionVariable + " set.");
        return 2;
    }
}
