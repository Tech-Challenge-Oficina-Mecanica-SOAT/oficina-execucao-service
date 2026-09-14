namespace OficinaExecucao.Domain.Exceptions;

public sealed class TransicaoInvalidaException : Exception
{
    public StatusExecucao StatusAtual { get; }
    public string AcaoTentada { get; }

    public TransicaoInvalidaException(StatusExecucao statusAtual, string acaoTentada)
        : base($"Não é possível executar '{acaoTentada}' com a execução no estado '{statusAtual}'.")
    {
        StatusAtual = statusAtual;
        AcaoTentada = acaoTentada;
    }
}
