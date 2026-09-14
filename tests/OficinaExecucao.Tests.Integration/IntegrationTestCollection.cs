using Xunit;

namespace OficinaExecucao.Tests.Integration;

// Serilog's two-stage bootstrap logger (Log.Logger = ...CreateBootstrapLogger(); builder.Host.UseSerilog(...))
// in Program.cs relies on a shared static Log.Logger. Building more than one WebApplicationFactory<Program>
// host concurrently races on that static field ("The logger is already frozen."). Forcing every integration
// test class into one xUnit collection makes xUnit run them sequentially instead of in parallel, avoiding the race.
[CollectionDefinition("Integration")]
public sealed class IntegrationTestCollection
{
}
