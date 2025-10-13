using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Osm.Pipeline.UatUsers.Steps;

public sealed class PrepareUserMapStep : IPipelineStep<UatUsersContext>
{
    public string Name => "prepare-user-map";

    public Task ExecuteAsync(UatUsersContext context, CancellationToken cancellationToken)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var templateRows = BuildTemplateRows(context.OrphanUserIds);
        context.Artifacts.WriteCsv("00_user_map.template.csv", templateRows);

        var mapPath = context.UserMapPath;
        var existing = File.Exists(mapPath) ? UserMapLoader.Load(mapPath) : Array.Empty<UserMappingEntry>();
        var merged = MergeMappings(context.OrphanUserIds, existing);
        context.SetUserMap(merged);

        WriteUserMap(mapPath, merged);

        var defaultPath = context.Artifacts.GetDefaultUserMapPath();
        if (!string.Equals(defaultPath, mapPath, StringComparison.OrdinalIgnoreCase))
        {
            WriteUserMap(defaultPath, merged);
        }

        return Task.CompletedTask;
    }

    private static IReadOnlyList<IReadOnlyList<string>> BuildTemplateRows(IReadOnlyCollection<long> orphanUserIds)
    {
        var rows = new List<IReadOnlyList<string>>(Math.Max(orphanUserIds.Count + 1, 1))
        {
            new[] { "SourceUserId", "TargetUserId", "Note" }
        };

        foreach (var orphan in orphanUserIds.OrderBy(static value => value))
        {
            rows.Add(new[]
            {
                orphan.ToString(CultureInfo.InvariantCulture),
                string.Empty,
                string.Empty
            });
        }

        return rows;
    }

    private static IReadOnlyList<UserMappingEntry> MergeMappings(
        IReadOnlyCollection<long> orphanUserIds,
        IReadOnlyList<UserMappingEntry> existing)
    {
        var orphanSet = new SortedSet<long>(orphanUserIds);
        if (orphanSet.Count == 0)
        {
            return Array.Empty<UserMappingEntry>();
        }

        var bySource = new Dictionary<long, UserMappingEntry>();
        foreach (var entry in existing)
        {
            if (!orphanSet.Contains(entry.SourceUserId))
            {
                continue;
            }

            if (!bySource.TryGetValue(entry.SourceUserId, out var current) || ShouldReplace(current, entry))
            {
                bySource[entry.SourceUserId] = entry;
            }
        }

        foreach (var orphan in orphanSet)
        {
            if (!bySource.ContainsKey(orphan))
            {
                bySource[orphan] = new UserMappingEntry(orphan, null, null);
            }
        }

        return bySource.Values
            .OrderBy(static entry => entry.SourceUserId)
            .ToArray();
    }

    private static bool ShouldReplace(UserMappingEntry existing, UserMappingEntry candidate)
    {
        if (existing.TargetUserId is null && candidate.TargetUserId is not null)
        {
            return true;
        }

        if (existing.TargetUserId == candidate.TargetUserId && string.IsNullOrEmpty(existing.Note) && !string.IsNullOrEmpty(candidate.Note))
        {
            return true;
        }

        return false;
    }

    private static void WriteUserMap(string path, IReadOnlyList<UserMappingEntry> entries)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var writer = new StreamWriter(path, false, Encoding.UTF8);
        WriteRow(writer, "SourceUserId", "TargetUserId", "Note");
        foreach (var entry in entries)
        {
            var target = entry.TargetUserId.HasValue
                ? entry.TargetUserId.Value.ToString(CultureInfo.InvariantCulture)
                : string.Empty;
            var note = entry.Note ?? string.Empty;
            WriteRow(writer,
                entry.SourceUserId.ToString(CultureInfo.InvariantCulture),
                target,
                note);
        }
    }

    private static void WriteRow(TextWriter writer, params string[] cells)
    {
        for (var i = 0; i < cells.Length; i++)
        {
            if (i > 0)
            {
                writer.Write(',');
            }

            writer.Write(EscapeCell(cells[i]));
        }

        writer.WriteLine();
    }

    private static string EscapeCell(string? value)
    {
        value ??= string.Empty;
        var mustQuote = value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r');
        if (!mustQuote)
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }
}
