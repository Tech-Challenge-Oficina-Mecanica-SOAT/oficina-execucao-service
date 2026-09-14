using FluentAssertions;
using Moq;
using OficinaExecucao.Application;
using OficinaExecucao.Application.UseCases;
using OficinaExecucao.Domain;
using Xunit;

namespace OficinaExecucao.Tests.Unit.Application.UseCases;

public class ProcessarOsCriadaUseCaseTests
{
    [Fact]
    public async Task ExecutarAsync_CriaExecucaoNaFilaComOEventIdComoMarcador()
    {
        var osId = Guid.NewGuid();
        var repositorio = new Mock<IExecucaoRepository>();
        Execucao? execucaoSalva = null;
        string? eventIdRecebido = null;
        repositorio
            .Setup(r => r.SalvarAsync(It.IsAny<Execucao>(), It.IsAny<HistoricoEntry>(), It.IsAny<CancellationToken>(), It.IsAny<string?>()))
            .Callback<Execucao, HistoricoEntry, CancellationToken, string?>((e, _, _, evt) => { execucaoSalva = e; eventIdRecebido = evt; })
            .Returns(Task.CompletedTask);

        var useCase = new ProcessarOsCriadaUseCase(repositorio.Object);
        await useCase.ExecutarAsync(osId, "evt-1", CancellationToken.None);

        execucaoSalva.Should().NotBeNull();
        execucaoSalva!.OsId.Should().Be(osId);
        execucaoSalva.Status.Should().Be(StatusExecucao.AguardandoDiagnostico);
        eventIdRecebido.Should().Be("evt-1");
    }
}
