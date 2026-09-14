namespace OficinaExecucao.Domain;

public sealed record HistoricoEntry(
    StatusExecucao StatusAnterior,
    StatusExecucao StatusNovo,
    DateTimeOffset MudouEm,
    string Origem);
