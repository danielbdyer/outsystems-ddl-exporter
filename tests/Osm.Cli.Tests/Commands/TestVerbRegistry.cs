using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Osm.Pipeline.Runtime;

namespace Osm.Cli.Tests.Commands;

internal static class TestVerbRegistry
{
    public static IVerbRegistry Create(IServiceProvider provider)
    {
        var verbs = provider.GetServices<IPipelineVerb>();
        return new InMemoryVerbRegistry(verbs);
    }

    private sealed class InMemoryVerbRegistry : IVerbRegistry
    {
        private readonly Dictionary<string, IPipelineVerb> _verbs;

        public InMemoryVerbRegistry(IEnumerable<IPipelineVerb> verbs)
        {
            if (verbs is null)
            {
                throw new ArgumentNullException(nameof(verbs));
            }

            _verbs = new Dictionary<string, IPipelineVerb>(StringComparer.OrdinalIgnoreCase);
            foreach (var verb in verbs)
            {
                if (verb is null)
                {
                    continue;
                }

                _verbs[verb.Name] = verb;
            }
        }

        public IPipelineVerb Get(string verbName)
        {
            if (string.IsNullOrWhiteSpace(verbName))
            {
                throw new ArgumentException("Verb name must be provided.", nameof(verbName));
            }

            if (!_verbs.TryGetValue(verbName, out var verb))
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
}
