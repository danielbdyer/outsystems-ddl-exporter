using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Osm.Domain.Configuration;
using Osm.Domain.Model;
using Osm.Domain.ValueObjects;

namespace Osm.Validation.Tightening;

public static class EntityLookupResolver
{
    public static EntityLookupResolution Resolve(OsmModel model, NamingOverrideOptions namingOverrides)
    {
        if (model is null)
        {
            throw new ArgumentNullException(nameof(model));
        }

        if (namingOverrides is null)
        {
            throw new ArgumentNullException(nameof(namingOverrides));
        }

        var lookup = new Dictionary<EntityName, EntityModel>();
        var diagnostics = ImmutableArray.CreateBuilder<TighteningDiagnostic>();

        var groups = model.Modules
            .SelectMany(static module => module.Entities, (module, entity) => new { Module = module, Entity = entity })
            .GroupBy(x => x.Entity.LogicalName);

        foreach (var group in groups)
        {
            var candidates = group
                .Select(x => new DuplicateCandidate(x.Module, x.Entity))
                .ToArray();

            if (candidates.Length == 0)
            {
                continue;
            }

            if (candidates.Length == 1)
            {
                lookup[group.Key] = candidates[0].Entity;
                continue;
            }

            var resolution = ResolveDuplicates(group.Key, candidates, namingOverrides);
            lookup[group.Key] = resolution.Canonical.Entity;
            diagnostics.Add(resolution.Diagnostic);
        }

        return new EntityLookupResolution(lookup, diagnostics.ToImmutable());
    }

    private static DuplicateResolution ResolveDuplicates(
        EntityName logicalName,
        IReadOnlyList<DuplicateCandidate> candidates,
        NamingOverrideOptions namingOverrides)
    {
        var moduleOverrideMatches = candidates
            .Where(candidate => namingOverrides.TryGetModuleScopedEntityOverride(
                candidate.Module.Name.Value,
                logicalName.Value,
                out _))
            .ToArray();

        DuplicateCandidate canonical;
        bool resolvedByOverride;
        string code;
        string message;
        TighteningDiagnosticSeverity severity;

        if (moduleOverrideMatches.Length == 1)
        {
            canonical = moduleOverrideMatches[0];
            resolvedByOverride = true;
            severity = TighteningDiagnosticSeverity.Info;
            code = "tightening.entity.duplicate.resolved";
            message = BuildResolvedDuplicateMessage(logicalName.Value, canonical.Module.Name.Value, candidates);
        }
        else
        {
            canonical = candidates
                .OrderBy(c => c.Module.Name.Value, StringComparer.OrdinalIgnoreCase)
                .ThenBy(c => c.Entity.Schema.Value, StringComparer.OrdinalIgnoreCase)
                .ThenBy(c => c.Entity.PhysicalName.Value, StringComparer.OrdinalIgnoreCase)
                .First();

            resolvedByOverride = false;
            severity = TighteningDiagnosticSeverity.Warning;
            code = moduleOverrideMatches.Length > 1
                ? "tightening.entity.duplicate.conflict"
                : "tightening.entity.duplicate.unresolved";
            message = BuildUnresolvedDuplicateMessage(
                logicalName.Value,
                canonical.Module.Name.Value,
                moduleOverrideMatches.Length > 1,
                candidates);
        }

        var candidateRecords = candidates
            .OrderBy(c => c.Module.Name.Value, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.Entity.Schema.Value, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.Entity.PhysicalName.Value, StringComparer.OrdinalIgnoreCase)
            .Select(c => new TighteningDuplicateCandidate(
                c.Module.Name.Value,
                c.Entity.Schema.Value,
                c.Entity.PhysicalName.Value))
            .ToImmutableArray();

        var diagnostic = new TighteningDiagnostic(
            code,
            message,
            severity,
            logicalName.Value,
            canonical.Module.Name.Value,
            canonical.Entity.Schema.Value,
            canonical.Entity.PhysicalName.Value,
            candidateRecords,
            resolvedByOverride);

        return new DuplicateResolution(canonical, diagnostic);
    }

    private static string BuildResolvedDuplicateMessage(
        string logicalName,
        string canonicalModule,
        IReadOnlyList<DuplicateCandidate> candidates)
    {
        var modules = string.Join(", ", candidates.Select(c => c.Module.Name.Value).Distinct(StringComparer.OrdinalIgnoreCase));
        return $"Entity logical name '{logicalName}' appears in modules [{modules}]. Canonical module '{canonicalModule}' was selected via module-scoped naming override.";
    }

    private static string BuildUnresolvedDuplicateMessage(
        string logicalName,
        string canonicalModule,
        bool conflictingOverrides,
        IReadOnlyList<DuplicateCandidate> candidates)
    {
        var modules = string.Join(", ", candidates.Select(c => c.Module.Name.Value).Distinct(StringComparer.OrdinalIgnoreCase));
        if (conflictingOverrides)
        {
            return $"Entity logical name '{logicalName}' has multiple module-scoped naming overrides across modules [{modules}]. Canonical module '{canonicalModule}' was selected by deterministic ordering.";
        }

        return $"Entity logical name '{logicalName}' appears in modules [{modules}] without a module-scoped naming override. Canonical module '{canonicalModule}' was selected by deterministic ordering.";
    }

    private sealed record DuplicateCandidate(ModuleModel Module, EntityModel Entity);

    private sealed record DuplicateResolution(DuplicateCandidate Canonical, TighteningDiagnostic Diagnostic);
}

public sealed record EntityLookupResolution(
    IReadOnlyDictionary<EntityName, EntityModel> Lookup,
    ImmutableArray<TighteningDiagnostic> Diagnostics);
