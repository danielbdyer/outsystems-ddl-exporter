using System;
using System.Collections.Immutable;
using System.Linq;
using Osm.Domain.Abstractions;

namespace Osm.Domain.Configuration;

/// <summary>
/// Configuration for handling circular dependencies.
/// </summary>
public sealed record CircularDependencyOptions
{
    public ImmutableArray<AllowedCycle> AllowedCycles { get; init; } = ImmutableArray<AllowedCycle>.Empty;

    /// <summary>
    /// When true, fail export on ANY cycle (even if allowed). Default: false.
    /// </summary>
    public bool StrictMode { get; init; } = false;

    public static CircularDependencyOptions Empty { get; } = new();

    public static Result<CircularDependencyOptions> Create(ImmutableArray<AllowedCycle> allowedCycles, bool strictMode = false)
    {
        // Validate no duplicate table ordering positions
        var duplicatePositions = allowedCycles
            .SelectMany(c => c.TableOrdering)
            .GroupBy(t => t.Position)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToArray();

        if (duplicatePositions.Any())
        {
            return Result<CircularDependencyOptions>.Failure(
                ValidationError.Create(
                    "CircularDependencyOptions.DuplicatePositions",
                    $"Duplicate ordering positions detected: {string.Join(", ", duplicatePositions)}. Each table must have a unique position."));
        }

        return Result<CircularDependencyOptions>.Success(new CircularDependencyOptions
        {
            AllowedCycles = allowedCycles,
            StrictMode = strictMode
        });
    }

    /// <summary>
    /// Checks if a cycle involving these tables is allowed.
    /// </summary>
    public bool IsCycleAllowed(ImmutableArray<string> tablesInCycle)
    {
        if (StrictMode)
        {
            return false;
        }

        foreach (var allowed in AllowedCycles)
        {
            // Check if the detected cycle matches this allowlist entry
            // Match if all tables in the allowlist are present in the cycle
            var allTablesMatch = allowed.TableOrdering
                .All(t => tablesInCycle.Contains(t.TableName, StringComparer.OrdinalIgnoreCase));

            if (allTablesMatch && allowed.TableOrdering.Length == tablesInCycle.Length)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Gets the manual ordering for a specific table, if configured.
    /// Returns null if no manual ordering is specified.
    /// </summary>
    public int? GetManualPosition(string tableName)
    {
        foreach (var cycle in AllowedCycles)
        {
            var tableOrdering = cycle.TableOrdering.FirstOrDefault(t =>
                t.TableName.Equals(tableName, StringComparison.OrdinalIgnoreCase));

            if (tableOrdering != null)
            {
                return tableOrdering.Position;
            }
        }

        return null;
    }
}

/// <summary>
/// Represents an allowed circular dependency with manual ordering control.
/// </summary>
public sealed record AllowedCycle
{
    /// <summary>
    /// Tables involved in the cycle with explicit ordering positions (z-index style).
    /// </summary>
    public ImmutableArray<TableOrdering> TableOrdering { get; init; } = ImmutableArray<TableOrdering>.Empty;

    public static Result<AllowedCycle> Create(ImmutableArray<TableOrdering> tableOrdering)
    {
        if (tableOrdering.IsDefaultOrEmpty)
        {
            return Result<AllowedCycle>.Failure(
                ValidationError.Create("AllowedCycle.MissingTables", "At least one table must be specified in allowed cycle"));
        }

        return Result<AllowedCycle>.Success(new AllowedCycle
        {
            TableOrdering = tableOrdering
        });
    }
}

/// <summary>
/// Specifies explicit ordering for a table within a circular dependency.
/// Lower position numbers load first (like z-index).
/// </summary>
public sealed record TableOrdering
{
    /// <summary>
    /// Physical table name (e.g., "OSUSR_USER").
    /// </summary>
    public string TableName { get; init; } = string.Empty;

    /// <summary>
    /// Explicit load position. Lower numbers load first.
    /// Example: Organization=100, User=200 → Organization loads before User.
    /// </summary>
    public int Position { get; init; }

    public static Result<TableOrdering> Create(string tableName, int position)
    {
        if (string.IsNullOrWhiteSpace(tableName))
        {
            return Result<TableOrdering>.Failure(
                ValidationError.Create("TableOrdering.MissingTableName", "Table name is required"));
        }

        if (position < 0)
        {
            return Result<TableOrdering>.Failure(
                ValidationError.Create("TableOrdering.InvalidPosition", "Position must be non-negative"));
        }

        return Result<TableOrdering>.Success(new TableOrdering
        {
            TableName = tableName.Trim(),
            Position = position
        });
    }
}
