namespace OficinaExecucao.Application;

public interface IMensagemDispatcher
{
    Task ProcessarAsync(string mensagemJson, CancellationToken ct);
}
