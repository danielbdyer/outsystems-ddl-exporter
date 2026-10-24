using System;
using System.Globalization;
using System.Linq;

namespace DbChangeLite;

public static class Diff
{
    public static Answer Run(Invocation invocation, Run run)
    {
        var from = Ssdt.Package(Source.Parse(invocation.Values["--from"], "--from"), null, run);
        var to = Ssdt.Package(Source.Parse(invocation.Values["--to"], "--to"), null, run);
        var lines = ModelDiff.Lines(from.PackagePath, to.PackagePath);
        var findings = lines.Select(l => Finding.Note("diff." + Code(l), Subject(l), l)).ToList();
        var message = lines.Count == 0 ? to.Describe + " changes nothing against " + from.Describe + "."
            : to.Describe + " makes " + lines.Count.ToString(CultureInfo.InvariantCulture) + " change" + (lines.Count == 1 ? "" : "s") + " against " + from.Describe + ".";
        return Answer.Of("diff", Contract.Of("done"), message, findings, run) with
        {
            Provenance = new Provenance(to.Commit ?? to.Fingerprint, from.Commit ?? from.Fingerprint, to.Tree, null, false, run.Stamp),
        };
    }

    private static string Code(string line) => line.StartsWith("created ", StringComparison.Ordinal) ? "created"
        : line.StartsWith("dropped ", StringComparison.Ordinal) ? "dropped" : line.StartsWith("renamed ", StringComparison.Ordinal) ? "renamed" : "altered";

    private static string Subject(string line)
    {
        var words = line.Split(' ');
        var at = line.StartsWith("created ", StringComparison.Ordinal) || line.StartsWith("dropped ", StringComparison.Ordinal) || line.StartsWith("renamed ", StringComparison.Ordinal) ? 2 : 1;
        return words.Length > at ? words[at].TrimEnd(':') : line;
    }
}
