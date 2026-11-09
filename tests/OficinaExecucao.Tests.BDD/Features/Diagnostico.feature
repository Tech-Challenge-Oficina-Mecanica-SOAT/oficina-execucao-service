# language: pt
Funcionalidade: Registro de diagnóstico
  Para que o orçamento possa ser gerado
  Como mecânico
  Quero registrar o diagnóstico com as peças e serviços necessários

  Cenário: Diagnóstico publica evento com peças e serviços
    Dado uma execução na fila aguardando diagnóstico
    Quando registro o diagnóstico com 1 peça e 1 serviço
    Então o evento "diagnostico.concluido" é publicado
    E o evento publicado contém as peças e serviços informados
