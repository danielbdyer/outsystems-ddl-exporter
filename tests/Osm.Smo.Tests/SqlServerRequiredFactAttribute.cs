using System;
using Xunit;

namespace Osm.Smo.Tests;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
internal sealed class SqlServerRequiredFactAttribute : FactAttribute
{
    public SqlServerRequiredFactAttribute()
    {
        if (!SmoSqlServerRequirement.IsAvailable(out var reason))
        {
            Skip = string.IsNullOrWhiteSpace(reason)
                ? "SQL Server instance unavailable for SMO tests."
                : $"SQL Server instance unavailable for SMO tests: {reason}";
        }
    }
}
