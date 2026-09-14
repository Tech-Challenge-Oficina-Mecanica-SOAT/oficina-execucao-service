using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OficinaExecucao.Application;
using OficinaExecucao.Application.UseCases;
using OficinaExecucao.Domain;
using Xunit;

namespace OficinaExecucao.Tests.Unit.Application.UseCases;

public class ProcessarOsCanceladaUseCaseTests
{
    [Fact]
    public async Task ExecutarAsync_QuandoExecucaoExiste_Cancela()
    {
        var osId = Guid.NewGuid();
        var execucao = Execucao.NovaNaFila(osId);
        var repositorio = new Mock<IExecucaoRepository>();
        repositorio.Setup(r => r.ObterPorOsIdAsync(osId, It.IsAny<CancellationToken>())).ReturnsAsync(execucao);

        var useCase = new ProcessarOsCanceladaUseCase(repositorio.Object, NullLogger<ProcessarOsCanceladaUseCase>.Instance);
        await useCase.ExecutarAsync(osId, "evt-2", CancellationToken.None);

        execucao.Status.Should().Be(StatusExecucao.Cancelado);
        repositorio.Verify(r => r.SalvarAsync(execucao, It.IsAny<HistoricoEntry>(), It.IsAny<CancellationToken>(), "evt-2"), Times.Once);
    }

    [Fact]
    public async Task ExecutarAsync_QuandoExecucaoNaoExiste_IgnoraSemErro()
    {
        var osId = Guid.NewGuid();
        var repositorio = new Mock<IExecucaoRepository>();
        repositorio.Setup(r => r.ObterPorOsIdAsync(osId, It.IsAny<CancellationToken>())).ReturnsAsync((Execucao?)null);

        var useCase = new ProcessarOsCanceladaUseCase(repositorio.Object, NullLogger<ProcessarOsCanceladaUseCase>.Instance);
        var act = async () => await useCase.ExecutarAsync(osId, "evt-3", CancellationToken.None);

        await act.Should().NotThrowAsync();
        repositorio.Verify(r => r.SalvarAsync(It.IsAny<Execucao>(), It.IsAny<HistoricoEntry>(), It.IsAny<CancellationToken>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task ExecutarAsync_QuandoJaFinalizada_IgnoraSemErro()
    {
        var osId = Guid.NewGuid();
        var execucao = Execucao.Reidratar(osId, StatusExecucao.Finalizado, Guid.NewGuid(), Guid.NewGuid(),
            Array.Empty<ItemPeca>(), Array.Empty<ItemServico>(), null, null, null, DateTimeOffset.UtcNow, 1m, DateTimeOffset.UtcNow);
        var repositorio = new Mock<IExecucaoRepository>();
        repositorio.Setup(r => r.ObterPorOsIdAsync(osId, It.IsAny<CancellationToken>())).ReturnsAsync(execucao);

        var useCase = new ProcessarOsCanceladaUseCase(repositorio.Object, NullLogger<ProcessarOsCanceladaUseCase>.Instance);
        var act = async () => await useCase.ExecutarAsync(osId, "evt-4", CancellationToken.None);

        await act.Should().NotThrowAsync();
        repositorio.Verify(r => r.SalvarAsync(It.IsAny<Execucao>(), It.IsAny<HistoricoEntry>(), It.IsAny<CancellationToken>(), It.IsAny<string?>()), Times.Never);
    }
}
