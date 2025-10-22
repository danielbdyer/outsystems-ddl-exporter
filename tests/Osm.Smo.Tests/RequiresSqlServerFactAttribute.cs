using Xunit;

namespace Osm.Smo.Tests;

public sealed class RequiresSqlServerFactAttribute : FactAttribute
{
    public RequiresSqlServerFactAttribute()
    {
        if (!SmoTestHelper.TryEnsureSqlServer(out var skipReason))
        {
            Skip = skipReason;
        }
    }
}
