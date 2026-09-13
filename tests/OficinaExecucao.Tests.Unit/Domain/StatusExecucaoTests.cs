using FluentAssertions;
using OficinaExecucao.Domain;
using Xunit;

namespace OficinaExecucao.Tests.Unit.Domain;

public class StatusExecucaoTests
{
    [Fact]
    public void DeveConterOsSeteEstadosDoFluxoDeExecucao()
    {
        var estados = Enum.GetNames<StatusExecucao>();

        estados.Should().BeEquivalentTo(new[]
        {
            "AguardandoDiagnostico",
            "EmDiagnostico",
            "DiagnosticoConcluido",
            "AguardandoReparo",
            "EmReparo",
            "Finalizado",
            "Cancelado"
        });
    }
}
