using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace OficinaExecucao.Tests.E2E;

[Collection("E2E")]
public class OsCriadaTests(LocalStackFixture localStack) : E2ETestBase(localStack)
{
    [Fact]
    public async Task OsCriada_AdicionaAOsNaFilaAguardandoDiagnostico()
    {
        var osId = Guid.NewGuid();

        await PublicarEventoFakeAsync(LocalStack.OsCriadaTopicArn, "os.criada", new
        {
            osId = osId.ToString(),
            clienteId = Guid.NewGuid().ToString(),
            veiculoId = Guid.NewGuid().ToString(),
            defeitoRelatado = "barulho no motor",
            criadaEm = DateTimeOffset.UtcNow.ToString("O")
        });

        await AguardarAsync(async () =>
        {
            var response = await Client.GetAsync($"/execucao/{osId}");
            if (!response.IsSuccessStatusCode)
                return false;

            var doc = await response.Content.ReadFromJsonAsync<JsonElement>();
            return doc.GetProperty("status").GetString() == "AguardandoDiagnostico";
        });
    }

    [Fact]
    public async Task OsCriada_ComMesmoEventId_ProcessaSoUmaVez()
    {
        var osId = Guid.NewGuid();
        var eventId = Guid.NewGuid().ToString();
        var dados = new
        {
            osId = osId.ToString(),
            clienteId = Guid.NewGuid().ToString(),
            veiculoId = Guid.NewGuid().ToString(),
            defeitoRelatado = "revisão",
            criadaEm = DateTimeOffset.UtcNow.ToString("O")
        };

        await PublicarEventoFakeAsync(LocalStack.OsCriadaTopicArn, "os.criada", dados, eventId);
        await AguardarAsync(async () =>
        {
            var response = await Client.GetAsync($"/execucao/{osId}");
            return response.IsSuccessStatusCode;
        });

        await PublicarEventoFakeAsync(LocalStack.OsCriadaTopicArn, "os.criada", dados, eventId);
        await Task.Delay(TimeSpan.FromSeconds(3));

        var historico = await Client.GetFromJsonAsync<JsonElement[]>($"/execucao/{osId}/historico");
        historico.Should().HaveCount(1);
    }
}
