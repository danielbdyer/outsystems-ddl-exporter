using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.SqlServer.Dac;

namespace DbChangeLite;

public static class Dacfx
{
    public static readonly string Running = Release(FileVersionInfo.GetVersionInfo(typeof(DacServices).Assembly.Location).FileVersion ?? "0.0.0.0");

    public static string Release(string version) => string.Join('.', version.Split('.').Take(3));
}

public sealed record Toolchain(string Pin, string? ReleaseBefore, IReadOnlyList<(string Hash, string ConfirmedBy)> Profiles, bool LedgerExists)
{
    public const string Path = "dbchange/ledgers/toolchain.md";

    public bool Confirms(string profileHash) => Profiles.Any(p => Fingerprint.Same(p.Hash, profileHash));

    public void HoldWindow()
    {
        if (Pin == "UNPINNED" || Pin == Dacfx.Running || ReleaseBefore == Dacfx.Running)
        {
            return;
        }

        throw Stop.Of("configuration-refused", "toolchain.outside-window", Path,
            "dbchange-lite runs DacFx " + Dacfx.Running + ", and " + Path + " pins the deployment step's DacFx at " + Pin
            + (ReleaseBefore is null ? "" : " with " + ReleaseBefore + " before it") + ".",
            "Run the dbchange-lite build whose DacFx is the pin or the release before it, or add the ledger row that moves the pin in a pull request that runs the corpus.");
    }
}

public static class ToolchainLedger
{
    public static Toolchain Read(string root)
    {
        var path = System.IO.Path.Combine(root, Toolchain.Path);
        if (!File.Exists(path))
        {
            return new Toolchain("UNPINNED", null, [], false);
        }

        var tables = Tables(File.ReadAllLines(path));
        var toolchain = tables.Find(t => t.Header.Any(h => h.Contains("DacFx", StringComparison.OrdinalIgnoreCase)));
        var profiles = tables.Find(t => t.Header.Any(h => h.Contains("Profile", StringComparison.OrdinalIgnoreCase)));
        var last = toolchain?.Rows.LastOrDefault();
        var pin = last is { Count: >= 3 } ? last[2].Trim() : "UNPINNED";
        var before = last is { Count: >= 4 } && last[3].Trim() is { Length: > 0 } b && b != "—" && b != "-" ? b : null;
        var confirmed = profiles?.Rows.Where(r => r.Count >= 4).Select(r => (r[2].Trim(), r[3].Trim())).ToList() ?? [];
        return new Toolchain(pin.Length == 0 ? "UNPINNED" : pin, before, confirmed, true);
    }

    private sealed record MarkdownTable(IReadOnlyList<string> Header, List<IReadOnlyList<string>> Rows);

    private static List<MarkdownTable> Tables(IEnumerable<string> lines)
    {
        var tables = new List<MarkdownTable>();
        MarkdownTable? current = null;
        foreach (var line in lines)
        {
            if (!line.TrimStart().StartsWith('|'))
            {
                current = null;
                continue;
            }

            var cells = line.Trim().Trim('|').Split('|').Select(c => c.Trim()).ToList();
            if (cells.All(c => c.Length > 0 && c.All(ch => ch is '-' or ':')))
            {
                continue;
            }

            if (current is null)
            {
                current = new MarkdownTable(cells, []);
                tables.Add(current);
                continue;
            }

            current.Rows.Add(cells);
        }

        return tables;
    }
}
