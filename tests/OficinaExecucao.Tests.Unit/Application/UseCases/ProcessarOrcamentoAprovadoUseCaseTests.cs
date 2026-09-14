using FluentAssertions;
using Moq;
using OficinaExecucao.Application;
using OficinaExecucao.Application.UseCases;
using OficinaExecucao.Domain;
using Xunit;

namespace OficinaExecucao.Tests.Unit.Application.UseCases;

public class ProcessarOrcamentoAprovadoUseCaseTests
{
    [Fact]
    public async Task ExecutarAsync_QuandoDiagnosticoConcluido_AprovaOrcamento()
    {
        var osId = Guid.NewGuid();
        var execucao = Execucao.Reidratar(osId, StatusExecucao.DiagnosticoConcluido, Guid.NewGuid(), null,
            Array.Empty<ItemPeca>(), Array.Empty<ItemServico>(), 4m, null, null, null, null, DateTimeOffset.UtcNow);
        var repositorio = new Mock<IExecucaoRepository>();
        repositorio.Setup(r => r.ObterPorOsIdAsync(osId, It.IsAny<CancellationToken>())).ReturnsAsync(execucao);

        var useCase = new ProcessarOrcamentoAprovadoUseCase(repositorio.Object);
        await useCase.ExecutarAsync(osId, "evt-5", CancellationToken.None);

        execucao.Status.Should().Be(StatusExecucao.AguardandoReparo);
        repositorio.Verify(r => r.SalvarAsync(execucao, It.IsAny<HistoricoEntry>(), It.IsAny<CancellationToken>(), "evt-5"), Times.Once);
    }

    [Fact]
    public async Task ExecutarAsync_QuandoOsNaoExiste_LancaKeyNotFound()
    {
        var osId = Guid.NewGuid();
        var repositorio = new Mock<IExecucaoRepository>();
        repositorio.Setup(r => r.ObterPorOsIdAsync(osId, It.IsAny<CancellationToken>())).ReturnsAsync((Execucao?)null);

        var useCase = new ProcessarOrcamentoAprovadoUseCase(repositorio.Object);
        var act = async () => await useCase.ExecutarAsync(osId, "evt-6", CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
