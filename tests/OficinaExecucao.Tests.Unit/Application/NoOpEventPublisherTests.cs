using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using OficinaExecucao.Application;
using Xunit;

namespace OficinaExecucao.Tests.Unit.Application;

public class NoOpEventPublisherTests
{
    [Fact]
    public async Task PublicarAsync_NaoLancaExcecao()
    {
        var mockLogger = new Mock<ILogger<NoOpEventPublisher>>();
        var publisher = new NoOpEventPublisher(mockLogger.Object);

        var act = async () => await publisher.PublicarAsync("diagnostico.concluido", new { osId = Guid.NewGuid() }, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }
}
