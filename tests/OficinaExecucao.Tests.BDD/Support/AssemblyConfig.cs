using Xunit;

// Program.cs usa o bootstrap logger estático do Serilog; o ambiente BDD é um único WebApplicationFactory
// compartilhado (AmbienteBdd), então não há a corrida de múltiplos hosts que a suíte de Integration evita
// com isso. Mantemos os cenários sequenciais mesmo assim, para não ter que pensar em concorrência sobre
// o repositório e o publicador fakes compartilhados.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
