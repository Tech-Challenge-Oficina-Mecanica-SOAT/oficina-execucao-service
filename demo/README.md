# Demos da Saga do Execução Service

Resumo: dois scripts mostram o Execução Service participando da Saga de uma ordem de serviço, de ponta a ponta, em dois alvos: `local` (padrão), tudo na sua máquina com Docker, e `eks`, contra o Execução real deployado no cluster do AWS Academy. A API do Execução, o SNS, o SQS e o DynamoDB são reais nos dois alvos. O OS Service e o Billing Service são simulados em ambos (Wiremock e eventos publicados pelo próprio script; no `eks`, os eventos de entrada são injetados direto na fila real do Execução). O alvo é escolhido com `TARGET=local` ou `TARGET=eks`; sem a variável, vale `local`.

- `demo/demo-happy-path.sh`: caminho feliz, da OS criada até a execução finalizada.
- `demo/demo-rollback.sh`: compensação: após o orçamento recusado, o OS Service cancela a OS (simulado) e o mesmo `os.cancelada` entregue 3 vezes gera uma única transição (idempotência).
- `demo/saga-completa.sh`: o fluxo completo pelos 4 serviços reais (Cadastros, OS, Execução e Billing) no cluster, sem nada simulado: `bash demo/saga-completa.sh happy` (OS termina `Concluida`) ou `rollback` (orçamento recusado, OS `Cancelada`). Exige os 4 serviços deployados; o passo a passo está em [docs/fluxo-completo.md](../docs/fluxo-completo.md).

## Fluxo da Saga

O diagrama abaixo é a visão do alvo `local`. No alvo `eks`, os serviços simulados são o próprio script: os eventos de entrada são injetados direto na fila SQS real do Execução e a verificação dos eventos de saída usa uma fila temporária.

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

| Componente | Alvo `local` | Alvo `eks` |
|---|---|---|
| API do Execução Service | Real: container construído do `Dockerfile` do repositório | Real: pod `oficina-execucao-service` do cluster `oficina-eks-homolog`, acessado por `kubectl port-forward` na porta 5002 |
| DynamoDB (fila de execução, histórico, idempotência) | Real: LocalStack | Real: DynamoDB da conta AWS Academy |
| SNS e SQS (consumo e publicação de eventos) | Real: LocalStack, com tópicos, filas e DLQ criados pelo `demo-setup.sh` | Real: fila e tópicos do Execução na conta AWS Academy (endereços lidos do SSM) |
| OS Service | Simulado: Wiremock responde `POST /ordens-servico`; o script publica `os.criada` e `os.cancelada` | Simulado: o id da OS é gerado pelo script e `os.criada` e `os.cancelada` são injetados direto na fila real do Execução |
| Billing Service | Simulado: Wiremock responde a aprovação do orçamento; o script publica `orcamento.aprovado` | Simulado: o id do orçamento é gerado pelo script e `orcamento.aprovado` é injetado direto na fila real do Execução |
| Verificação dos eventos de saída | Fila `oficina-execucao-verificacao-queue` criada pelo `demo-setup.sh` | Fila temporária `oficina-demo-probe-*`, criada e removida a cada execução |

## Por que o alvo `eks` roda assim

No cluster do AWS Academy existe apenas o Execução Service. O motivo de os testes rodarem desse modo:

- Cadastros, OS e Billing não estão deployados no cluster e não caberiam nele (o cluster tem um único node `t3.small`). Por isso os eventos de entrada (`os.criada`, `orcamento.aprovado`, `os.cancelada`) são injetados direto na fila real do Execução, e o script avisa na tela a cada injeção.
- O AWS Academy não permite criar roles IAM nem IRSA. O pod usa as credenciais temporárias da sessão atual do laboratório (cerca de 4 horas), entregues por um secret do Kubernetes. Quando a sessão é renovada, é preciso refazer o deploy (veja o Troubleshooting).
- A API só tem um `ClusterIP`, sem exposição externa. O acesso é por `kubectl port-forward`, aberto e encerrado pelo próprio script.
- Como não há consumidores reais dos eventos de saída, o script cria uma fila temporária assinada nos 3 tópicos reais (`diagnostico.concluido`, `execucao.iniciada`, `execucao.finalizada`) e a lê para provar que a publicação aconteceu. A fila e as assinaturas são removidas ao final, inclusive com Ctrl+C.

A API key interna é lida do secret `oficina-execucao-secrets` do cluster e nunca é impressa. Os ids de OS e de orçamento são gerados a cada execução, então rodar de novo é seguro.

Mais detalhes sobre o deploy em [docs/deploy.md](../docs/deploy.md). Para rodar a Saga com os 4 serviços reais juntos no cluster, veja [docs/fluxo-completo.md](../docs/fluxo-completo.md).

## Pré-requisitos

Alvo `local` (padrão):

- Docker (com Docker Compose) em execução.
- `curl`.
- Um shell bash: Git Bash no Windows ou qualquer terminal Linux.

Não é preciso `jq`, `uuidgen` nem AWS CLI no host. Os comandos AWS rodam dentro do container do LocalStack.

Alvo `eks`:

- `aws` (AWS CLI), `kubectl` e `curl` no host, mais um shell bash.
- Credenciais válidas do AWS Academy exportadas no terminal e kubeconfig do cluster `oficina-eks-homolog`.
- O Execução deployado no cluster, conforme [docs/deploy.md](../docs/deploy.md), com o pod `Running 1/1`.

Docker não é necessário no alvo `eks`. Também não há `demo-setup.sh` nem `docker compose`: o script cria e remove sozinho o que precisa.

## Como rodar no alvo `eks`

Execute a partir da raiz do repositório, com as credenciais do AWS Academy exportadas:

```bash
TARGET=eks AWS_DEFAULT_REGION=us-east-1 bash demo/demo-happy-path.sh
```

```bash
TARGET=eks AWS_DEFAULT_REGION=us-east-1 bash demo/demo-rollback.sh
```

O que o script faz, nesta ordem:

1. Confere `aws`, `kubectl` e `curl` no host e valida a sessão AWS. Se algo faltar ou a sessão for inválida, para antes de criar qualquer coisa.
2. Lê do SSM a URL da fila de entrada do Execução e os ARNs dos 3 tópicos de saída, e lê a API key do secret `oficina-execucao-secrets` do cluster (sem imprimir a chave).
3. Aborta se a porta 5002 já estiver respondendo (port-forward esquecido), para não falar com o serviço errado.
4. Instala o `trap` de limpeza, abre `kubectl port-forward svc/oficina-execucao-service 5002:80 -n oficina` e espera até 30 s por `/health/ready` = 200.
5. Varre sobras `oficina-demo-probe-*` (filas e assinaturas) de execuções interrompidas.
6. Cria a fila temporária de verificação e a assina nos 3 tópicos de saída.
7. Executa a Saga: os eventos de entrada são injetados na fila do Execução e os de saída são conferidos na fila temporária.
8. Ao terminar, falhar ou receber Ctrl+C, remove assinaturas, fila temporária e port-forward.

Não rode duas demos `eks` em paralelo na mesma conta: a varredura do início de cada execução remove a fila temporária de verificação da outra.

Para o New Relic mostrar dados, rode os scripts pouco antes de abrir o APM `oficina-execucao-service`: o tráfego gerado pela demo é o que popula as telas.

## Como rodar no alvo `local`

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

Não rode duas demos em paralelo contra o mesmo ambiente, pois cada script descarta da fila de verificação os eventos de outras OSes.

### Portas usadas no host

| Serviço | Porta | Observação |
|---|---|---|
| API do Execução | 5000 | Configurável com `API_PORT` |
| Port-forward do Execução (alvo `eks`) | 5002 | Fixa; o script abre e fecha sozinho |
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

### Alvo `eks`

**"Sessão AWS inválida ou expirada. Atualize as credenciais do AWS Academy."** As credenciais exportadas no terminal expiraram ou não existem. Renove-as no AWS Academy, exporte de novo e rode o script outra vez. Nada foi criado antes dessa checagem.

**"A porta 5002 já está respondendo (port-forward esquecido?)".** Há um `kubectl port-forward` antigo (ou outro processo) na 5002 e o script aborta de propósito, para não falar com o serviço errado. Encerre o processo e rode de novo.

No Git Bash (Windows), ache o PID de quem escuta na 5002 (última coluna da linha `LISTENING`) e encerre-o:

```bash
netstat -ano | grep ':5002'
```

```bash
taskkill //F //PID <pid>
```

As barras são duplas no Git Bash. Se nenhum outro `kubectl` estiver em uso, a opção direta é `taskkill //F //IM kubectl.exe`. No Linux: `lsof -ti :5002 | xargs kill`.

**Pod com `AccessDenied` no SQS ou no DynamoDB e `0/1`, logo depois de renovar as credenciais.** Ao renovar o laboratório, o AWS Academy cancela as credenciais da sessão anterior (política `voc-cancel-cred`), e o pod, que ainda usa as antigas pelo secret, passa a falhar e fica `0/1`. Com as credenciais novas exportadas, refaça o deploy (veja [docs/deploy.md](../docs/deploy.md)):

```bash
./scripts/deploy.sh homolog
```

**`kubectl get nodes` vazio e pod `Pending`.** O AWS Academy troca o node do cluster aproximadamente a cada hora e, às vezes, o substituto não se registra: o bootstrap do node recebe um deny explícito de uma política da organização em `ec2:DescribeInstances`. Termine a instância EC2 travada para que o autoscaling group lance outra, e espere o node aparecer em `kubectl get nodes`.

**Sobrou uma fila `oficina-demo-probe-*`.** Acontece se o processo foi morto à força. A próxima execução do script varre essas sobras sozinha.

## Demo do OS Service

O OS Service tem demos próprias em [oficina-os-service](https://github.com/Tech-Challenge-Oficina-Mecanica-SOAT/oficina-os-service), nos scripts `scripts/demo-happy-path.sh` e `scripts/demo-rollback.sh`. Nesta demo o OS é apenas simulado.
