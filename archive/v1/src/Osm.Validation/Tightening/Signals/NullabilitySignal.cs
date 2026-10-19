using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Osm.Validation.Tightening.Signals;

internal abstract record NullabilitySignal
{
    protected NullabilitySignal(string code, string description)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("Signal code must be provided.", nameof(code));
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            throw new ArgumentException("Signal description must be provided.", nameof(description));
        }

        Code = code;
        Description = description;
    }

    public string Code { get; }

    public string Description { get; }

    public SignalEvaluation Evaluate(in NullabilitySignalContext context)
        => EvaluateCore(context);

    protected abstract SignalEvaluation EvaluateCore(in NullabilitySignalContext context);

    protected static ImmutableArray<string> MergeRationales(params IEnumerable<string>[] sources)
    {
        var builder = ImmutableArray.CreateBuilder<string>();
        foreach (var source in sources)
        {
            if (source is null)
            {
                continue;
            }

            foreach (var rationale in source)
            {
                builder.Add(rationale);
            }
        }

        return builder.ToImmutable();
    }
}
