using System;
using System.Collections.Generic;

namespace Osm.Validation.Tightening;

internal sealed class ColumnAnalysisBuilder
{
    private readonly ColumnIdentity _identity;
    private NullabilityDecision? _nullability;
    private ForeignKeyDecision? _foreignKey;
    private readonly List<UniqueIndexDecision> _uniqueIndexes = new();

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

    public NullabilityDecision Nullability => _nullability ?? throw new InvalidOperationException("Nullability decision has not been computed.");

    public ForeignKeyDecision? ForeignKey => _foreignKey;

    public IReadOnlyList<UniqueIndexDecision> UniqueIndexes => _uniqueIndexes;
}
