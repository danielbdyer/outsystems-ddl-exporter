using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Osm.Validation.Tightening.Signals;

internal sealed record AnyOfSignal : NullabilitySignal
{
    private readonly ImmutableArray<NullabilitySignal> _signals;

    public AnyOfSignal(string code, string description, IEnumerable<NullabilitySignal> signals)
        : base(code, description)
    {
        if (signals is null)
        {
            throw new ArgumentNullException(nameof(signals));
        }

        _signals = signals.ToImmutableArray();
        if (_signals.IsDefaultOrEmpty)
        {
            throw new ArgumentException("At least one child signal is required.", nameof(signals));
        }
    }

    public AnyOfSignal(string code, string description, params NullabilitySignal[] signals)
        : this(code, description, (IEnumerable<NullabilitySignal>)signals)
    {
    }

    protected override SignalEvaluation EvaluateCore(in NullabilitySignalContext context)
    {
        var evaluations = ImmutableArray.CreateBuilder<SignalEvaluation>(_signals.Length);
        var result = false;

        foreach (var signal in _signals)
        {
            var evaluation = signal.Evaluate(context);
            evaluations.Add(evaluation);
            result |= evaluation.Result;
        }

        return SignalEvaluation.Create(Code, Description, result, children: evaluations.ToImmutable());
    }
}
