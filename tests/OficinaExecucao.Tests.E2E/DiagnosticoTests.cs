using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace OficinaExecucao.Tests.E2E;

[Collection("E2E")]
public class DiagnosticoTests(LocalStackFixture localStack) : E2ETestBase(localStack)
{
    [Fact]
    public async Task RegistrarDiagnostico_PublicaDiagnosticoConcluidoComOPayloadCorreto()
    {
        var osId = Guid.NewGuid();
        await PublicarEventoFakeAsync(LocalStack.OsCriadaTopicArn, "os.criada", new
        {
            osId = osId.ToString(),
            clienteId = Guid.NewGuid().ToString(),
            veiculoId = Guid.NewGuid().ToString(),
            defeitoRelatado = "freio",
            criadaEm = DateTimeOffset.UtcNow.ToString("O")
        });
        await AguardarAsync(async () =>
        {
            var r = await Client.GetAsync($"/execucao/{osId}");
            return r.IsSuccessStatusCode;
        });

        var pecaId = Guid.NewGuid();
        var response = await Client.PostAsJsonAsync($"/execucao/{osId}/diagnostico", new
        {
            pecas = new[] { new { pecaId, quantidade = 1 } },
            servicos = Array.Empty<object>(),
            tempoEstimadoHoras = 2.5
        });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var mensagem = await LerProximaMensagemDaFilaDeVerificacaoAsync();

        mensagem.RootElement.GetProperty("eventType").GetString().Should().Be("diagnostico.concluido");
        mensagem.RootElement.GetProperty("data").GetProperty("osId").GetString().Should().Be(osId.ToString());
        mensagem.RootElement.GetProperty("data").GetProperty("pecas")[0].GetProperty("pecaId").GetString().Should().Be(pecaId.ToString());
    }
}
