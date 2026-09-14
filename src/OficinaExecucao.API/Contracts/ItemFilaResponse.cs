namespace OficinaExecucao.API.Contracts;

public sealed record ItemFilaResponse(Guid OsId, string Status, DateTimeOffset AdicionadaEm);
