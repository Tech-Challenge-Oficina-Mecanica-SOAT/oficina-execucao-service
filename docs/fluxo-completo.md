# Fluxo completo da Saga nos 4 serviços (AWS Academy)

Este documento mostra como rodar a Saga completa de uma ordem de serviço pelos quatro microsserviços reais (Cadastros, OS, Execução e Billing) juntos no mesmo cluster EKS do AWS Academy, para a demonstração de entrega. O resultado esperado é uma OS que termina `Concluida` (caminho feliz) e outra que termina `Cancelada` com a Execução em `Cancelado` (compensação, orçamento recusado). Quem só quer ver o Execução participando da Saga, sem subir os outros três, deve usar as demos de [demo/README.md](../demo/README.md).

## Por que existem contornos

O AWS Academy impõe limites que moldam este procedimento:

- Não permite criar roles IAM nem IRSA: os pods usam as credenciais temporárias da sessão (cerca de 4 horas), entregues por secrets do Kubernetes. A cada renovação do laboratório é preciso refazer o deploy (passo 8).
- O cluster tem um node `t3.small` por padrão, pouco para quatro serviços: é preciso subir para 2 nodes (passo 3).
- O RDS é privado e compartilhado; cada serviço cria o próprio database nele.
- Nenhum serviço tem URL pública: o OS usa um Service interno no lugar do NLB e todo acesso é por `kubectl port-forward`.
- O Billing lê o campo `descricao`, mas o Cadastros devolve `nome` (bug de contrato com correção em PR aberto no repositório do Billing). Enquanto a imagem corrigida não existe, um proxy nginx faz a tradução (passo 7).

Os arquivos de apoio ficam em [demo/academy](../demo/academy) e o script automatizado em [demo/saga-completa.sh](../demo/saga-completa.sh).

## Pré-requisitos

- Git Bash, `git` (com submódulos), `make`, `envsubst`, `aws` CLI, `kubectl`, `terraform`, `curl` e `openssl`. Docker só é necessário para a demo local.
- Credenciais novas do AWS Academy exportadas (`AWS_ACCESS_KEY_ID`, `AWS_SECRET_ACCESS_KEY`, `AWS_SESSION_TOKEN`; expiram em cerca de 4 horas).
- Um token do GitHub (PAT) com permissão Packages: read, para o pull das imagens no GHCR.
- A `NEW_RELIC_LICENSE_KEY`, para o APM reportar (opcional para o fluxo funcionar).
- Os repositórios dos serviços clonados como pastas irmãs, no mesmo diretório pai: `oficina-execucao-service`, `oficina-cadastros-service`, `oficina-os-service`, `oficina-billing-service`, `oficina-infra-k8s` e `oficina-infra-db`.

Exporte os segredos só no terminal, nunca em arquivos:

```bash
export AWS_ACCESS_KEY_ID=... AWS_SECRET_ACCESS_KEY=... AWS_SESSION_TOKEN=...
export GHCR_USERNAME=<usuario-github> GHCR_PAT=<token>
export NEW_RELIC_LICENSE_KEY=<chave>
export AWS_DEFAULT_REGION=us-east-1
export MSYS_NO_PATHCONV=1   # Git Bash: impede que o aws.exe receba /oficina/... reescrito como C:/Program Files/Git/...
export POS=<pasta-pai-dos-repositorios>   # ex.: /j/Dev/pos
```

Os comandos abaixo usam `$POS` como a pasta que contém todos os clones. Os passos 5.2 em diante devem rodar no mesmo terminal (variáveis como `NODE_ROLE` são reaproveitadas).

Regra para os `terraform apply` abaixo: gere o plano com `-out`, leia e aplique o arquivo salvo (`terraform plan ... -out=tfplan && terraform apply tfplan`). Os blocos mostram só a linha do `plan`. Exceção: `make db-apply` e `make k8s-apply` (passo 1) aplicam com `-auto-approve`.

Regra de state: vários diretórios usam state local, sem backend remoto (`oficina-infra-db/envs/homolog` via `make db-apply`, `oficina-infra-k8s/terraform/envs/homolog`, `oficina-os-service/terraform` e `oficina-cadastros-service/terraform`), e um clone pode guardar state de OUTRA conta do Academy. Antes de planejar em qualquer um deles, compare as contas do state com a conta atual:

```bash
aws sts get-caller-identity --query Account --output text
grep -o 'arn:aws:[a-z]*:[a-z0-9-]*:[0-9]*' terraform.tfstate | sort | uniq -c
```

Se aparecerem ids de outra conta, faça backup do state e tire-o do caminho (`mv terraform.tfstate terraform.tfstate.outra-conta`) ou remova só os recursos estrangeiros com `terraform state rm`, e só então rode o `plan`.

## 1. Infra base: VPC, EKS e RDS

A VPC e o RDS vêm do `oficina-infra-db`, e o EKS lê a VPC no Parameter Store, então o banco vem primeiro. O `envs/homolog` do `oficina-infra-db` usa um backend S3 que não serve no Academy; o `make db-apply` do `oficina-infra-k8s` cria um `backend_override.tf` (ignorado pelo git) que troca para state local. O módulo VPC é um submódulo git, que precisa ser inicializado antes:

```bash
git -C "$POS/oficina-infra-db" submodule update --init --recursive
cd "$POS/oficina-infra-k8s"
make creds-check
make db-apply
```

O `make db-apply` aplica com `-auto-approve`. O state local fica em `oficina-infra-db/envs/homolog`. Se ele veio de outra conta do Academy, os sintomas são `reading Secrets Manager Secret ... AccessDenied` ou recursos com account id diferente. Correção: faça backup do arquivo de state, remova as entradas antigas e planeje de novo (deve mostrar só criações):

```bash
cd "$POS/oficina-infra-db/envs/homolog"
cp terraform.tfstate terraform.tfstate.bak
terraform state rm $(terraform state list | grep -E '^module\.(rds\.|secrets\.aws_secretsmanager_)')
terraform plan -out=tfplan
terraform apply tfplan
```

O security group do RDS já libera a porta 5432 para a VPC inteira, então a regra `rds_ingress` do `oficina-infra-k8s` não é necessária. Não rode `terraform apply -target=module.rds_ingress` lá: o plano substitui o node group.

Com a VPC pronta, crie o cluster e o namespace pelo `oficina-infra-k8s`:

```bash
cd "$POS/oficina-infra-k8s"
make k8s-apply kubeconfig
kubectl apply -f k8s/shared/namespace.yaml
```

Rode `make k8s-apply` uma única vez, numa conta nova, e nunca depois do passo 3: ele aplica com `-auto-approve` e o node group tem `desired_size = 1` sem `ignore_changes`, então reexecutá-lo desfaz o escalonamento para 2 nodes. Não rode `make up` nem `make deploy`: eles também implantam o monolito da Fase 3, que não faz parte desta Saga. Por isso o namespace `oficina` é criado à mão, pelo `k8s/shared/namespace.yaml`; sem ele os passos seguintes (inclusive o `scripts/deploy.sh` do Cadastros) falham.

Verificação:

```bash
kubectl get nodes
kubectl get ns oficina
aws ssm get-parameter --name /oficina/homolog/db/endpoint --query Parameter.Value --output text
```

Esperado: ao menos um node `Ready`, o namespace `oficina` existente e o endpoint do RDS impresso.

## 2. Pull secret do GHCR

```bash
bash "$POS/oficina-infra-k8s/scripts/create-ghcr-pull-secret.sh" homolog
```

Lê `GHCR_USERNAME` e `GHCR_PAT` do ambiente. Esperado: `Secret 'ghcr-pull-secret' criado/atualizado no namespace 'oficina'`.

## 3. Capacidade do cluster

Um `t3.small` não comporta os quatro serviços. Suba o node group para 2 nodes sem editar o Terraform:

```bash
aws eks update-nodegroup-config --cluster-name oficina-eks-homolog \
  --nodegroup-name oficina-workers-homolog \
  --scaling-config minSize=1,maxSize=2,desiredSize=2
kubectl get nodes
```

Esperado: 2 nodes `Ready` em alguns minutos. Se `kubectl get nodes` vier vazio e os pods ficarem `Pending`, veja a tabela de problemas no fim deste documento.

## 4. Cadastros

```bash
cd "$POS/oficina-cadastros-service"
(cd terraform && terraform init -input=false && terraform plan -var env=homolog -out=tfplan && terraform apply tfplan)
bash scripts/bootstrap-db.sh homolog
IMAGE=ghcr.io/tech-challenge-oficina-mecanica-soat/oficina-cadastros-service:latest bash scripts/deploy.sh homolog
```

Esperado: `Deploy concluido` e o pod `oficina-cadastros-service` em `1/1`.

## 5. Recursos AWS e mensageria, na ordem das dependências

Cada serviço assina tópicos publicados por outros, e o Terraform lê os ARNs no Parameter Store. Por isso a ordem importa.

**5.1 Execução, primeira passada.** Primeiro o `infra/bootstrap` (bucket de state e tabela de lock, detalhes e contornos no [README](../README.md), seção Infraestrutura). Depois, em `infra/terraform/envs/homolog`, aplique só o que não depende de outros serviços. O módulo `execucao-messaging` lê no SSM os ARNs dos tópicos `os.criada`, `os.cancelada` e `orcamento.aprovado` por um data source; enquanto esses parâmetros não existem, um `plan` completo aborta com `ParameterNotFound` e não cria nada (nem tabela, nem fila, nem tópicos), e os passos 5.2 e 5.3 falhariam por falta de `diagnostico.concluido` e `execucao.*`. Por isso use `-target`:

```bash
cd "$POS/oficina-execucao-service/infra/terraform/envs/homolog"
terraform init -input=false
terraform plan -out=tfplan \
  -target=module.execucao_table \
  -target=module.execucao_messaging.aws_sqs_queue.execucao_dlq \
  -target=module.execucao_messaging.aws_sqs_queue.execucao_queue \
  -target=module.execucao_messaging.aws_ssm_parameter.queue_url \
  -target=module.execucao_messaging.aws_sns_topic.published \
  -target=module.execucao_messaging.aws_ssm_parameter.published_topic_arns
terraform apply tfplan
```

Esperado: tabela DynamoDB, fila, DLQ, os 3 tópicos publicados e os parâmetros do SSM. As 3 assinaturas e a policy da fila (`allow_sns`) ficam para a segunda passada (5.5).

**5.2 OS, fase 1.** Descubra o nome real da role dos nodes (o padrão `LabEksNodeRole` não existe no Academy) e assine só os tópicos que já existem:

```bash
NODE_ROLE=$(aws iam list-roles --query "Roles[?contains(RoleName,'LabEksNodeRole')].RoleName" --output text)
cd "$POS/oficina-os-service/terraform"
terraform init -input=false
terraform plan -out=tfplan -var environment=homolog -var node_role_name="$NODE_ROLE" \
  -var 'consumed_events=["diagnostico.concluido","execucao.iniciada","execucao.finalizada","cliente.desativado"]'
terraform apply tfplan
```

Há um PR de correção aberto no repositório do OS; até o merge, passe o valor como acima.

**5.3 Billing, só a mensageria.** O Terraform completo do Billing pressupõe IRSA e um provider PostgreSQL, que o Academy não oferece. Use uma cópia em diretório fixo (`$POS/.billing-tf-academy`, fora dos repositórios), com state local e `-target` apenas nos recursos de mensageria:

```bash
BILLING_TF="$POS/.billing-tf-academy"
mkdir -p "$BILLING_TF" && cp "$POS"/oficina-billing-service/terraform/*.tf "$BILLING_TF" && cd "$BILLING_TF"
printf 'terraform {\n  backend "local" {}\n}\n' > backend_override.tf
# variáveis exigidas pelo módulo, mas não usadas pelos recursos alvo (valores fictícios)
export TF_VAR_rds_admin_username=nao-usado TF_VAR_rds_admin_password=nao-usado
export TF_VAR_oidc_provider_arn=nao-usado TF_VAR_oidc_provider_url=https://nao-usado
terraform init -input=false
terraform plan -var environment=homolog -out=tfplan \
  -target=aws_sns_topic.owned -target=aws_ssm_parameter.owned_topic_arn \
  -target=aws_sqs_queue.billing_dlq -target=aws_sqs_queue.billing_queue \
  -target=aws_sqs_queue_policy.billing_queue_policy \
  -target=aws_sqs_queue_redrive_allow_policy.billing_dlq \
  -target=aws_sns_topic_subscription.consumed
terraform apply tfplan
```

O diretório `$BILLING_TF` guarda o state local: não apague, pois a derrubada precisa dele (se o state for de outra conta do Academy, aplique a regra de state do início). O plano deve mostrar só criações de SNS, SSM e SQS.

**5.4 OS, fase 2.** Agora que os tópicos do Billing existem, assine todos os eventos (a lista padrão):

```bash
cd "$POS/oficina-os-service/terraform"
terraform plan -out=tfplan -var environment=homolog -var node_role_name="$NODE_ROLE"
terraform apply tfplan
```

**5.5 Execução, segunda passada.** Agora os parâmetros do OS e do Billing existem. Este é o plano completo (sem `-target`): ele cria as 3 assinaturas e a policy da fila, de modo que ela passa a assinar `os.criada`, `os.cancelada` e `orcamento.aprovado`:

```bash
cd "$POS/oficina-execucao-service/infra/terraform/envs/homolog"
terraform plan -out=tfplan
terraform apply tfplan
```

Verificação (os tópicos de todos os serviços devem estar no SSM):

```bash
aws ssm get-parameters-by-path --path /oficina/homolog/messaging/topics/ --recursive --query "Parameters[].Name" --output text | tr '\t' '\n' | sort
```

Esperado: parâmetros de `os.criada`, `os.cancelada`, `diagnostico.concluido`, `execucao.iniciada`, `execucao.finalizada`, `orcamento.*`, `pagamento.*` e `cliente.desativado`.

## 6. Deploy do OS

Variante com Service interno (`ClusterIP`) no lugar do NLB internet-facing do script original, sem escrever no SSM:

```bash
cd "$POS/oficina-execucao-service"
bash demo/academy/os/deploy-os-academy.sh homolog
```

O clone do OS é localizado como pasta irmã `oficina-os-service` (subindo a árvore a partir do script, inclusive em worktree aninhado); para outro caminho, `OS_REPO=<caminho>`. Esperado: `OS Service no ar (interno)`.

## 7. Deploy do Billing

Overlay do Academy (1 réplica, Mercado Pago fake, sem Ingress, credenciais da sessão) mais o proxy `cadastros-proxy`:

```bash
bash demo/academy/billing/deploy-billing-academy.sh
```

O script localiza o clone do Billing pela variável `BILLING_REPO` ou, se ela não existir, pela pasta irmã `oficina-billing-service`, subindo a árvore a partir do próprio script (funciona também em worktree aninhado). Ele monta um overlay temporário ao lado do clone, porque o kustomize só aceita bases por caminho relativo, e o remove ao terminar. A licença do New Relic vem de `NEW_RELIC_LICENSE_KEY` (ou, na falta dela, do secret `oficina-secrets`, se já existir).

O proxy reescreve o campo `nome` do Cadastros para `descricao`, que é o que o Billing lê (ver o bug na tabela de problemas). A imagem do Billing está fixada no `newTag` do `demo/academy/billing/kustomization.yaml` (as imagens do Billing levam como tag o sha de 12 caracteres do commit, não há `latest`). Depois que o PR de correção do Billing for mesclado e a imagem for reconstruída, são necessárias duas edições nesse arquivo: trocar o `newTag` pelo sha da imagem nova e remover `cadastros-proxy.yaml` de `resources`; no patch do ConfigMap, aponte `Services__CadastrosService__BaseUrl` para `http://oficina-cadastros-service.oficina.svc.cluster.local`. Remover só o proxy, mantendo a tag antiga, deixa o bug no ar. Não há variável de ambiente para a tag: a edição é no arquivo.

Esperado: `Billing no ar (interno)`.

## 8. Deploy da Execução e ajuste dos HPAs

```bash
bash scripts/deploy.sh homolog
```

Depois dos deploys, fixe os HPAs do OS, do Cadastros e da Execução em 1 réplica. Os picos de CPU na partida fazem o HPA escalar e esgotar os 2 nodes, e aí as liveness probes (timeout de 1 s) reiniciam o Billing:

```bash
for h in oficina-os-service oficina-cadastros-service oficina-execucao-service; do
  kubectl patch hpa $h -n oficina --type merge -p '{"spec":{"minReplicas":1,"maxReplicas":1}}'
done
kubectl get pods -n oficina
```

Esperado: Cadastros, OS, Execução, Billing e `cadastros-proxy` em `1/1 Running`.

### Renovando as credenciais

Quando o laboratório é renovado, o Academy cancela as credenciais antigas (política `voc-cancel-cred`). OS, Execução e Billing guardam as credenciais da sessão nos secrets e passam a falhar com `AccessDenied`, ficando `0/1` (o Cadastros usa a role do node e não é afetado). Com as credenciais novas exportadas (as mesmas variáveis do início), um único script refaz o pull secret e o deploy dos quatro serviços (os clones são localizados como no passo 7) e reaplica os HPAs:

```bash
bash demo/academy/renovar-credenciais.sh
```

Os valores de `GHCR_USERNAME`, `GHCR_PAT` e `NEW_RELIC_LICENSE_KEY` são lidos só do ambiente; o script não os imprime nem os grava.

## 9. Rodar o fluxo completo

```bash
bash demo/saga-completa.sh happy      # OS termina Concluida
bash demo/saga-completa.sh rollback   # orçamento recusado: OS Cancelada, Execução Cancelado
```

O script abre `kubectl port-forward` nas portas 5010 (OS), 5011 (Cadastros), 5012 (Execução) e 5013 (Billing) e se recusa a iniciar se alguma estiver ocupada. Leva de 60 a 90 segundos, porque os eventos viajam por SNS e SQS entre os serviços. Ele cria cliente, veículo, peça e serviço no Cadastros, abre a OS, registra o diagnóstico, gera o orçamento (R$ 500,00), e então aprova e conclui (`happy`) ou recusa e compensa (`rollback`).

Esperado no `happy`: a sequência de status da OS `EmDiagnostico`, `AguardandoAprovacao`, `Aprovada`, `EmReparo`, `AguardandoPagamento` e `Concluida`, e a mensagem final `Saga completa`. No `rollback`: OS `Cancelada`, Execução `Cancelado`, OS fora da fila e a mensagem final de compensação.

Riscos conhecidos:

- O Mercado Pago fake do Billing guarda as cobranças em memória. Um restart do Billing entre a criação da cobrança e o webhook a perde (o script faz os dois em segundos).
- A geração do orçamento pode demorar mais logo depois de reiniciar um pod. Se der timeout, rode de novo.

## Problemas conhecidos

| Sintoma | Causa | Correção |
|---|---|---|
| `reading Secrets Manager Secret ... AccessDenied` ou recursos de outra conta no `plan` do RDS | State local do `oficina-infra-db` (ou de outro diretório com state local) veio de outra conta do Academy | Backup do state, `terraform state rm` das entradas `module.rds.*` e `module.secrets.aws_secretsmanager_*`, novo `plan -out`: deve mostrar só criações |
| `aws ssm get-parameter` ou scripts com o parâmetro `/oficina/...` falham com caminho `C:/Program Files/Git/...` | Conversão de caminhos do Git Bash | `export MSYS_NO_PATHCONV=1` |
| `make k8s-apply` reexecutado quer alterar o node group | `desired_size = 1` sem `ignore_changes` | Não reexecutar depois do passo 3; se aconteceu, refaça o escalonamento do passo 3 |
| `terraform plan` do Execução aborta com `ParameterNotFound` | Os tópicos de OS/Billing ainda não existem | Primeira passada com `-target` (passo 5.1) |
| `terraform apply -target=module.rds_ingress` quer substituir o node group | A regra não é necessária (o RDS já libera 5432 para a VPC) | Não rodar; descartar o plano |
| `kubectl get nodes` vazio e pods `Pending` | O node substituto às vezes não se registra; observado uma vez numa conta real, o log de boot do node mostrava deny explícito de uma política da organização em `ec2:DescribeInstances` (não é determinístico nem tem periodicidade conhecida) | Terminar a instância EC2 travada para o autoscaling group lançar outra e esperar o node aparecer |
| Pods `Pending` por falta de recurso | Um `t3.small` não comporta os 4 serviços | Passo 3 (2 nodes) |
| Pods de OS, Execução ou Billing `0/1` com `AccessDenied` depois de renovar o laboratório | Credenciais antigas canceladas (`voc-cancel-cred`) nos secrets | `bash demo/academy/renovar-credenciais.sh` |
| `LabEksNodeRole` não existe no `terraform apply` do OS | O nome real da role difere no Academy | Passar `-var node_role_name=<nome real>` (passo 5.2) |
| Apply do OS falha ao ler o parâmetro de um tópico | O dono do tópico ainda não aplicou | Respeitar a ordem do passo 5 (fases 1 e 2) |
| Billing reinicia sozinho (liveness) e há mais réplicas que o esperado | O HPA escalou na partida e esgotou os 2 nodes | Fixar HPAs do OS e Cadastros em 1 réplica (passo 8) |
| Orçamento sem itens ou erro ao gerar o orçamento | Bug de contrato: o Billing lê `descricao`, o Cadastros devolve `nome` | Manter o proxy `cadastros-proxy` até a correção do Billing ser mesclada e a imagem reconstruída; depois trocar o `newTag` e remover o proxy do overlay (passo 7) |
| Pagamento não aparece ou webhook falha | O Billing reiniciou entre criar a cobrança e o webhook (Mercado Pago fake em memória) | Rodar o script de novo |
| `saga-completa.sh` aborta com "A porta ... já está respondendo" | Port-forward antigo nas portas 5010 a 5013 | Encerrar o processo (`netstat -ano \| grep :501`, `taskkill /F /PID <pid>` (barras simples, pois `MSYS_NO_PATHCONV=1` está exportado; sem ele, use `//F //PID`)) |
| Timeout esperando um status | Eventos ainda em trânsito ou pod recém-reiniciado | Conferir `kubectl get pods -n oficina` e `kubectl logs deploy/<servico> -n oficina`; rodar de novo |

## Derrubada

- Este fluxo não cria Service do tipo LoadBalancer: o deploy do OS usa `ClusterIP`. Se alguém usou o `scripts/deploy-k8s.sh` original do OS, que cria um NLB, apague esse Service antes de derrubar a VPC (`kubectl delete svc oficina-os-service -n oficina`), senão o load balancer bloqueia a remoção.
- Os recursos de mensageria do Billing foram criados com state local em `$BILLING_TF` (`$POS/.billing-tf-academy`, passo 5.3): para removê-los, rode lá `terraform plan -destroy -out=tfplan` com os mesmos `-target` e aplique o plano.
- A derrubada da infra (cluster e RDS) segue o `make down` do `oficina-infra-k8s`, que termina com `make sanity-check` para listar o que ainda possa estar cobrando. Detalhes e ressalvas ficam naquele repositório.
- Os recursos AWS de Execução, OS e Cadastros (SNS, SQS, DynamoDB, parâmetros do SSM) custam muito pouco e são opcionais de remover, já que cada sessão do Academy começa numa conta nova. Se quiser removê-los, rode `terraform plan -destroy -out=tfplan` e aplique o plano, em ordem inversa à criação: Execução (`infra/terraform/envs/homolog`), OS (`terraform/` do OS, com as mesmas variáveis) e Cadastros (`terraform/` do Cadastros, com `-var env=homolog`).
