namespace OficinaExecucao.API.Contracts;

public sealed record ExecucaoResponse(
    Guid OsId,
    string Status,
    IReadOnlyList<ItemPecaDto> Pecas,
    IReadOnlyList<ItemServicoDto> Servicos,
    DateTimeOffset? IniciadaEm,
    DateTimeOffset? FinalizadaEm,
    decimal? TempoRealHoras);
