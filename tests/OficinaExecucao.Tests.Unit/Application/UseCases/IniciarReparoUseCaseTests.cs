using FluentAssertions;
using Moq;
using OficinaExecucao.Application;
using OficinaExecucao.Application.UseCases;
using OficinaExecucao.Domain;
using Xunit;

namespace OficinaExecucao.Tests.Unit.Application.UseCases;

public class IniciarReparoUseCaseTests
{
    [Fact]
    public async Task ExecutarAsync_QuandoAguardandoReparo_SalvaEPublicaExecucaoIniciada()
    {
        var osId = Guid.NewGuid();
        var execucao = Execucao.Reidratar(osId, StatusExecucao.AguardandoReparo, Guid.NewGuid(), null,
            Array.Empty<ItemPeca>(), Array.Empty<ItemServico>(), null, null, null, null, null);
        var repositorio = new Mock<IExecucaoRepository>();
        repositorio.Setup(r => r.ObterPorOsIdAsync(osId, It.IsAny<CancellationToken>())).ReturnsAsync(execucao);
        var publisher = new Mock<IEventPublisher>();

        var useCase = new IniciarReparoUseCase(repositorio.Object, publisher.Object);
        var resultado = await useCase.ExecutarAsync(osId, CancellationToken.None);

        resultado.Status.Should().Be(StatusExecucao.EmReparo);
        publisher.Verify(p => p.PublicarAsync("execucao.iniciada", It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecutarAsync_QuandoOsNaoExiste_LancaKeyNotFound()
    {
        var osId = Guid.NewGuid();
        var repositorio = new Mock<IExecucaoRepository>();
        repositorio.Setup(r => r.ObterPorOsIdAsync(osId, It.IsAny<CancellationToken>())).ReturnsAsync((Execucao?)null);

        var useCase = new IniciarReparoUseCase(repositorio.Object, Mock.Of<IEventPublisher>());
        var act = async () => await useCase.ExecutarAsync(osId, CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
