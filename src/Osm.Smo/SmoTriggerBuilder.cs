using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Osm.Smo;

internal static class SmoTriggerBuilder
{
    public static ImmutableArray<SmoTriggerDefinition> Build(EntityEmissionContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (context.Entity.Triggers.IsDefaultOrEmpty)
        {
            return ImmutableArray<SmoTriggerDefinition>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<SmoTriggerDefinition>(context.Entity.Triggers.Length);
        foreach (var trigger in context.Entity.Triggers)
        {
            if (trigger is null)
            {
                continue;
            }

            builder.Add(new SmoTriggerDefinition(
                SmoNormalization.NormalizeWhitespace(trigger.Name.Value) ?? trigger.Name.Value,
                context.Entity.Schema.Value,
                context.Entity.PhysicalName.Value,
                trigger.IsDisabled,
                SmoNormalization.NormalizeSqlExpression(trigger.Definition) ?? trigger.Definition));
        }

        var triggers = builder.ToImmutable();
        if (!triggers.IsDefaultOrEmpty)
        {
            triggers = triggers.Sort(SmoTriggerDefinitionComparer.Instance);
        }

        return triggers;
    }

    private sealed class SmoTriggerDefinitionComparer : IComparer<SmoTriggerDefinition>
    {
        public static readonly SmoTriggerDefinitionComparer Instance = new();

        public int Compare(SmoTriggerDefinition? x, SmoTriggerDefinition? y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }

            if (x is null)
            {
                return -1;
            }

            if (y is null)
            {
                return 1;
            }

            return StringComparer.OrdinalIgnoreCase.Compare(x.Name, y.Name);
        }
    }
}
