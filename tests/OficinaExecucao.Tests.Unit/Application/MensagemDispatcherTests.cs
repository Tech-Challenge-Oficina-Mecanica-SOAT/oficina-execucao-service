using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OficinaExecucao.Application;
using OficinaExecucao.Application.UseCases;
using OficinaExecucao.Domain;
using Xunit;

namespace OficinaExecucao.Tests.Unit.Application;

public class MensagemDispatcherTests
{
    private static string EnvelopeJson(string eventType, string eventId, Guid osId) =>
        $$"""
        {
          "eventId": "{{eventId}}",
          "eventType": "{{eventType}}",
          "eventVersion": "1.0",
          "occurredAt": "2026-09-14T00:00:00Z",
          "correlationId": "corr-1",
          "producer": "os-service",
          "data": { "osId": "{{osId}}" }
        }
        """;

    [Fact]
    public async Task ProcessarAsync_QuandoEventoJaProcessado_NaoChamaNenhumUseCase()
    {
        var osId = Guid.NewGuid();
        var repositorio = new Mock<IExecucaoRepository>();
        repositorio.Setup(r => r.EventoJaProcessadoAsync(osId, "evt-1", It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var dispatcher = new MensagemDispatcher(
            repositorio.Object,
            new ProcessarOsCriadaUseCase(repositorio.Object),
            new ProcessarOsCanceladaUseCase(repositorio.Object, NullLogger<ProcessarOsCanceladaUseCase>.Instance),
            new ProcessarOrcamentoAprovadoUseCase(repositorio.Object));

        await dispatcher.ProcessarAsync(EnvelopeJson("os.criada", "evt-1", osId), CancellationToken.None);

        repositorio.Verify(r => r.SalvarAsync(It.IsAny<Execucao>(), It.IsAny<HistoricoEntry>(), It.IsAny<CancellationToken>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task ProcessarAsync_RoteiaOsCriadaParaOUseCaseCorreto()
    {
        var osId = Guid.NewGuid();
        var repositorio = new Mock<IExecucaoRepository>();
        repositorio.Setup(r => r.EventoJaProcessadoAsync(osId, "evt-2", It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var dispatcher = new MensagemDispatcher(
            repositorio.Object,
            new ProcessarOsCriadaUseCase(repositorio.Object),
            new ProcessarOsCanceladaUseCase(repositorio.Object, NullLogger<ProcessarOsCanceladaUseCase>.Instance),
            new ProcessarOrcamentoAprovadoUseCase(repositorio.Object));

        await dispatcher.ProcessarAsync(EnvelopeJson("os.criada", "evt-2", osId), CancellationToken.None);

        repositorio.Verify(r => r.SalvarAsync(
            It.Is<Execucao>(e => e.OsId == osId && e.Status == StatusExecucao.AguardandoDiagnostico),
            It.IsAny<HistoricoEntry>(), It.IsAny<CancellationToken>(), "evt-2"), Times.Once);
    }

    [Fact]
    public async Task ProcessarAsync_TipoDesconhecido_LancaInvalidOperation()
    {
        var osId = Guid.NewGuid();
        var repositorio = new Mock<IExecucaoRepository>();
        repositorio.Setup(r => r.EventoJaProcessadoAsync(osId, "evt-3", It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var dispatcher = new MensagemDispatcher(
            repositorio.Object,
            new ProcessarOsCriadaUseCase(repositorio.Object),
            new ProcessarOsCanceladaUseCase(repositorio.Object, NullLogger<ProcessarOsCanceladaUseCase>.Instance),
            new ProcessarOrcamentoAprovadoUseCase(repositorio.Object));

        var act = async () => await dispatcher.ProcessarAsync(EnvelopeJson("tipo.inexistente", "evt-3", osId), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
