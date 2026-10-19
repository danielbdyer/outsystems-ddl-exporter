using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Osm.Pipeline.Configuration;

namespace Osm.Pipeline.Tests.Configuration;

public sealed class SqlSectionReaderTests
{
    [Fact]
    public void TryRead_WhenSectionMissing_ReturnsFalse()
    {
        using var document = JsonDocument.Parse("{}");
        var reader = new SqlSectionReader();

        var success = reader.TryRead(document.RootElement, out var configuration);

        success.Should().BeFalse();
        configuration.Should().Be(SqlConfiguration.Empty);
    }

    [Fact]
    public void TryRead_WhenSectionPresent_ParsesConfiguration()
    {
        const string Json = """
        {
            "sql": {
                "connectionString": "Server=.;",
                "commandTimeoutSeconds": 42,
                "sampling": {
                    "rowSamplingThreshold": 1000,
                    "sampleSize": 5
                },
                "authentication": {
                    "method": "ActiveDirectoryManagedIdentity",
                    "trustServerCertificate": "true",
                    "applicationName": "Osm",
                    "accessToken": "token"
                },
                "metadataContract": {
                    "optionalColumns": {
                        "Users": ["DisplayName", "displayname", "Email"],
                        "Empty": [1]
                    }
                }
            }
        }
        """;

        using var document = JsonDocument.Parse(Json);
        var reader = new SqlSectionReader();

        var success = reader.TryRead(document.RootElement, out var configuration);

        success.Should().BeTrue();
        configuration.ConnectionString.Should().Be("Server=.;");
        configuration.CommandTimeoutSeconds.Should().Be(42);
        configuration.Sampling.RowSamplingThreshold.Should().Be(1000);
        configuration.Sampling.SampleSize.Should().Be(5);
        configuration.Authentication.Method.Should().Be(Microsoft.Data.SqlClient.SqlAuthenticationMethod.ActiveDirectoryManagedIdentity);
        configuration.Authentication.TrustServerCertificate.Should().BeTrue();
        configuration.Authentication.ApplicationName.Should().Be("Osm");
        configuration.Authentication.AccessToken.Should().Be("token");
        configuration.MetadataContract.OptionalColumns.Should().ContainKey("Users");
        configuration.MetadataContract.OptionalColumns["Users"].Should().BeEquivalentTo(new[] { "DisplayName", "Email" });
        configuration.MetadataContract.OptionalColumns.Should().NotContainKey("Empty");
    }
}
