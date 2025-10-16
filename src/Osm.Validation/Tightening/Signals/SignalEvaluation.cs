using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Osm.Validation.Tightening.Signals;

public sealed record SignalEvaluation(
    string Code,
    string Description,
    bool Result,
    ImmutableArray<string> Rationales,
    ImmutableArray<SignalEvaluation> Children)
{
    public static SignalEvaluation Create(
        string code,
        string description,
        bool result,
        IEnumerable<string>? rationales = null,
        IEnumerable<SignalEvaluation>? children = null)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("Signal code must be provided.", nameof(code));
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            throw new ArgumentException("Signal description must be provided.", nameof(description));
        }

        var rationaleArray = rationales switch
        {
            null => ImmutableArray<string>.Empty,
            ImmutableArray<string> array => array,
            _ => rationales.ToImmutableArray(),
        };

        var childArray = children switch
        {
            null => ImmutableArray<SignalEvaluation>.Empty,
            ImmutableArray<SignalEvaluation> array => array,
            _ => children.ToImmutableArray(),
        };

        return new SignalEvaluation(code, description, result, rationaleArray, childArray);
    }

    public IEnumerable<string> CollectRationales()
    {
        foreach (var rationale in Rationales)
        {
            yield return rationale;
        }

        foreach (var child in Children)
        {
            foreach (var childRationale in child.CollectRationales())
            {
                yield return childRationale;
            }
        }
    }

    public bool ContainsSatisfiedCode(ISet<string> codes)
    {
        if (codes.Contains(Code) && Result)
        {
            return true;
        }

        return Children.Any(child => child.ContainsSatisfiedCode(codes));
    }

    public bool ContainsCode(string code)
    {
        if (string.Equals(Code, code, StringComparison.Ordinal))
        {
            return true;
        }

        return Children.Any(child => child.ContainsCode(code));
    }

    public SignalEvaluation AppendChild(SignalEvaluation child)
    {
        if (child is null)
        {
            throw new ArgumentNullException(nameof(child));
        }

        return this with { Children = Children.Add(child) };
    }
}
