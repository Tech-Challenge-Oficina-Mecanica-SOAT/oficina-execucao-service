using Amazon;
using FluentAssertions;
using OficinaExecucao.Infrastructure.DynamoDb;
using Xunit;

namespace OficinaExecucao.Tests.Unit.Infrastructure;

public class AmazonDynamoDbClientFactoryTests
{
    [Fact]
    public void BuildConfig_SemServiceUrl_UsaEndpointPadraoDaRegiao()
    {
        var config = AmazonDynamoDbClientFactory.BuildConfig(new DynamoDbOptions { TableName = "t" });

        config.ServiceURL.Should().BeNull();
        config.RegionEndpoint.Should().Be(RegionEndpoint.USEast1);
    }

    [Fact]
    public void BuildConfig_ComServiceUrl_ApontaParaOEndpointCustomizado()
    {
        var config = AmazonDynamoDbClientFactory.BuildConfig(new DynamoDbOptions { TableName = "t", ServiceUrl = "http://localhost:4566" });

        config.ServiceURL.Should().Be("http://localhost:4566/");
        config.UseHttp.Should().BeTrue();
    }
}
