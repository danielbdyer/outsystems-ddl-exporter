using System;
using System.Linq;
using System.Threading.Tasks;
using Osm.Pipeline.Orchestration;
using Tests.Support;
using Xunit;

namespace Osm.Pipeline.Tests;

public class SupplementalEntityLoaderTests
{
    [Fact]
    public async Task LoadAsync_includes_built_in_user_model_when_enabled()
    {
        var loader = new SupplementalEntityLoader();
        var options = new SupplementalModelOptions(true, Array.Empty<string>());
        var originalDirectory = Environment.CurrentDirectory;

        try
        {
            Environment.CurrentDirectory = FixtureFile.RepositoryRoot;
            var result = await loader.LoadAsync(options);
            Assert.True(result.IsSuccess, string.Join(
                Environment.NewLine,
                result.Errors.Select(e => $"{e.Code}: {e.Message}")));

            var entities = result.Value;
            var user = Assert.Single(
                entities,
                entity => entity.PhysicalName.Value.Equals("ossys_User", StringComparison.OrdinalIgnoreCase));

            Assert.Equal("UserExtension_CS", user.Module.Value);
            Assert.Contains(
                user.Indexes,
                index => index.Name.Value.Equals("PK_ossys_User", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Environment.CurrentDirectory = originalDirectory;
        }
    }
}
