using System;
using System.Collections.Generic;
using System.Linq;

namespace Osm.Pipeline.Hosting;

public interface IVerbRegistry
{
    IPipelineVerb Get(string name);
}

public sealed class VerbRegistry : IVerbRegistry
{
    private readonly Dictionary<string, IPipelineVerb> _verbs;

    public VerbRegistry(IEnumerable<IPipelineVerb> verbs)
    {
        if (verbs is null)
        {
            throw new ArgumentNullException(nameof(verbs));
        }

        _verbs = verbs.ToDictionary(static verb => verb.Name, StringComparer.OrdinalIgnoreCase);
    }

    public IPipelineVerb Get(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Verb name must be provided.", nameof(name));
        }

        if (_verbs.TryGetValue(name, out var verb))
        {
            return verb;
        }

        throw new InvalidOperationException($"Pipeline verb '{name}' is not registered.");
    }
}
