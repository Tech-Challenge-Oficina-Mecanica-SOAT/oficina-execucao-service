#!/usr/bin/env bash
# Renova as credenciais temporárias do AWS Academy nos 4 serviços do cluster.
# Quando o laboratório é renovado, o Academy cancela as credenciais antigas (política voc-cancel-cred) e
# os pods que ainda as usam passam a falhar com AccessDenied e ficam 0/1. Este script refaz o deploy de
# cada serviço com as credenciais novas, reaproveitando o script de deploy de cada um (todos idempotentes).
#
# Antes de rodar, exporte no terminal:
#   AWS_ACCESS_KEY_ID, AWS_SECRET_ACCESS_KEY, AWS_SESSION_TOKEN   (sessão nova do Academy)
#   GHCR_USERNAME, GHCR_PAT                                       (pull secret do GHCR)
#   NEW_RELIC_LICENSE_KEY                                         (opcional, para o APM reportar)
# Os valores são lidos apenas do ambiente: nunca são impressos nem gravados em arquivo.
#
# Os clones oficina-cadastros-service, oficina-os-service e oficina-infra-k8s são localizados como pastas
# irmãs, subindo a árvore a partir deste script (vale também em worktree aninhado). Para outro local,
# defina CADASTROS_REPO, OS_REPO, BILLING_REPO ou INFRA_K8S_REPO.
# Uso: bash demo/academy/renovar-credenciais.sh [homolog]
set -euo pipefail
export AWS_DEFAULT_REGION="${AWS_DEFAULT_REGION:-us-east-1}" MSYS_NO_PATHCONV=1

AQUI="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
RAIZ="$(cd "$AQUI/../.." && pwd)"
ENV="${1:-homolog}"
source "$AQUI/_localizar-clone.sh"
localizar_clone oficina-cadastros-service CADASTROS_REPO scripts || { echo "Clone do oficina-cadastros-service não encontrado (defina CADASTROS_REPO)." >&2; exit 1; }
localizar_clone oficina-os-service OS_REPO k8s || { echo "Clone do oficina-os-service não encontrado (defina OS_REPO)." >&2; exit 1; }
localizar_clone oficina-infra-k8s INFRA_K8S_REPO scripts || { echo "Clone do oficina-infra-k8s não encontrado (defina INFRA_K8S_REPO)." >&2; exit 1; }
export OS_REPO

: "${AWS_ACCESS_KEY_ID:?Exporte AWS_ACCESS_KEY_ID (sessão nova do Academy)}"
: "${AWS_SECRET_ACCESS_KEY:?Exporte AWS_SECRET_ACCESS_KEY}"
: "${AWS_SESSION_TOKEN:?Exporte AWS_SESSION_TOKEN}"
: "${GHCR_USERNAME:?Exporte GHCR_USERNAME}"
: "${GHCR_PAT:?Exporte GHCR_PAT}"

aws sts get-caller-identity >/dev/null 2>&1 || { echo "Sessão AWS inválida ou expirada." >&2; exit 1; }

CLUSTER_NAME=$(aws ssm get-parameter --name "/oficina/$ENV/k8s/cluster-name" --query Parameter.Value --output text)
aws eks update-kubeconfig --region "$AWS_DEFAULT_REGION" --name "$CLUSTER_NAME" >/dev/null

etapa() { printf '\n==> %s\n' "$*"; }

etapa "ghcr-pull-secret"
bash "$INFRA_K8S_REPO/scripts/create-ghcr-pull-secret.sh" "$ENV" >/dev/null

etapa "Cadastros"
# O deploy.sh do Cadastros não reinicia o pod. O Cadastros não guarda credenciais AWS no secret (usa a role
# do node), mas o restart é inofensivo e relê a chave do New Relic.
(cd "$CADASTROS_REPO" && bash scripts/deploy.sh "$ENV")
kubectl rollout restart deployment/oficina-cadastros-service -n oficina
kubectl rollout status deployment/oficina-cadastros-service -n oficina --timeout=5m

etapa "OS Service"
bash "$AQUI/os/deploy-os-academy.sh" "$ENV"

etapa "Billing"
bash "$AQUI/billing/deploy-billing-academy.sh" "$ENV"

etapa "Execução"
(cd "$RAIZ" && bash scripts/deploy.sh "$ENV")


# Mantém os HPAs em 1 réplica (picos de CPU na partida fazem o HPA escalar e esgotam os 2 nodes)
for hpa in oficina-os-service oficina-cadastros-service oficina-execucao-service; do
  kubectl patch hpa "$hpa" -n oficina --type merge -p '{"spec":{"minReplicas":1,"maxReplicas":1}}' >/dev/null \
    || echo "AVISO: não consegui fixar o HPA $hpa em 1 réplica." >&2
done

etapa "Estado final"
kubectl get pods -n oficina
