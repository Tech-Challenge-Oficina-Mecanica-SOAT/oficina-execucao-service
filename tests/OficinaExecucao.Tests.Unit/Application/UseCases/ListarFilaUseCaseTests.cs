using FluentAssertions;
using Moq;
using OficinaExecucao.Application;
using OficinaExecucao.Application.UseCases;
using OficinaExecucao.Domain;
using Xunit;

namespace OficinaExecucao.Tests.Unit.Application.UseCases;

public class ListarFilaUseCaseTests
{
    [Fact]
    public async Task ExecutarAsync_DelegaParaORepositoryComOStatusInformado()
    {
        var repositorio = new Mock<IExecucaoRepository>();
        var esperado = new[] { Execucao.NovaNaFila(Guid.NewGuid()) };
        repositorio
            .Setup(r => r.ListarPorStatusAsync(StatusExecucao.AguardandoReparo, It.IsAny<CancellationToken>()))
            .ReturnsAsync(esperado);

        var useCase = new ListarFilaUseCase(repositorio.Object);
        var resultado = await useCase.ExecutarAsync(StatusExecucao.AguardandoReparo, CancellationToken.None);

        resultado.Should().BeEquivalentTo(esperado);
    }
}
