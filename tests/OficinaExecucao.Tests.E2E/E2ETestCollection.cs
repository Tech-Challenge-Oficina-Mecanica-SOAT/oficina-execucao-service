using Xunit;

namespace OficinaExecucao.Tests.E2E;

// Shares one LocalStackFixture (and its LocalStack container) across every E2E test class:
// starting a fresh container per class would be slow for no benefit. This also forces xUnit
// to run all E2E test classes sequentially instead of in parallel, which avoids the same
// Serilog static Log.Logger race across concurrent WebApplicationFactory<Program> hosts that
// IntegrationTestCollection guards against ("The logger is already frozen.").
[CollectionDefinition("E2E")]
public sealed class E2ETestCollection : ICollectionFixture<LocalStackFixture>
{
}
