using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace OficinaExecucao.Tests.E2E;

[Collection("E2E")]
public class OsCanceladaTests(LocalStackFixture localStack) : E2ETestBase(localStack)
{
    [Fact]
    public async Task OsCancelada_MarcaAExecucaoComoCancelada()
    {
        var osId = Guid.NewGuid();
        await PublicarEventoFakeAsync(LocalStack.OsCriadaTopicArn, "os.criada", new
        {
            osId = osId.ToString(),
            clienteId = Guid.NewGuid().ToString(),
            veiculoId = Guid.NewGuid().ToString(),
            defeitoRelatado = "troca de óleo",
            criadaEm = DateTimeOffset.UtcNow.ToString("O")
        });
        await AguardarAsync(async () =>
        {
            var r = await Client.GetAsync($"/execucao/{osId}");
            return r.IsSuccessStatusCode;
        });

        await PublicarEventoFakeAsync(LocalStack.OsCanceladaTopicArn, "os.cancelada", new
        {
            osId = osId.ToString(),
            motivo = "cliente_desistiu",
            canceladaEm = DateTimeOffset.UtcNow.ToString("O")
        });

        await AguardarAsync(async () =>
        {
            var doc = await Client.GetFromJsonAsync<JsonElement>($"/execucao/{osId}");
            return doc.GetProperty("status").GetString() == "Cancelado";
        });
    }
}
