using System.IO;
using System.Text;
using Osm.Domain.Abstractions;
using Osm.Pipeline.SqlExtraction;

namespace Osm.Pipeline.Tests.SqlExtraction;

public class SnapshotValidatorTests
{
    [Fact]
    public void Validate_ShouldReturnNullForValidPayload()
    {
        const string json = "{\"modules\": [{ \"name\": \"Inventory\", \"entities\": [{ \"name\": \"Product\", \"attributes\": [], \"relationships\": [], \"indexes\": [], \"triggers\": [] }]}]}";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var validator = new SnapshotValidator();

        var error = validator.Validate(stream);

        Assert.Null(error);
    }

    [Fact]
    public void Validate_ShouldReturnErrorWhenModulesMissing()
    {
        const string json = "{}";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var validator = new SnapshotValidator();

        var error = validator.Validate(stream);

        Assert.True(error.HasValue);
        Assert.Equal("extraction.sql.contract.modules", error.Value.Code);
    }

    [Fact]
    public void Validate_ShouldThrowWhenAttributesNull()
    {
        const string json = "{\"modules\": [{ \"name\": \"Inventory\", \"entities\": [{ \"name\": \"Product\", \"attributes\": null, \"relationships\": [], \"indexes\": [], \"triggers\": [] }]}]}";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var validator = new SnapshotValidator();

        var exception = Assert.Throws<InvalidDataException>(() => validator.Validate(stream));
        Assert.Contains("null attributes", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
