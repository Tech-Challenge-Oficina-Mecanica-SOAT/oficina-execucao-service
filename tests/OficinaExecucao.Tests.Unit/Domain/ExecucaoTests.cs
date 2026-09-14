using FluentAssertions;
using OficinaExecucao.Domain;
using OficinaExecucao.Domain.Exceptions;
using Xunit;

namespace OficinaExecucao.Tests.Unit.Domain;

public class ExecucaoTests
{
    private static readonly IReadOnlyList<ItemPeca> Pecas = new[] { new ItemPeca(Guid.NewGuid(), 2) };
    private static readonly IReadOnlyList<ItemServico> Servicos = new[] { new ItemServico(Guid.NewGuid(), 1) };

    [Fact]
    public void NovaNaFila_ComecaEmAguardandoDiagnostico()
    {
        var execucao = Execucao.NovaNaFila(Guid.NewGuid());

        execucao.Status.Should().Be(StatusExecucao.AguardandoDiagnostico);
    }

    [Fact]
    public void RegistrarDiagnostico_APartirDeAguardandoDiagnostico_MudaParaDiagnosticoConcluido()
    {
        var execucao = Execucao.NovaNaFila(Guid.NewGuid());

        var historico = execucao.RegistrarDiagnostico(Pecas, Servicos, 4m, "observação");

        execucao.Status.Should().Be(StatusExecucao.DiagnosticoConcluido);
        execucao.DiagnosticoId.Should().NotBeNull();
        execucao.Pecas.Should().BeEquivalentTo(Pecas);
        execucao.Servicos.Should().BeEquivalentTo(Servicos);
        historico.StatusAnterior.Should().Be(StatusExecucao.AguardandoDiagnostico);
        historico.StatusNovo.Should().Be(StatusExecucao.DiagnosticoConcluido);
    }

    [Fact]
    public void RegistrarDiagnostico_APartirDeEmDiagnostico_MudaParaDiagnosticoConcluido()
    {
        var execucao = Execucao.Reidratar(Guid.NewGuid(), StatusExecucao.EmDiagnostico, null, null,
            Array.Empty<ItemPeca>(), Array.Empty<ItemServico>(), null, null, null, null, null, DateTimeOffset.UtcNow);

        execucao.RegistrarDiagnostico(Pecas, Servicos, null, null);

        execucao.Status.Should().Be(StatusExecucao.DiagnosticoConcluido);
    }

    [Theory]
    [InlineData(StatusExecucao.DiagnosticoConcluido)]
    [InlineData(StatusExecucao.AguardandoReparo)]
    [InlineData(StatusExecucao.EmReparo)]
    [InlineData(StatusExecucao.Finalizado)]
    [InlineData(StatusExecucao.Cancelado)]
    public void RegistrarDiagnostico_DeQualquerOutroEstado_LancaTransicaoInvalida(StatusExecucao statusInicial)
    {
        var execucao = Execucao.Reidratar(Guid.NewGuid(), statusInicial, null, null,
            Array.Empty<ItemPeca>(), Array.Empty<ItemServico>(), null, null, null, null, null, DateTimeOffset.UtcNow);

        var act = () => execucao.RegistrarDiagnostico(Pecas, Servicos, null, null);

        act.Should().Throw<TransicaoInvalidaException>()
            .Which.StatusAtual.Should().Be(statusInicial);
    }

    [Fact]
    public void IniciarReparo_APartirDeAguardandoReparo_MudaParaEmReparo()
    {
        var execucao = Execucao.Reidratar(Guid.NewGuid(), StatusExecucao.AguardandoReparo, Guid.NewGuid(), null,
            Pecas, Servicos, 4m, null, null, null, null, DateTimeOffset.UtcNow);

        var historico = execucao.IniciarReparo();

        execucao.Status.Should().Be(StatusExecucao.EmReparo);
        execucao.ExecucaoId.Should().NotBeNull();
        execucao.IniciadaEm.Should().NotBeNull();
        historico.StatusAnterior.Should().Be(StatusExecucao.AguardandoReparo);
        historico.StatusNovo.Should().Be(StatusExecucao.EmReparo);
    }

    [Fact]
    public void IniciarReparo_ForaDeAguardandoReparo_LancaTransicaoInvalida()
    {
        var execucao = Execucao.NovaNaFila(Guid.NewGuid());

        var act = () => execucao.IniciarReparo();

        act.Should().Throw<TransicaoInvalidaException>();
    }

    [Fact]
    public void Finalizar_APartirDeEmReparo_MudaParaFinalizado()
    {
        var execucao = Execucao.Reidratar(Guid.NewGuid(), StatusExecucao.EmReparo, Guid.NewGuid(), Guid.NewGuid(),
            Pecas, Servicos, 4m, null, DateTimeOffset.UtcNow, null, null, DateTimeOffset.UtcNow);

        var historico = execucao.Finalizar(3.5m);

        execucao.Status.Should().Be(StatusExecucao.Finalizado);
        execucao.TempoRealHoras.Should().Be(3.5m);
        execucao.FinalizadaEm.Should().NotBeNull();
        historico.StatusNovo.Should().Be(StatusExecucao.Finalizado);
    }

    [Fact]
    public void Finalizar_ForaDeEmReparo_LancaTransicaoInvalida()
    {
        var execucao = Execucao.NovaNaFila(Guid.NewGuid());

        var act = () => execucao.Finalizar(1m);

        act.Should().Throw<TransicaoInvalidaException>();
    }

    [Theory]
    [InlineData(StatusExecucao.Finalizado)]
    [InlineData(StatusExecucao.Cancelado)]
    public void Cancelar_QuandoJaFinalizadoOuCancelado_LancaTransicaoInvalida(StatusExecucao statusInicial)
    {
        var execucao = Execucao.Reidratar(Guid.NewGuid(), statusInicial, null, null,
            Array.Empty<ItemPeca>(), Array.Empty<ItemServico>(), null, null, null, null, null, DateTimeOffset.UtcNow);

        var act = () => execucao.Cancelar();

        act.Should().Throw<TransicaoInvalidaException>();
    }

    [Fact]
    public void Cancelar_DeQualquerOutroEstado_MudaParaCancelado()
    {
        var execucao = Execucao.NovaNaFila(Guid.NewGuid());

        var historico = execucao.Cancelar();

        execucao.Status.Should().Be(StatusExecucao.Cancelado);
        historico.StatusNovo.Should().Be(StatusExecucao.Cancelado);
    }

    [Fact]
    public void AprovarOrcamento_APartirDeDiagnosticoConcluido_MudaParaAguardandoReparo()
    {
        var execucao = Execucao.Reidratar(Guid.NewGuid(), StatusExecucao.DiagnosticoConcluido, Guid.NewGuid(), null,
            Pecas, Servicos, 4m, null, null, null, null, DateTimeOffset.UtcNow);

        var historico = execucao.AprovarOrcamento();

        execucao.Status.Should().Be(StatusExecucao.AguardandoReparo);
        historico.StatusAnterior.Should().Be(StatusExecucao.DiagnosticoConcluido);
        historico.StatusNovo.Should().Be(StatusExecucao.AguardandoReparo);
    }

    [Theory]
    [InlineData(StatusExecucao.AguardandoDiagnostico)]
    [InlineData(StatusExecucao.EmDiagnostico)]
    [InlineData(StatusExecucao.AguardandoReparo)]
    [InlineData(StatusExecucao.EmReparo)]
    [InlineData(StatusExecucao.Finalizado)]
    [InlineData(StatusExecucao.Cancelado)]
    public void AprovarOrcamento_DeQualquerOutroEstado_LancaTransicaoInvalida(StatusExecucao statusInicial)
    {
        var execucao = Execucao.Reidratar(Guid.NewGuid(), statusInicial, null, null,
            Array.Empty<ItemPeca>(), Array.Empty<ItemServico>(), null, null, null, null, null, DateTimeOffset.UtcNow);

        var act = () => execucao.AprovarOrcamento();

        act.Should().Throw<TransicaoInvalidaException>()
            .Which.StatusAtual.Should().Be(statusInicial);
    }
}
