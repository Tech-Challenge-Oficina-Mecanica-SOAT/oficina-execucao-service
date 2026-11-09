using System.Net;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Moq;
using OficinaExecucao.Application;
using Xunit;

namespace OficinaExecucao.Tests.Integration.Api;

[Collection("Integration")]
public class HealthEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HealthEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.RemoveAll<IEventPublisher>();
                services.AddScoped<IEventPublisher, NoOpEventPublisher>();

                var dynamoDbMock = new Mock<IAmazonDynamoDB>();
                dynamoDbMock
                    .Setup(c => c.DescribeTableAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new DescribeTableResponse { Table = new TableDescription { TableStatus = TableStatus.ACTIVE } });
                services.RemoveAll<IAmazonDynamoDB>();
                services.AddSingleton(dynamoDbMock.Object);
            });
        });
    }

    [Fact]
    public async Task GetHealthLive_RetornaOk()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetHealthReady_QuandoDynamoDbResponde_RetornaOk()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health/ready");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetHealthReady_QuandoDynamoDbFalha_Retorna503()
    {
        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var dynamoDbMock = new Mock<IAmazonDynamoDB>();
                dynamoDbMock
                    .Setup(c => c.DescribeTableAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .ThrowsAsync(new AmazonDynamoDBException("tabela indisponivel"));
                services.RemoveAll<IAmazonDynamoDB>();
                services.AddSingleton(dynamoDbMock.Object);
            });
        }).CreateClient();

        var response = await client.GetAsync("/health/ready");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }
}
