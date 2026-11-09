# language: pt
Funcionalidade: Fila de execução
  Para que o mecânico veja as OSes que chegaram
  Como Execução Service
  Quero colocar na fila toda OS criada

  Cenário: OS criada é adicionada à fila
    Dado que o evento "os.criada" foi consumido para a OS
    Quando consulto a execução da OS
    Então o status da execução é "AguardandoDiagnostico"
