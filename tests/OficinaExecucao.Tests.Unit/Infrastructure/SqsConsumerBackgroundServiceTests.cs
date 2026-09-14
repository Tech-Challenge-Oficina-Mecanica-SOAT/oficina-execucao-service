using Amazon.SQS;
using Amazon.SQS.Model;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using OficinaExecucao.Application;
using OficinaExecucao.Infrastructure.Messaging;
using Xunit;

namespace OficinaExecucao.Tests.Unit.Infrastructure;

public class SqsConsumerBackgroundServiceTests
{
    private static (Mock<IServiceScopeFactory> factory, Mock<IMensagemDispatcher> dispatcher) CriarScopeFactory()
    {
        var dispatcher = new Mock<IMensagemDispatcher>();
        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider.Setup(p => p.GetService(typeof(IMensagemDispatcher))).Returns(dispatcher.Object);
        var scope = new Mock<IServiceScope>();
        scope.Setup(s => s.ServiceProvider).Returns(serviceProvider.Object);
        var factory = new Mock<IServiceScopeFactory>();
        factory.Setup(f => f.CreateScope()).Returns(scope.Object);
        return (factory, dispatcher);
    }

    [Fact]
    public async Task ProcessarLoteAsync_ProcessaEDeletaMensagemComSucesso()
    {
        var sqsClient = new Mock<IAmazonSQS>();
        sqsClient
            .Setup(c => c.ReceiveMessageAsync(It.IsAny<ReceiveMessageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReceiveMessageResponse
            {
                Messages = [new Message { Body = "{}", ReceiptHandle = "rh-1", MessageId = "m-1" }]
            });
        var (scopeFactory, dispatcher) = CriarScopeFactory();
        var options = Options.Create(new SqsOptions { QueueUrl = "https://sqs.test/queue" });

        var service = new SqsConsumerBackgroundService(sqsClient.Object, options, scopeFactory.Object, NullLogger<SqsConsumerBackgroundService>.Instance);
        await service.ProcessarLoteAsync(CancellationToken.None);

        dispatcher.Verify(d => d.ProcessarAsync("{}", It.IsAny<CancellationToken>()), Times.Once);
        sqsClient.Verify(c => c.DeleteMessageAsync("https://sqs.test/queue", "rh-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessarLoteAsync_QuandoDispatcherFalha_NaoDeletaAMensagem()
    {
        var sqsClient = new Mock<IAmazonSQS>();
        sqsClient
            .Setup(c => c.ReceiveMessageAsync(It.IsAny<ReceiveMessageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReceiveMessageResponse
            {
                Messages = [new Message { Body = "{}", ReceiptHandle = "rh-2", MessageId = "m-2" }]
            });
        var (scopeFactory, dispatcher) = CriarScopeFactory();
        dispatcher.Setup(d => d.ProcessarAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("falhou"));
        var options = Options.Create(new SqsOptions { QueueUrl = "https://sqs.test/queue" });

        var service = new SqsConsumerBackgroundService(sqsClient.Object, options, scopeFactory.Object, NullLogger<SqsConsumerBackgroundService>.Instance);
        var act = async () => await service.ProcessarLoteAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
        sqsClient.Verify(c => c.DeleteMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
