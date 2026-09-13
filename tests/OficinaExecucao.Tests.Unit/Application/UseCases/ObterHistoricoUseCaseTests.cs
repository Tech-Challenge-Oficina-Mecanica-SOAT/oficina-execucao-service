using FluentAssertions;
using Moq;
using OficinaExecucao.Application;
using OficinaExecucao.Application.UseCases;
using OficinaExecucao.Domain;
using Xunit;

namespace OficinaExecucao.Tests.Unit.Application.UseCases;

public class ObterHistoricoUseCaseTests
{
    [Fact]
    public async Task ExecutarAsync_DelegaParaORepository()
    {
        var osId = Guid.NewGuid();
        var repositorio = new Mock<IExecucaoRepository>();
        var esperado = new[] { new HistoricoEntry(StatusExecucao.AguardandoDiagnostico, StatusExecucao.DiagnosticoConcluido, DateTimeOffset.UtcNow, "api") };
        repositorio.Setup(r => r.ObterHistoricoAsync(osId, It.IsAny<CancellationToken>())).ReturnsAsync(esperado);

        var useCase = new ObterHistoricoUseCase(repositorio.Object);
        var resultado = await useCase.ExecutarAsync(osId, CancellationToken.None);

        resultado.Should().BeEquivalentTo(esperado);
    }
}
