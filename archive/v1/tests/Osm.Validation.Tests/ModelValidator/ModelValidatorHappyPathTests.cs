using Osm.Validation;

namespace Osm.Validation.Tests.ModelValidation;

public class ModelValidatorHappyPathTests
{
    private readonly ModelValidator _validator = new();

    [Fact]
    public void Validate_ShouldReturnValidReport_ForEdgeCaseFixture()
    {
        var model = ValidationModelFixture.CreateValidModel();

        var report = _validator.Validate(model);

        Assert.True(report.IsValid);
        Assert.Empty(report.Messages);
    }
}
