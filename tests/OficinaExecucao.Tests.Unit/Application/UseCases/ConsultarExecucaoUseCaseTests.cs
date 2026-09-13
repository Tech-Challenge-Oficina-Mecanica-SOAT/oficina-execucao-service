using FluentAssertions;
using Moq;
using OficinaExecucao.Application;
using OficinaExecucao.Application.UseCases;
using OficinaExecucao.Domain;
using Xunit;

namespace OficinaExecucao.Tests.Unit.Application.UseCases;

public class ConsultarExecucaoUseCaseTests
{
    [Fact]
    public async Task ExecutarAsync_RetornaOQueORepositoryDevolve()
    {
        var osId = Guid.NewGuid();
        var repositorio = new Mock<IExecucaoRepository>();
        var esperado = Execucao.NovaNaFila(osId);
        repositorio.Setup(r => r.ObterPorOsIdAsync(osId, It.IsAny<CancellationToken>())).ReturnsAsync(esperado);

        var useCase = new ConsultarExecucaoUseCase(repositorio.Object);
        var resultado = await useCase.ExecutarAsync(osId, CancellationToken.None);

        resultado.Should().Be(esperado);
    }

    [Fact]
    public async Task ExecutarAsync_QuandoNaoExiste_RetornaNull()
    {
        var osId = Guid.NewGuid();
        var repositorio = new Mock<IExecucaoRepository>();
        repositorio.Setup(r => r.ObterPorOsIdAsync(osId, It.IsAny<CancellationToken>())).ReturnsAsync((Execucao?)null);

        var useCase = new ConsultarExecucaoUseCase(repositorio.Object);
        var resultado = await useCase.ExecutarAsync(osId, CancellationToken.None);

        resultado.Should().BeNull();
    }
}
