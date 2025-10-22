using Xunit;

namespace Osm.Etl.Integration.Tests;

public sealed class RequiresDockerFactAttribute : FactAttribute
{
    public RequiresDockerFactAttribute()
    {
        if (!DockerTestHelper.TryEnsureDocker(out var skipReason))
        {
            Skip = skipReason;
        }
    }
}
