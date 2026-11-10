using FluentAssertions;
using OficinaExecucao.Application.Observabilidade;
using Xunit;

namespace OficinaExecucao.Tests.Unit.Application.Observabilidade;

public class ObservabilidadeHelpersTests
{
    private const string TraceId = "4bf92f3577b34da6a3ce929d0e0e4736";

    [Fact]
    public void ExtrairTraceId_TraceparentValido_RetornaOTraceId()
        => CorrelacaoHelper.ExtrairTraceId($"00-{TraceId}-00f067aa0ba902b7-01").Should().Be(TraceId);

    [Fact]
    public void ExtrairTraceId_GuidSimples_RetornaComoEsta()
    {
        var guid = Guid.NewGuid().ToString();
        CorrelacaoHelper.ExtrairTraceId(guid).Should().Be(guid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ExtrairTraceId_NuloOuVazio_RetornaNulo(string? valor)
        => CorrelacaoHelper.ExtrairTraceId(valor).Should().BeNull();

    [Theory]
    [InlineData("00-abc-def-01")]
    [InlineData("00-00000000000000000000000000000000-00f067aa0ba902b7-01")]
    [InlineData("lixo")]
    public void ExtrairTraceId_Malformado_RetornaComoEsta(string valor)
        => CorrelacaoHelper.ExtrairTraceId(valor).Should().Be(valor);

    [Fact]
    public void EhTraceparent_DistingueValidoDeInvalido()
    {
        CorrelacaoHelper.EhTraceparent($"00-{TraceId}-00f067aa0ba902b7-01").Should().BeTrue();
        CorrelacaoHelper.EhTraceparent("corr-1").Should().BeFalse();
        CorrelacaoHelper.EhTraceparent(null).Should().BeFalse();
    }

    [Fact]
    public void NomeTransacao_Web_UsaOTemplateDaRota()
    {
        NomeTransacao.Web("get", "/execucao/{osId}").Should().Be("GET /execucao/{osId}");
        NomeTransacao.Web("POST", "/execucao/{osId}/diagnostico").Should().Be("POST /execucao/{osId}/diagnostico");
    }

    [Fact]
    public void NomeTransacao_Web_RemoveRestricoesDeParametro()
        => NomeTransacao.Web("GET", "/execucao/{osId:guid}/historico").Should().Be("GET /execucao/{osId}/historico");

    [Fact]
    public void NomeTransacao_Mensagem_UsaOTipoDoEvento()
    {
        NomeTransacao.Mensagem("os.criada").Should().Be("SQS/os.criada");
        NomeTransacao.Mensagem(null).Should().Be("SQS/desconhecido");
    }

    [Theory]
    [InlineData("/health/live", true)]
    [InlineData("/health/ready", true)]
    [InlineData("/Health/Ready", true)]
    [InlineData("/fila", false)]
    [InlineData("/execucao/abc", false)]
    public void NomeTransacao_DeveIgnorar_SoParaHealthChecks(string path, bool esperado)
        => NomeTransacao.DeveIgnorar(path).Should().Be(esperado);

    [Fact]
    public void ContextoMensagem_EnvelopeCompleto_LeOsCampos()
    {
        var json = """{"eventId":"e1","eventType":"os.criada","correlationId":"00-""" + TraceId + """-00f067aa0ba902b7-01","data":{"osId":"abc"}}""";
        var ctx = ContextoMensagem.Ler(json);
        ctx.EventId.Should().Be("e1");
        ctx.EventType.Should().Be("os.criada");
        ctx.OsId.Should().Be("abc");
        ctx.CorrelationId.Should().StartWith("00-");
        ctx.TraceId.Should().Be(TraceId);
    }

    [Theory]
    [InlineData("""{"eventId":"e1","eventType":"os.criada","data":{"osId":"abc"}}""")]
    [InlineData("""{"eventId":"e1","eventType":"os.criada","correlationId":123,"data":{"osId":"abc"}}""")]
    public void ContextoMensagem_SemCorrelationIdOuInvalido_NaoQuebra(string json)
    {
        var ctx = ContextoMensagem.Ler(json);
        ctx.CorrelationId.Should().BeNull();
        ctx.TraceId.Should().BeNull();
        ctx.OsId.Should().Be("abc");
    }

    [Theory]
    [InlineData("não é json")]
    [InlineData("{}")]
    [InlineData("[]")]
    public void ContextoMensagem_JsonInvalidoOuVazio_RetornaCamposNulos(string json)
    {
        var ctx = ContextoMensagem.Ler(json);
        ctx.EventType.Should().BeNull();
        ctx.OsId.Should().BeNull();
    }
}
