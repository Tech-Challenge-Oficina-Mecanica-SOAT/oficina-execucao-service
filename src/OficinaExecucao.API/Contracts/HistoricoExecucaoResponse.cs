namespace OficinaExecucao.API.Contracts;

public sealed record HistoricoExecucaoResponse(string StatusAnterior, string StatusNovo, DateTimeOffset MudouEm, string Origem);
