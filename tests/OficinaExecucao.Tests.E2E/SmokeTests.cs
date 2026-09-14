using FluentAssertions;
using System.Net;
using Xunit;

namespace OficinaExecucao.Tests.E2E;

[Collection("E2E")]
public class SmokeTests(LocalStackFixture localStack) : E2ETestBase(localStack)
{
    [Fact]
    public async Task GetHealth_ComAApiApontadaParaOLocalStack_Retorna200()
    {
        var response = await Client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetFila_ComAApiApontadaParaOLocalStack_Retorna200()
    {
        var response = await Client.GetAsync("/fila");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
