using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace DbChangeLite;

public static class Doctor
{
    private sealed record Item(string Name, string State, string Detail, string? Remedy);

    public static Answer Run(Run run)
    {
        var items = new List<Item>();
        var dotnet = Ssdt.Exec("dotnet", ["--version"], run.Root, TimeSpan.FromMinutes(1), run.Cancel);
        var sdk = dotnet.Code == 0 ? dotnet.Output.Trim() : "";
        items.Add(sdk.StartsWith("10.", StringComparison.Ordinal) ? Ok(".NET SDK", sdk) : Missing(".NET SDK", "dotnet --version answers " + (sdk.Length == 0 ? "nothing" : sdk), "Install the .NET 10 SDK global.json names."));
        var ledger = ToolchainLedger.Read(run.Root);
        items.Add(Window(ledger));
        items.Add(File.Exists(Path.Combine(run.Tool, Ssdt.Targets)) ? Ok("build targets", Ssdt.Targets + " in " + run.Tool)
            : Missing("build targets", run.Tool + " holds no " + Ssdt.Targets, "Run ci/publish.sh (ci/publish.ps1 on Windows), or set DBCHANGE_LITE_TOOL to the published folder."));
        items.Add(File.Exists(Path.Combine(run.Tool, "operations.json")) && File.Exists(Path.Combine(run.Tool, "causes.json")) ? Ok("operations.json and causes.json", run.Tool)
            : Missing("operations.json and causes.json", run.Tool + " lacks one of them", "Publish the tool folder again."));
        var local = LocalServer.Try(run);
        items.Add(local is null ? Unreachable("local server", LocalServer.Describe(), "Start the local server (ci/sql.sh up, or LocalDB), or set DBCHANGE_SQL.") : Ok("local server", local));
        Environments? environments = null;
        try
        {
            environments = EnvironmentsFile.Read(run.Root);
            items.Add(Ok(EnvironmentsFile.Name, string.Join(", ", environments.All.Select(e => e.Name))));
        }
        catch (Stop stop)
        {
            items.Add(Missing(EnvironmentsFile.Name, stop.Finding.Message, stop.Finding.Remedy!));
        }

        var project = Project(run, items);
        foreach (var environment in environments?.All ?? [])
        {
            items.AddRange(Environment(environment, project, run, ledger));
        }

        var findings = items.Select(i => i.State is "ok" or "elsewhere" ? Finding.Note("doctor." + Slug(i.Name), i.Name, i.Detail) : Finding.Error("doctor." + Slug(i.Name), i.Name, i.Detail, i.Remedy!)).ToList();
        var outcome = items.Any(i => i.State == "missing") ? "degraded" : items.Any(i => i.State == "unreachable") ? "unreachable" : "ready";
        var missing = items.Where(i => i.State is "missing" or "unreachable").ToList();
        var message = outcome == "ready" ? "Ready: every item is present." : missing.Count.ToString(CultureInfo.InvariantCulture) + " item" + (missing.Count == 1 ? "" : "s") + " missing or unreachable: " + string.Join("; ", missing.Select(i => i.Name)) + ".";
        return Answer.Of("doctor", Contract.Of(outcome), message, findings, run);
    }

    private static Item Window(Toolchain ledger)
    {
        try
        {
            ledger.HoldWindow();
            return Ok("DacFx", DacFxRelease.Running + (ledger.Pin == "UNPINNED" ? ", the ledger UNPINNED: " + Toolchain.Path + " names no DacFx release yet" : ", at the ledger's pin " + ledger.Pin));
        }
        catch (Stop stop)
        {
            return Missing("DacFx", stop.Finding.Message, stop.Finding.Remedy!);
        }
    }

    private static (IReadOnlyList<string> SqlCmd, IReadOnlyList<string> References, int Platform)? Project(Run run, List<Item> items)
    {
        string path;
        try
        {
            path = Ssdt.OneProject(run.Root);
        }
        catch (Stop stop)
        {
            items.Add(Missing("SSDT project", stop.Finding.Message, stop.Finding.Remedy!));
            return null;
        }

        var xml = XDocument.Load(path);
        var ns = xml.Root!.Name.Namespace;
        var variables = xml.Descendants(ns + "SqlCmdVariable").Select(v => (string?)v.Attribute("Include") ?? "").Where(v => v.Length > 0).ToList();
        var references = xml.Descendants(ns + "ArtifactReference").Select(r => (string?)r.Element(ns + "HintPath") ?? (string?)r.Attribute("Include") ?? "").Where(r => r.Length > 0).ToList();
        var platform = Regex.Match(xml.Descendants(ns + "DSP").FirstOrDefault()?.Value ?? "", @"Sql(\d{2,3})Database", RegexOptions.CultureInvariant);
        var major = platform.Success ? int.Parse(platform.Groups[1].Value, CultureInfo.InvariantCulture) / 10 : 0;
        items.Add(Ok("SSDT project", Path.GetRelativePath(run.Root, path).Replace('\\', '/') + ", platform Sql" + (major * 10).ToString(CultureInfo.InvariantCulture)));
        foreach (var reference in references)
        {
            var file = Path.GetFileName(reference.Replace('\\', '/'));
            var system = file.Equals("master.dacpac", StringComparison.OrdinalIgnoreCase) || file.Equals("msdb.dacpac", StringComparison.OrdinalIgnoreCase);
            var resolved = system ? Path.Combine(run.Tool, "Extensions", "Microsoft", "SQLDB", "Extensions", "SqlServer", (major * 10).ToString(CultureInfo.InvariantCulture), "SqlSchemas", file)
                : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, reference.Replace('\\', Path.DirectorySeparatorChar)));
            items.Add(File.Exists(resolved) ? Ok("reference " + file, resolved) : Missing("reference " + file, resolved + " does not exist", system ? "Publish the tool folder again, which carries the system dacpacs." : "Restore the referenced dacpac at " + reference + "."));
        }

        return (variables, references, major);
    }

    private static IEnumerable<Item> Environment(NamedEnvironment environment, (IReadOnlyList<string> SqlCmd, IReadOnlyList<string> References, int Platform)? project, Run run, Toolchain ledger)
    {
        var items = new List<Item>();
        PublishProfile? profile = null;
        try
        {
            profile = PublishProfiles.Load(run.Root, environment.Profile);
            items.Add(Ok("profile of " + environment.Name, environment.Profile + " " + profile.Hash + (ledger.Confirms(profile.Hash) ? ", confirmed" : "; no confirmation row in " + Toolchain.Path)));
        }
        catch (Stop stop)
        {
            items.Add(Missing("profile of " + environment.Name, stop.Finding.Message, stop.Finding.Remedy!));
        }

        if (project is { } p && profile is not null)
        {
            var unset = p.SqlCmd.Where(v => !profile.SqlCmd.ContainsKey(v) && !environment.SqlCmd.ContainsKey(v)).ToList();
            items.Add(unset.Count == 0 ? Ok("SQLCMD values of " + environment.Name, p.SqlCmd.Count.ToString(CultureInfo.InvariantCulture) + " variables valued")
                : Missing("SQLCMD values of " + environment.Name, "no value for " + string.Join(", ", unset), "Add each value under sqlcmd of " + environment.Name + " in " + EnvironmentsFile.Name + ", or to the profile."));
        }

        if (environment.NeverRead)
        {
            items.Add(Ok("read of " + environment.Name, "never read, by the environments file"));
            return items;
        }

        try
        {
            using var session = Session.Open(EnvironmentsFile.Resolve(run.Root, environment.Connection), run.Log, run.Cancel, environment.Name);
            var (version, edition, compat) = session.ServerInfo();
            var grant = session.Rows("SELECT HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'VIEW DEFINITION')").FirstOrDefault()?[0] == "1";
            var major = int.Parse(version.Split('.')[0], CultureInfo.InvariantCulture);
            items.Add(!grant ? Missing("read of " + environment.Name, "this login holds no VIEW DEFINITION on " + session.Database, "Ask for VIEW DEFINITION on the database, which Extract and Script need.")
                : project is { } q && q.Platform > major ? Missing("platform of " + environment.Name, "SQL Server " + version + " is older than the project's platform Sql" + (q.Platform * 10).ToString(CultureInfo.InvariantCulture), "Lower the project's DSP, or upgrade the environment.")
                : Ok("read of " + environment.Name, "SQL Server " + version + " " + edition + ", compatibility level " + compat.ToString(CultureInfo.InvariantCulture) + ", VIEW DEFINITION held"));
        }
        catch (Stop stop)
        {
            items.Add(stop.Finding.Code.StartsWith("environments.reference", StringComparison.Ordinal)
                ? new Item("read of " + environment.Name, "elsewhere", stop.Finding.Message + " This machine does not read " + environment.Name + "; a person who holds its connection does. " + stop.Finding.Remedy, null)
                : stop.Outcome.Word == "unreachable" ? Unreachable("read of " + environment.Name, stop.Finding.Message, stop.Finding.Remedy!) : Missing("read of " + environment.Name, stop.Finding.Message, stop.Finding.Remedy!));
        }

        return items;
    }

    private static Item Ok(string name, string detail) => new(name, "ok", detail, null);

    private static Item Missing(string name, string detail, string remedy) => new(name, "missing", detail, remedy);

    private static Item Unreachable(string name, string detail, string remedy) => new(name, "unreachable", detail, remedy);

    private static string Slug(string name) => Regex.Replace(name.ToLowerInvariant(), @"[^a-z0-9]+", "-", RegexOptions.CultureInvariant).Trim('-');
}
