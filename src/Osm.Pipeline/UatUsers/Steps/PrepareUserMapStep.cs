using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Osm.Pipeline.UatUsers.Steps;

public sealed class PrepareUserMapStep : IPipelineStep<UatUsersContext>
{
    private readonly ILogger<PrepareUserMapStep> _logger;

    public PrepareUserMapStep(ILogger<PrepareUserMapStep>? logger = null)
    {
        _logger = logger ?? NullLogger<PrepareUserMapStep>.Instance;
    }

    public string Name => "prepare-user-map";

    public Task ExecuteAsync(UatUsersContext context, CancellationToken cancellationToken)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        _logger.LogInformation(
            "Preparing user map artifacts. OrphanCount={OrphanCount}.",
            context.OrphanUserIds.Count);

        var templateRows = BuildTemplateRows(context.OrphanUserIds);
        context.Artifacts.WriteCsv("00_user_map.template.csv", templateRows);
        _logger.LogInformation(
            "User map template written to {Path}.",
            Path.Combine(context.Artifacts.Root, "uat-users", "00_user_map.template.csv"));

        var mapPath = context.UserMapPath;
        var existing = File.Exists(mapPath) ? UserMapLoader.Load(mapPath) : Array.Empty<UserMappingEntry>();
        var automatic = context.AutomaticMappings ?? Array.Empty<UserMappingEntry>();
        _logger.LogInformation(
            "Loaded {ExistingCount} existing mapping rows from {MapPath}.",
            existing.Count,
            mapPath);
        _logger.LogInformation(
            "Matching engine proposed {AutomaticCount} automatic rows.",
            automatic.Count);
        var merged = MergeMappings(context.OrphanUserIds, existing, automatic);
        context.SetUserMap(merged);

        WriteUserMap(mapPath, merged);
        _logger.LogInformation(
            "Primary user map written to {MapPath} with {EntryCount} entries.",
            mapPath,
            merged.Count);

        var defaultPath = context.Artifacts.GetDefaultUserMapPath();
        if (!string.Equals(defaultPath, mapPath, StringComparison.OrdinalIgnoreCase))
        {
            WriteUserMap(defaultPath, merged);
            _logger.LogInformation(
                "Synchronized user map copy written to {DefaultPath}.",
                defaultPath);
        }

        return Task.CompletedTask;
    }

    private static IReadOnlyList<IReadOnlyList<string>> BuildTemplateRows(IReadOnlyCollection<UserIdentifier> orphanUserIds)
    {
        var rows = new List<IReadOnlyList<string>>(Math.Max(orphanUserIds.Count + 1, 1))
        {
            new[] { "SourceUserId", "TargetUserId", "Rationale" }
        };

        foreach (var orphan in orphanUserIds.OrderBy(static value => value))
        {
            rows.Add(new[]
            {
                orphan.ToString(),
                string.Empty,
                string.Empty
            });
        }

        return rows;
    }

    private static IReadOnlyList<UserMappingEntry> MergeMappings(
        IReadOnlyCollection<UserIdentifier> orphanUserIds,
        IReadOnlyList<UserMappingEntry> existing,
        IReadOnlyList<UserMappingEntry> automatic)
    {
        var orphanSet = new SortedSet<UserIdentifier>(orphanUserIds);
        if (orphanSet.Count == 0)
        {
            return Array.Empty<UserMappingEntry>();
        }

        var bySource = new Dictionary<UserIdentifier, UserMappingEntry>();
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

        foreach (var entry in automatic)
        {
            if (!orphanSet.Contains(entry.SourceUserId))
            {
                continue;
            }

            if (!bySource.ContainsKey(entry.SourceUserId))
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

        if (existing.TargetUserId == candidate.TargetUserId && string.IsNullOrEmpty(existing.Rationale) && !string.IsNullOrEmpty(candidate.Rationale))
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
        WriteRow(writer, "SourceUserId", "TargetUserId", "Rationale");
        foreach (var entry in entries)
        {
            var target = entry.TargetUserId.HasValue
                ? entry.TargetUserId.Value.ToString()
                : string.Empty;
            var rationale = entry.Rationale ?? string.Empty;
            WriteRow(writer,
                entry.SourceUserId.ToString(),
                target,
                rationale);
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
