using System.Collections.Immutable;
using System.Linq;

using Osm.Domain.Abstractions;

namespace Osm.Domain.Model;

public sealed record OsmModel(
    DateTime ExportedAtUtc,
    ImmutableArray<ModuleModel> Modules)
{
    public static Result<OsmModel> Create(DateTime exportedAtUtc, IEnumerable<ModuleModel> modules)
    {
        if (modules is null)
        {
            throw new ArgumentNullException(nameof(modules));
        }

        var materialized = modules.ToImmutableArray();
        if (materialized.IsDefaultOrEmpty)
        {
            return Result<OsmModel>.Failure(ValidationError.Create("model.modules.empty", "Model must include at least one module."));
        }

        if (materialized.Select(m => m.Name.Value).Distinct(StringComparer.OrdinalIgnoreCase).Count() != materialized.Length)
        {
            return Result<OsmModel>.Failure(ValidationError.Create("model.modules.duplicate", "Duplicate module names detected."));
        }

        return Result<OsmModel>.Success(new OsmModel(exportedAtUtc, materialized));
    }
}
