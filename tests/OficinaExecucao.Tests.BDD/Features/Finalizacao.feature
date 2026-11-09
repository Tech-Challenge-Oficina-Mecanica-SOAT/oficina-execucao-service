# language: pt
Funcionalidade: Finalização do reparo
  Para que o cliente seja cobrado
  Como mecânico
  Quero finalizar o reparo e publicar o evento correspondente

  Cenário: Reparo finalizado publica execucao.finalizada
    Dado uma execução em reparo
    Quando finalizo o reparo informando 2 horas de trabalho
    Então o evento "execucao.finalizada" é publicado
    E o histórico da execução mostra a transição para "Finalizado"
