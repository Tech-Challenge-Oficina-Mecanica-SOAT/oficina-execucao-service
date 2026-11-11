using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using OficinaExecucao.Application;
using OficinaExecucao.Application.Observabilidade;
using OficinaExecucao.Infrastructure.Messaging;
using Xunit;

namespace OficinaExecucao.Tests.Unit.Infrastructure;

public class MensagemDispatcherObservavelTests
{
    private const string TraceId = "4bf92f3577b34da6a3ce929d0e0e4736";

    private sealed class LoggerComEscopo : ILogger<MensagemDispatcherObservavel>
    {
        public List<Dictionary<string, object>> Escopos { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            if (state is IEnumerable<KeyValuePair<string, object>> pares)
                Escopos.Add(pares.ToDictionary(p => p.Key, p => p.Value));
            return null;
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }

    private static string Envelope(string correlacao) =>
        $$$"""{"eventId":"e1","eventType":"os.criada",{{{correlacao}}}"data":{"osId":"abc"}}""";

    [Fact]
    public async Task ProcessarAsync_SemCorrelationId_DelegaSemQuebrar()
    {
        var inner = new Mock<IMensagemDispatcher>();
        var correlacao = new CorrelacaoAtual();
        var logger = new LoggerComEscopo();
        var sut = new MensagemDispatcherObservavel(inner.Object, new ObservabilidadeNula(), correlacao, logger);
        var json = Envelope("");

        await sut.ProcessarAsync(json, CancellationToken.None);

        inner.Verify(d => d.ProcessarAsync(json, It.IsAny<CancellationToken>()), Times.Once);
        correlacao.CorrelationId.Should().BeNull();
        logger.Escopos.Single().Should().NotContainKey("correlationId").And.ContainKey("osId");
    }

    [Fact]
    public async Task ProcessarAsync_CorrelationIdMalformado_DelegaEUsaOValorComoEsta()
    {
        var inner = new Mock<IMensagemDispatcher>();
        var logger = new LoggerComEscopo();
        var sut = new MensagemDispatcherObservavel(inner.Object, new ObservabilidadeNula(), new CorrelacaoAtual(), logger);

        await sut.ProcessarAsync(Envelope("\"correlationId\":\"00-xyz\","), CancellationToken.None);

        inner.Verify(d => d.ProcessarAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        logger.Escopos.Single()["correlationId"].Should().Be("00-xyz");
    }

    [Fact]
    public async Task ProcessarAsync_Traceparent_UsaTraceIdNoEscopoEGuardaOCorrelationIdOriginal()
    {
        var inner = new Mock<IMensagemDispatcher>();
        var correlacao = new CorrelacaoAtual();
        var logger = new LoggerComEscopo();
        var traceparent = $"00-{TraceId}-00f067aa0ba902b7-01";
        string? visto = null;
        inner.Setup(d => d.ProcessarAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => visto = correlacao.CorrelationId).Returns(Task.CompletedTask);
        var sut = new MensagemDispatcherObservavel(inner.Object, new ObservabilidadeNula(), correlacao, logger);

        await sut.ProcessarAsync(Envelope($"\"correlationId\":\"{traceparent}\","), CancellationToken.None);

        logger.Escopos.Single()["correlationId"].Should().Be(TraceId);
        visto.Should().Be(traceparent);
    }

    [Fact]
    public async Task ProcessarAsync_ExecutaDentroDaTransacaoDeMensagemEPropagaExcecao()
    {
        var inner = new Mock<IMensagemDispatcher>();
        inner.Setup(d => d.ProcessarAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("falhou"));
        var obs = new Mock<IObservabilidade>();
        obs.Setup(o => o.ProcessarMensagemAsync(It.IsAny<ContextoMensagem>(), It.IsAny<Func<Task>>()))
            .Returns<ContextoMensagem, Func<Task>>((_, f) => f());
        var sut = new MensagemDispatcherObservavel(inner.Object, obs.Object, new CorrelacaoAtual(), new LoggerComEscopo());

        var act = () => sut.ProcessarAsync(Envelope(""), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("falhou");
        obs.Verify(o => o.ProcessarMensagemAsync(It.Is<ContextoMensagem>(c => c.EventType == "os.criada" && c.OsId == "abc"), It.IsAny<Func<Task>>()), Times.Once);
    }
}
