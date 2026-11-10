# Demos da Saga do Execução Service

Resumo: dois scripts mostram o Execução Service participando da Saga de uma ordem de serviço, de ponta a ponta, na sua máquina. A API do Execução, o SNS, o SQS e o DynamoDB (no LocalStack) são reais. O OS Service e o Billing Service são simulados (Wiremock e eventos publicados pelo próprio script).

- `demo/demo-happy-path.sh`: caminho feliz, da OS criada até a execução finalizada.
- `demo/demo-rollback.sh`: compensação: após o orçamento recusado, o OS Service cancela a OS (simulado) e o mesmo `os.cancelada` entregue 3 vezes gera uma única transição (idempotência).

## Fluxo da Saga

```
 [SIMULADO]                 [REAL]                              [SIMULADO]
 OS Service                 Execução Service                    Billing Service
 (Wiremock + script)        (API + LocalStack)                  (Wiremock + script)

 POST /ordens-servico
   -> id da OS
 publica os.criada --SNS--> SQS --> consumer
                                    status: AguardandoDiagnostico
                            POST /execucao/{id}/diagnostico
                            publica diagnostico.concluido ------> (script confere na
                                    status: DiagnosticoConcluido    fila de verificação)
                                                                  POST /orcamentos/{id}/aprovar
                            SQS <--SNS-- orcamento.aprovado <---- publica orcamento.aprovado
                                    status: AguardandoReparo
                            POST /execucao/{id}/iniciar-reparo
                            publica execucao.iniciada
                                    status: EmReparo
                            POST /execucao/{id}/finalizar
                            publica execucao.finalizada
                                    status: Finalizado

 Rollback (demo-rollback.sh):
 publica os.cancelada x3 --SNS--> SQS --> consumer
 (mesmo eventId)                  status: Cancelado (1 transição, OS sai de GET /fila)
```

Os eventos publicados pelo Execução (`diagnostico.concluido`, `execucao.iniciada`, `execucao.finalizada`) vão para tópicos SNS reais, assinados por uma fila SQS de verificação que o script lê para provar que a publicação aconteceu.

## O que é real e o que é simulado

| Componente | Na demo | Como |
|---|---|---|
| API do Execução Service | Real | Container construído do `Dockerfile` do repositório |
| DynamoDB (fila de execução, histórico, idempotência) | Real | LocalStack |
| SNS e SQS (consumo e publicação de eventos) | Real | LocalStack, com tópicos, filas e DLQ criados pelo `demo-setup.sh` |
| OS Service | Simulado | Wiremock responde `POST /ordens-servico`; o script publica `os.criada` e `os.cancelada` |
| Billing Service | Simulado | Wiremock responde a aprovação do orçamento; o script publica `orcamento.aprovado` |

## Pré-requisitos

- Docker (com Docker Compose) em execução.
- `curl`.
- Um shell bash: Git Bash no Windows ou qualquer terminal Linux.

Não é preciso `jq`, `uuidgen` nem AWS CLI no host. Os comandos AWS rodam dentro do container do LocalStack.

## Como rodar

Execute a partir da raiz do repositório.

1. Subir o ambiente (LocalStack, Wiremock e API):

```bash
docker compose -f demo/docker-compose.demo.yml up -d --build
```

2. Provisionar tabela, filas e tópicos e esperar a API ficar pronta:

```bash
bash demo/demo-setup.sh
```

3. Rodar o caminho feliz:

```bash
bash demo/demo-happy-path.sh
```

4. Rodar a compensação (rollback):

```bash
bash demo/demo-rollback.sh
```

5. Derrubar tudo e apagar os dados:

```bash
docker compose -f demo/docker-compose.demo.yml down -v
```

Os scripts de demo podem ser executados várias vezes sem derrubar o ambiente. Eventos de execuções anteriores que sobrarem na fila de verificação são descartados automaticamente.

### Portas usadas no host

| Serviço | Porta | Observação |
|---|---|---|
| API do Execução | 5000 | Configurável com `API_PORT` |
| Wiremock | 8080 | Fixa |
| LocalStack | 4566 | Fixa |

Se a porta 5000 estiver ocupada, use outra porta da API. A variável precisa estar exportada nos comandos de `up` e nos scripts (eles montam o `API_URL` a partir dela):

```bash
export API_PORT=5001
```

Depois rode os passos 1 a 4 normalmente, no mesmo terminal.

## Troubleshooting

**"Ambiente fora do ar" ou erro ao falar com o Docker.** O Docker não está rodando, ou o `up` não foi executado. Inicie o Docker e rode o passo 1.

**"port is already allocated" na porta 5000.** Outro serviço usa a 5000. Exporte `API_PORT` (veja acima) e suba de novo.

**"port is already allocated" na porta 8080 ou 4566.** Essas portas não são configuráveis. Descubra o container que as ocupa e pare-o:

```bash
docker ps --format "table {{.Names}}\t{{.Ports}}"
```

```bash
docker stop <nome-do-container>
```

**Demora no `demo-setup.sh` ("Aquecendo a entrega SNS -> SQS").** Na primeira execução, a entrega do SNS para o SQS no LocalStack pode levar alguns segundos. O script tenta por até 60 segundos. Se estourar, rode o `demo-setup.sh` de novo.

**Timeout esperando um status ou evento.** Veja os logs da API:

```bash
docker compose -f demo/docker-compose.demo.yml logs api
```

Se o ambiente ficou inconsistente, recomece do zero com `down -v` (passo 5) e repita os passos 1 e 2.

## Demo do OS Service

O OS Service tem demos próprias em [oficina-os-service](https://github.com/Tech-Challenge-Oficina-Mecanica-SOAT/oficina-os-service), nos scripts `scripts/demo-happy-path.sh` e `scripts/demo-rollback.sh`. Nesta demo o OS é apenas simulado.
