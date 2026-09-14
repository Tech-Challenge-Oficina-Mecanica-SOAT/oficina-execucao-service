using FluentAssertions;
using Moq;
using OficinaExecucao.Application;
using OficinaExecucao.Application.UseCases;
using OficinaExecucao.Domain;
using Xunit;

namespace OficinaExecucao.Tests.Unit.Application.UseCases;

public class FinalizarUseCaseTests
{
    [Fact]
    public async Task ExecutarAsync_QuandoEmReparo_SalvaEPublicaExecucaoFinalizada()
    {
        var osId = Guid.NewGuid();
        var execucao = Execucao.Reidratar(osId, StatusExecucao.EmReparo, Guid.NewGuid(), Guid.NewGuid(),
            Array.Empty<ItemPeca>(), Array.Empty<ItemServico>(), null, null, DateTimeOffset.UtcNow, null, null, DateTimeOffset.UtcNow);
        var repositorio = new Mock<IExecucaoRepository>();
        repositorio.Setup(r => r.ObterPorOsIdAsync(osId, It.IsAny<CancellationToken>())).ReturnsAsync(execucao);
        var publisher = new Mock<IEventPublisher>();

        var useCase = new FinalizarUseCase(repositorio.Object, publisher.Object);
        var resultado = await useCase.ExecutarAsync(osId, 2.5m, CancellationToken.None);

        resultado.Status.Should().Be(StatusExecucao.Finalizado);
        publisher.Verify(p => p.PublicarAsync("execucao.finalizada", It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecutarAsync_QuandoOsNaoExiste_LancaKeyNotFound()
    {
        var osId = Guid.NewGuid();
        var repositorio = new Mock<IExecucaoRepository>();
        repositorio.Setup(r => r.ObterPorOsIdAsync(osId, It.IsAny<CancellationToken>())).ReturnsAsync((Execucao?)null);

        var useCase = new FinalizarUseCase(repositorio.Object, Mock.Of<IEventPublisher>());
        var act = async () => await useCase.ExecutarAsync(osId, 1m, CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
