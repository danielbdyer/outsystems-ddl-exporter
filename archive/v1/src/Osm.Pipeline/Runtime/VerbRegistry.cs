using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Osm.Pipeline.Runtime;

public interface IVerbRegistry
{
    IPipelineVerb Get(string verbName);
    bool TryGet(string verbName, out IPipelineVerb verb);
}

internal sealed class VerbRegistry : IVerbRegistry
{
    private readonly ImmutableDictionary<string, IPipelineVerb> _verbs;

    public VerbRegistry(IEnumerable<IPipelineVerb> verbs)
    {
        if (verbs is null)
        {
            throw new ArgumentNullException(nameof(verbs));
        }

        var builder = ImmutableDictionary.CreateBuilder<string, IPipelineVerb>(StringComparer.OrdinalIgnoreCase);
        foreach (var verb in verbs)
        {
            if (verb is null)
            {
                continue;
            }

            builder[verb.Name] = verb;
        }

        _verbs = builder.ToImmutable();
    }

    public IPipelineVerb Get(string verbName)
    {
        if (!TryGet(verbName, out var verb))
        {
            throw new KeyNotFoundException($"No pipeline verb registered with name '{verbName}'.");
        }

        return verb;
    }

    public bool TryGet(string verbName, out IPipelineVerb verb)
    {
        if (string.IsNullOrWhiteSpace(verbName))
        {
            verb = default!;
            return false;
        }

        return _verbs.TryGetValue(verbName, out verb!);
    }
}
