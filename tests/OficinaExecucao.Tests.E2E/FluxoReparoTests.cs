using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace OficinaExecucao.Tests.E2E;

[Collection("E2E")]
public class FluxoReparoTests(LocalStackFixture localStack) : E2ETestBase(localStack)
{
    [Fact]
    public async Task FluxoCompleto_DeOrcamentoAprovadoAteFinalizar_PublicaOsDoisEventosEHistoricoCompleto()
    {
        var osId = Guid.NewGuid();
        await PublicarEventoFakeAsync(LocalStack.OsCriadaTopicArn, "os.criada", new
        {
            osId = osId.ToString(),
            clienteId = Guid.NewGuid().ToString(),
            veiculoId = Guid.NewGuid().ToString(),
            defeitoRelatado = "suspensão",
            criadaEm = DateTimeOffset.UtcNow.ToString("O")
        });
        await AguardarAsync(async () =>
        {
            var r = await Client.GetAsync($"/execucao/{osId}");
            return r.IsSuccessStatusCode;
        });

        var diagnosticoResponse = await Client.PostAsJsonAsync($"/execucao/{osId}/diagnostico", new
        {
            pecas = Array.Empty<object>(),
            servicos = new[] { new { servicoId = Guid.NewGuid(), quantidade = 1 } },
            tempoEstimadoHoras = 3.0
        });
        diagnosticoResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var eventoDiagnostico = await LerProximaMensagemDaFilaDeVerificacaoAsync();
        eventoDiagnostico.RootElement.GetProperty("eventType").GetString().Should().Be("diagnostico.concluido");

        await PublicarEventoFakeAsync(LocalStack.OrcamentoAprovadoTopicArn, "orcamento.aprovado", new
        {
            osId = osId.ToString(),
            orcamentoId = Guid.NewGuid().ToString(),
            aprovadoEm = DateTimeOffset.UtcNow.ToString("O")
        });
        await AguardarAsync(async () =>
        {
            var doc = await Client.GetFromJsonAsync<JsonElement>($"/execucao/{osId}");
            return doc.GetProperty("status").GetString() == "AguardandoReparo";
        });

        var iniciarResponse = await Client.PostAsync($"/execucao/{osId}/iniciar-reparo", null);
        iniciarResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var eventoIniciada = await LerProximaMensagemDaFilaDeVerificacaoAsync();
        eventoIniciada.RootElement.GetProperty("eventType").GetString().Should().Be("execucao.iniciada");

        var finalizarResponse = await Client.PostAsJsonAsync($"/execucao/{osId}/finalizar", new { tempoRealHoras = 2.8 });
        finalizarResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var eventoFinalizada = await LerProximaMensagemDaFilaDeVerificacaoAsync();
        eventoFinalizada.RootElement.GetProperty("eventType").GetString().Should().Be("execucao.finalizada");

        var historico = await Client.GetFromJsonAsync<JsonElement[]>($"/execucao/{osId}/historico");
        var statusSequence = historico!.Select(h => h.GetProperty("statusNovo").GetString()).ToList();
        statusSequence.Should().ContainInOrder("DiagnosticoConcluido", "AguardandoReparo", "EmReparo", "Finalizado");
    }
}
