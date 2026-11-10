using FluentAssertions;
using OficinaExecucao.Application.Observabilidade;
using OficinaExecucao.Infrastructure.Observabilidade;
using Xunit;

namespace OficinaExecucao.Tests.Unit.Infrastructure;

// Sem o agente do New Relic carregado, a NewRelic.Agent.Api é no-op: aqui valida-se que o adaptador
// não quebra o processamento e não altera exceções do delegate.
public class NewRelicObservabilidadeTests
{
    private const string Traceparent = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01";
    private readonly NewRelicObservabilidade _sut = new();

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "corr-malformado")]
    [InlineData("os.criada", Traceparent)]
    [InlineData(null, "00-xyz")]
    public async Task ProcessarMensagemAsync_SemAgente_NaoLancaEExecutaODelegate(string? eventType, string? correlationId)
    {
        var executou = false;

        await _sut.ProcessarMensagemAsync(
            new ContextoMensagem("e1", eventType, "abc", correlationId),
            () => { executou = true; return Task.CompletedTask; });

        executou.Should().BeTrue();
    }

    [Fact]
    public async Task ProcessarMensagemAsync_ExcecaoDoDelegate_PropagaInalterada()
    {
        var original = new InvalidOperationException("falhou");

        var act = () => _sut.ProcessarMensagemAsync(
            new ContextoMensagem("e1", "os.criada", "abc", Traceparent),
            () => throw original);

        (await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("falhou")).Which.Should().BeSameAs(original);
    }
}
