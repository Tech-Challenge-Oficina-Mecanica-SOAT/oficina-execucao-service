#!/usr/bin/env bash
# Deploy do Billing no AWS Academy sem alterar o repositório do Billing (ver kustomization.yaml).
# Requer: credenciais da sessão exportadas (AWS_ACCESS_KEY_ID, AWS_SECRET_ACCESS_KEY, AWS_SESSION_TOKEN)
# e o secret ghcr-pull-secret no namespace oficina. O clone do oficina-billing-service é localizado por
# BILLING_REPO (se informado) ou pela pasta irmã oficina-billing-service, subindo a árvore a partir deste
# script (funciona também em worktree aninhado). Um overlay temporário é montado ao lado do clone.
# A licença do New Relic vem de NEW_RELIC_LICENSE_KEY; sem ela, reaproveita a do secret oficina-secrets.
# Uso: [BILLING_REPO=<clone>] bash demo/academy/billing/deploy-billing-academy.sh [homolog]
set -euo pipefail
export AWS_DEFAULT_REGION=${AWS_DEFAULT_REGION:-us-east-1} MSYS_NO_PATHCONV=1
ENV="${1:-homolog}"; NS=oficina
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/../_localizar-clone.sh"
localizar_clone oficina-billing-service BILLING_REPO k8s/base || { echo "Clone do oficina-billing-service não encontrado. Informe BILLING_REPO=/caminho/oficina-billing-service" >&2; exit 1; }
echo "Usando o Billing em: $BILLING_REPO"
ssm() { aws ssm get-parameter --name "$1" --query Parameter.Value --output text; }
DB_ENDPOINT=$(ssm /oficina/$ENV/db/endpoint); DB_PORT=$(ssm /oficina/$ENV/db/port); DB_USER=$(ssm /oficina/$ENV/db/username)
DB_PASSWORD=$(aws secretsmanager get-secret-value --secret-id oficina/$ENV/db-password --query SecretString --output text)
kubectl create secret generic billing-academy-secrets -n $NS \
  --from-literal=ConnectionStrings__BillingDb="Host=$DB_ENDPOINT;Port=$DB_PORT;Database=oficina_billing;Username=$DB_USER;Password=$DB_PASSWORD;Trust Server Certificate=true" \
  --from-literal=AWS_ACCESS_KEY_ID="${AWS_ACCESS_KEY_ID:?}" --from-literal=AWS_SECRET_ACCESS_KEY="${AWS_SECRET_ACCESS_KEY:?}" \
  --from-literal=AWS_SESSION_TOKEN="${AWS_SESSION_TOKEN:?}" --dry-run=client -o yaml | kubectl apply -f - >/dev/null
echo "Secret billing-academy-secrets aplicado."
# Licença do New Relic: variável de ambiente primeiro; senão, a do Secret compartilhado oficina-secrets (se existir)
NR="${NEW_RELIC_LICENSE_KEY:-}"
if [ -z "$NR" ]; then
  NR=$(kubectl get secret oficina-secrets -n $NS -o jsonpath='{.data.NEW_RELIC_LICENSE_KEY}' 2>/dev/null | base64 -d || true)
fi
if [ -n "$NR" ]; then
  kubectl create secret generic newrelic-license -n $NS --from-literal=license-key="$NR" --dry-run=client -o yaml | kubectl apply -f - >/dev/null
  echo "Secret newrelic-license aplicado."
fi

# Overlay temporário ao lado do clone do Billing (kustomize só aceita bases por caminho relativo)
OVERLAY_TMP="$(dirname "$BILLING_REPO")/.billing-academy-overlay.$$"
mkdir -p "$OVERLAY_TMP"
trap 'rm -rf "$OVERLAY_TMP"' EXIT
cp "$SCRIPT_DIR/kustomization.yaml" "$SCRIPT_DIR/cadastros-proxy.yaml" "$OVERLAY_TMP/"
sed -i "s#^  - \.\./\.\./\.\./\.\./oficina-billing-service/k8s/base#  - ../$(basename "$BILLING_REPO")/k8s/base#" "$OVERLAY_TMP/kustomization.yaml"
grep -q "k8s/base" "$OVERLAY_TMP/kustomization.yaml" || { echo "Falha ao montar o overlay temporário." >&2; exit 1; }
(cd "$OVERLAY_TMP" && kubectl apply -k .)
kubectl rollout restart deployment/oficina-billing-service -n $NS
kubectl rollout status deployment/cadastros-proxy -n $NS --timeout=3m
kubectl rollout status deployment/oficina-billing-service -n $NS --timeout=5m
echo "Billing no ar (interno): http://oficina-billing-service.$NS.svc.cluster.local"
