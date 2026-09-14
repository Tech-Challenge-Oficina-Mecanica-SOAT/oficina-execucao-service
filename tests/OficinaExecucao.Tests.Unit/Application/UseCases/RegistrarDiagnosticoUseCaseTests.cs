using FluentAssertions;
using Moq;
using OficinaExecucao.Application;
using OficinaExecucao.Application.UseCases;
using OficinaExecucao.Domain;
using OficinaExecucao.Domain.Exceptions;
using Xunit;

namespace OficinaExecucao.Tests.Unit.Application.UseCases;

public class RegistrarDiagnosticoUseCaseTests
{
    private static readonly IReadOnlyList<ItemPeca> Pecas = new[] { new ItemPeca(Guid.NewGuid(), 1) };
    private static readonly IReadOnlyList<ItemServico> Servicos = new[] { new ItemServico(Guid.NewGuid(), 1) };

    [Fact]
    public async Task ExecutarAsync_QuandoOsExiste_SalvaEPublicaDiagnosticoConcluido()
    {
        var osId = Guid.NewGuid();
        var execucao = Execucao.NovaNaFila(osId);
        var repositorio = new Mock<IExecucaoRepository>();
        repositorio.Setup(r => r.ObterPorOsIdAsync(osId, It.IsAny<CancellationToken>())).ReturnsAsync(execucao);
        var publisher = new Mock<IEventPublisher>();

        var useCase = new RegistrarDiagnosticoUseCase(repositorio.Object, publisher.Object);
        var resultado = await useCase.ExecutarAsync(osId, Pecas, Servicos, 4m, "obs", CancellationToken.None);

        resultado.Status.Should().Be(StatusExecucao.DiagnosticoConcluido);
        repositorio.Verify(r => r.SalvarAsync(execucao, It.IsAny<HistoricoEntry>(), It.IsAny<CancellationToken>()), Times.Once);
        publisher.Verify(p => p.PublicarAsync("diagnostico.concluido", It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecutarAsync_QuandoOsNaoExiste_LancaKeyNotFound()
    {
        var osId = Guid.NewGuid();
        var repositorio = new Mock<IExecucaoRepository>();
        repositorio.Setup(r => r.ObterPorOsIdAsync(osId, It.IsAny<CancellationToken>())).ReturnsAsync((Execucao?)null);

        var useCase = new RegistrarDiagnosticoUseCase(repositorio.Object, Mock.Of<IEventPublisher>());
        var act = async () => await useCase.ExecutarAsync(osId, Pecas, Servicos, null, null, CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task ExecutarAsync_QuandoTransicaoInvalida_Propaga()
    {
        var osId = Guid.NewGuid();
        var execucao = Execucao.Reidratar(osId, StatusExecucao.Finalizado, null, null,
            Array.Empty<ItemPeca>(), Array.Empty<ItemServico>(), null, null, null, null, null, DateTimeOffset.UtcNow);
        var repositorio = new Mock<IExecucaoRepository>();
        repositorio.Setup(r => r.ObterPorOsIdAsync(osId, It.IsAny<CancellationToken>())).ReturnsAsync(execucao);

        var useCase = new RegistrarDiagnosticoUseCase(repositorio.Object, Mock.Of<IEventPublisher>());
        var act = async () => await useCase.ExecutarAsync(osId, Pecas, Servicos, null, null, CancellationToken.None);

        await act.Should().ThrowAsync<TransicaoInvalidaException>();
    }
}
