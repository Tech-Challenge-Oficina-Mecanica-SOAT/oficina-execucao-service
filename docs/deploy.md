# Deploy do Execução Service

Ordem para subir um ambiente (rode no Git Bash, com o lab do AWS Academy ativo e as credenciais renovadas):

1. **Infra base:** `make db-apply k8s-apply kubeconfig` no `oficina-infra-k8s` (cria VPC, EKS e o namespace `oficina`). Não rode `make up`/`make deploy` nesse repositório — isso também sobe o aplicativo monolítico da Fase 3, que não faz parte da Fase 4.
2. **Secret compartilhado de pull do GHCR:** `oficina-infra-k8s/scripts/create-ghcr-pull-secret.sh`, se ainda não tiver sido criado nesta sessão (outro serviço do grupo pode já ter criado).
3. **Recursos AWS do Execução:** já aplicados (`infra/bootstrap` + `infra/terraform/envs/homolog`, ver seção "Infraestrutura (Terraform)" do `README.md`).
4. **Imagem:** publicada pelo workflow `ci.yml` (job `build-docker`) ao mesclar na `main`. Para testar antes do merge, publique manualmente: `docker build -t ghcr.io/tech-challenge-oficina-mecanica-soat/oficina-execucao-service:<tag> . && docker push ghcr.io/tech-challenge-oficina-mecanica-soat/oficina-execucao-service:<tag>` (autenticado no GHCR) e use `IMAGE=...:<tag>` no passo seguinte.
5. **Aplicação:** `export AWS_ACCESS_KEY_ID=... AWS_SECRET_ACCESS_KEY=... AWS_SESSION_TOKEN=...` (sessão atual) + `export NEW_RELIC_LICENSE_KEY=...` (opcional, mas necessário para o APM reportar) + `./scripts/deploy.sh homolog`.
6. **New Relic (infraestrutura do cluster, uma vez por cluster):** `NEW_RELIC_LICENSE_KEY=... make newrelic` no `oficina-infra-k8s`, se ainda não tiver sido instalado nesta sessão.

Verificação: `kubectl port-forward svc/oficina-execucao-service 5002:80 -n oficina` e `curl http://localhost:5002/health/ready`.

## Acesso do pod à AWS

O node do EKS usa a role `LabEksNodeRole`, sem permissão para DynamoDB/SQS/SNS nesta conta AWS Academy — e não é possível anexar política nova nela nem na `LabRole` (`AccessDenied` em `iam:PutRolePolicy`, testado). Por isso `scripts/populate-secret.sh` repassa as credenciais temporárias da sessão atual para o pod via variáveis de ambiente (`AWS_ACCESS_KEY_ID`/`AWS_SECRET_ACCESS_KEY`/`AWS_SESSION_TOKEN`), que o SDK da AWS para .NET usa automaticamente. Essas credenciais expiram em ~4h (mesmo prazo de qualquer sessão AWS Academy) — rode `populate-secret.sh` de novo se for retomar a demonstração depois disso.

Derrubar: `kubectl delete deployment,service,hpa oficina-execucao-service -n oficina`.
