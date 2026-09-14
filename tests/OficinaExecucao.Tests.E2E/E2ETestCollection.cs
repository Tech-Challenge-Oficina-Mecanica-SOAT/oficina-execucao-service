using Xunit;

namespace OficinaExecucao.Tests.E2E;

[CollectionDefinition("E2E")]
public sealed class E2ETestCollection : ICollectionFixture<LocalStackFixture>
{
}
