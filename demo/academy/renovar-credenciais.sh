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
# Layout esperado: os clones oficina-cadastros-service, oficina-os-service e oficina-infra-k8s são pastas
# irmãs deste repositório. Para outro local, defina CADASTROS_REPO, OS_REPO ou INFRA_K8S_REPO.
# Uso: bash demo/academy/renovar-credenciais.sh [homolog]
set -euo pipefail
export AWS_DEFAULT_REGION="${AWS_DEFAULT_REGION:-us-east-1}" MSYS_NO_PATHCONV=1

AQUI="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
RAIZ="$(cd "$AQUI/../.." && pwd)"
ENV="${1:-homolog}"
CADASTROS_REPO="${CADASTROS_REPO:-$RAIZ/../oficina-cadastros-service}"
OS_REPO="${OS_REPO:-$RAIZ/../oficina-os-service}"
INFRA_K8S_REPO="${INFRA_K8S_REPO:-$RAIZ/../oficina-infra-k8s}"
export OS_REPO

: "${AWS_ACCESS_KEY_ID:?Exporte AWS_ACCESS_KEY_ID (sessão nova do Academy)}"
: "${AWS_SECRET_ACCESS_KEY:?Exporte AWS_SECRET_ACCESS_KEY}"
: "${AWS_SESSION_TOKEN:?Exporte AWS_SESSION_TOKEN}"
: "${GHCR_USERNAME:?Exporte GHCR_USERNAME}"
: "${GHCR_PAT:?Exporte GHCR_PAT}"

for d in "$CADASTROS_REPO" "$OS_REPO" "$INFRA_K8S_REPO"; do
  [ -d "$d" ] || { echo "Clone não encontrado: $d (defina CADASTROS_REPO, OS_REPO ou INFRA_K8S_REPO)." >&2; exit 1; }
done
aws sts get-caller-identity >/dev/null 2>&1 || { echo "Sessão AWS inválida ou expirada." >&2; exit 1; }

CLUSTER_NAME=$(aws ssm get-parameter --name "/oficina/$ENV/k8s/cluster-name" --query Parameter.Value --output text)
aws eks update-kubeconfig --region "$AWS_DEFAULT_REGION" --name "$CLUSTER_NAME" >/dev/null

etapa() { printf '\n==> %s\n' "$*"; }

etapa "ghcr-pull-secret"
bash "$INFRA_K8S_REPO/scripts/create-ghcr-pull-secret.sh" "$ENV" >/dev/null

etapa "Cadastros"
# O deploy.sh do Cadastros não reinicia o pod: sem o restart, ele manteria as credenciais antigas.
(cd "$CADASTROS_REPO" && bash scripts/deploy.sh "$ENV")
kubectl rollout restart deployment/oficina-cadastros-service -n oficina
kubectl rollout status deployment/oficina-cadastros-service -n oficina --timeout=5m

etapa "OS Service"
bash "$AQUI/os/deploy-os-academy.sh" "$ENV"

etapa "Billing"
bash "$AQUI/billing/deploy-billing-academy.sh"

etapa "Execução"
(cd "$RAIZ" && bash scripts/deploy.sh "$ENV")

# Mantém os HPAs do OS e do Cadastros em 1 réplica (picos de CPU na partida esgotam os 2 nodes)
for hpa in oficina-os-service oficina-cadastros-service; do
  kubectl patch hpa "$hpa" -n oficina --type merge -p '{"spec":{"minReplicas":1,"maxReplicas":1}}' >/dev/null || true
done

etapa "Estado final"
kubectl get pods -n oficina
