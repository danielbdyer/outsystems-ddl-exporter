using System;
using System.Collections.Generic;
using Osm.Validation.Tightening.Opportunities;

namespace Osm.Validation.Tightening;

internal sealed class ColumnAnalysisBuilder
{
    private readonly ColumnIdentity _identity;
    private NullabilityDecision? _nullability;
    private ForeignKeyDecision? _foreignKey;
    private readonly List<UniqueIndexDecision> _uniqueIndexes = new();
    private readonly List<Opportunity> _opportunities = new();

    public ColumnAnalysisBuilder(ColumnIdentity identity)
    {
        _identity = identity;
    }

    public void SetNullability(NullabilityDecision decision)
    {
        _nullability = decision ?? throw new ArgumentNullException(nameof(decision));
    }

    public void SetForeignKey(ForeignKeyDecision decision)
    {
        _foreignKey = decision ?? throw new ArgumentNullException(nameof(decision));
    }

    public void AddUniqueDecision(UniqueIndexDecision decision)
    {
        if (decision is null)
        {
            throw new ArgumentNullException(nameof(decision));
        }

        if (!_uniqueIndexes.Contains(decision))
        {
            _uniqueIndexes.Add(decision);
        }
    }

    public void AddOpportunity(Opportunity opportunity)
    {
        if (opportunity is null)
        {
            throw new ArgumentNullException(nameof(opportunity));
        }

        _opportunities.Add(opportunity);
    }

    public NullabilityDecision Nullability => _nullability ?? throw new InvalidOperationException("Nullability decision has not been computed.");

    public ForeignKeyDecision? ForeignKey => _foreignKey;

    public IReadOnlyList<UniqueIndexDecision> UniqueIndexes => _uniqueIndexes;

    public IReadOnlyList<Opportunity> Opportunities => _opportunities;

    public ColumnAnalysis Build()
    {
        if (_nullability is null)
        {
            throw new InvalidOperationException("Nullability decision must be populated before building analysis.");
        }

        return ColumnAnalysis.Create(_identity, _nullability, _foreignKey, _uniqueIndexes, _opportunities);
    }
}
