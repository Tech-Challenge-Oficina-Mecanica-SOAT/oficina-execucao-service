using Amazon;
using FluentAssertions;
using OficinaExecucao.Infrastructure.Messaging;
using Xunit;

namespace OficinaExecucao.Tests.Unit.Infrastructure;

public class AmazonSnsClientFactoryTests
{
    [Fact]
    public void BuildConfig_SemServiceUrl_UsaEndpointPadraoDaRegiao()
    {
        var config = AmazonSnsClientFactory.BuildConfig(new SnsOptions { TopicArns = new Dictionary<string, string>() });

        config.ServiceURL.Should().BeNull();
        config.RegionEndpoint.Should().Be(RegionEndpoint.USEast1);
    }

    [Fact]
    public void BuildConfig_ComServiceUrl_ApontaParaOEndpointCustomizado()
    {
        var config = AmazonSnsClientFactory.BuildConfig(new SnsOptions { TopicArns = new Dictionary<string, string>(), ServiceUrl = "http://localhost:4566" });

        config.ServiceURL.Should().Be("http://localhost:4566/");
        config.UseHttp.Should().BeTrue();
    }
}
