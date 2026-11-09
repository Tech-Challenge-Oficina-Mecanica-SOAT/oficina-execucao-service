using Reqnroll;

namespace OficinaExecucao.Tests.BDD.Support;

/// <summary>Sobe a API uma única vez para toda a execução e derruba no final.</summary>
[Binding]
public static class AmbienteBdd
{
    private static ExecucaoApiFactory? _factory;

    public static ExecucaoApiFactory Factory =>
        _factory ?? throw new InvalidOperationException("O ambiente BDD ainda não foi iniciado.");

    [BeforeTestRun]
    public static void Iniciar() => _factory = new ExecucaoApiFactory();

    [AfterTestRun]
    public static void Encerrar()
    {
        _factory?.Dispose();
        _factory = null;
    }
}
