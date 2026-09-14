using System.Text.Json;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using OficinaExecucao.Infrastructure.Messaging;
using Xunit;

namespace OficinaExecucao.Tests.Unit.Infrastructure;

public class SnsEventPublisherTests
{
    [Fact]
    public async Task PublicarAsync_PublicaNoTopicoCorretoComOEnvelopePadrao()
    {
        var client = new Mock<IAmazonSimpleNotificationService>();
        PublishRequest? capturado = null;
        client
            .Setup(c => c.PublishAsync(It.IsAny<PublishRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PublishRequest, CancellationToken>((req, _) => capturado = req)
            .ReturnsAsync(new PublishResponse());

        var options = Options.Create(new SnsOptions
        {
            TopicArns = new Dictionary<string, string> { ["diagnostico.concluido"] = "arn:aws:sns:us-east-1:123:diagnostico-concluido" }
        });

        var publisher = new SnsEventPublisher(client.Object, options);
        await publisher.PublicarAsync("diagnostico.concluido", new { osId = "abc" }, CancellationToken.None);

        capturado.Should().NotBeNull();
        capturado!.TopicArn.Should().Be("arn:aws:sns:us-east-1:123:diagnostico-concluido");

        using var envelope = JsonDocument.Parse(capturado.Message);
        envelope.RootElement.GetProperty("eventType").GetString().Should().Be("diagnostico.concluido");
        envelope.RootElement.GetProperty("producer").GetString().Should().Be("execucao-service");
        envelope.RootElement.GetProperty("data").GetProperty("osId").GetString().Should().Be("abc");
        Guid.TryParse(envelope.RootElement.GetProperty("eventId").GetString(), out _).Should().BeTrue();
    }

    [Fact]
    public async Task PublicarAsync_TopicoNaoConfigurado_LancaInvalidOperation()
    {
        var client = new Mock<IAmazonSimpleNotificationService>();
        var options = Options.Create(new SnsOptions { TopicArns = new Dictionary<string, string>() });

        var publisher = new SnsEventPublisher(client.Object, options);
        var act = async () => await publisher.PublicarAsync("evento.sem.topico", new { }, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
