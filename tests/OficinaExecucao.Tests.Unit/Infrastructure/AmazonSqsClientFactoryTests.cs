using Amazon;
using FluentAssertions;
using OficinaExecucao.Infrastructure.Messaging;
using Xunit;

namespace OficinaExecucao.Tests.Unit.Infrastructure;

public class AmazonSqsClientFactoryTests
{
    [Fact]
    public void BuildConfig_SemServiceUrl_UsaEndpointPadraoDaRegiao()
    {
        var config = AmazonSqsClientFactory.BuildConfig(new SqsOptions { QueueUrl = "https://sqs.test/q" });

        config.ServiceURL.Should().BeNull();
        config.RegionEndpoint.Should().Be(RegionEndpoint.USEast1);
    }

    [Fact]
    public void BuildConfig_ComServiceUrl_ApontaParaOEndpointCustomizado()
    {
        var config = AmazonSqsClientFactory.BuildConfig(new SqsOptions { QueueUrl = "https://sqs.test/q", ServiceUrl = "http://localhost:4566" });

        config.ServiceURL.Should().Be("http://localhost:4566/");
        config.UseHttp.Should().BeTrue();
        config.AuthenticationRegion.Should().Be("us-east-1");
    }
}
