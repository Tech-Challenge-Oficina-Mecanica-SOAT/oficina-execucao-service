#!/usr/bin/env bash
# Funcoes compartilhadas pelos scripts de demo. Uso: source "$(dirname "$0")/lib/demo-lib.sh"
# Pre-requisitos do host: Docker e curl (sem jq, uuidgen ou AWS CLI).

# Git Bash no Windows converte argumentos que parecem caminhos (ex.: /000000000000/fila)
export MSYS_NO_PATHCONV=1

DEMO_LIB_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$DEMO_LIB_DIR/../.." && pwd)"
COMPOSE_FILE="demo/docker-compose.demo.yml"

export API_URL="${API_URL:-http://localhost:${API_PORT:-5000}}"
export WIREMOCK_URL="http://localhost:8080"
export API_KEY="execucao-internal-api-key-local-dev"
export AWS_DEFAULT_REGION="us-east-1"

# Alvo da demo: local (Docker/LocalStack/Wiremock) ou eks (ambiente homolog)
TARGET="${TARGET:-local}"
case "$TARGET" in
  local|eks) ;;
  *) printf '%s❌ TARGET invalido: "%s". Use local ou eks.%s\n' $'\033[1;31m' "$TARGET" $'\033[0m' >&2; exit 1 ;;
esac
export TARGET

VERIF_QUEUE_URL="http://localhost:4566/000000000000/oficina-execucao-verificacao-queue"

# Os caminhos do compose sao relativos a raiz do repositorio
cd "$REPO_ROOT"

_C_AMARELO=$'\033[1;33m'
_C_VERDE=$'\033[1;32m'
_C_VERMELHO=$'\033[1;31m'
_C_CIANO=$'\033[0;36m'
_C_RESET=$'\033[0m'
_PASSO=0

step() {
  _PASSO=$((_PASSO + 1))
  printf '\n%s▶ %d. %s%s\n' "$_C_AMARELO" "$_PASSO" "$*" "$_C_RESET"
}

ok() { printf '%s✅ %s%s\n' "$_C_VERDE" "$*" "$_C_RESET"; }

fail() {
  printf '%s❌ %s%s\n' "$_C_VERMELHO" "$*" "$_C_RESET" >&2
  exit 1
}

info() { printf '%s   %s%s\n' "$_C_CIANO" "$*" "$_C_RESET"; }

banner_sucesso() {
  local titulo="$1"
  printf '\n%s============================================================\n' "$_C_VERDE"
  printf '  🎉 %s\n' "$titulo"
  printf '============================================================%s\n' "$_C_RESET"
}

gen_uuid() {
  local u=""
  if [ -r /proc/sys/kernel/random/uuid ]; then
    u="$(cat /proc/sys/kernel/random/uuid)"
  elif command -v uuidgen >/dev/null 2>&1; then
    u="$(uuidgen)"
  elif command -v powershell.exe >/dev/null 2>&1; then
    u="$(powershell.exe -NoProfile -Command '[guid]::NewGuid().ToString()' | tr -d '\r')"
  elif command -v powershell >/dev/null 2>&1; then
    u="$(powershell -NoProfile -Command '[guid]::NewGuid().ToString()' | tr -d '\r')"
  fi
  [ -n "$u" ] || fail "Nao foi possivel gerar UUID (sem /proc, uuidgen ou powershell)"
  printf '%s\n' "$u" | tr 'A-F' 'a-f'
}

aws_local() {
  case "$TARGET" in
    local) docker compose -f "$COMPOSE_FILE" exec -T localstack awslocal "$@" ;;
    eks) aws "$@" ;;
  esac
}

topic_arn() { printf 'arn:aws:sns:us-east-1:000000000000:%s\n' "$1"; }

# publicar_evento <topico-nome> <eventType> <data-json> [eventId]
publicar_evento() {
  local topico="$1" event_type="$2" data_json="$3" event_id="${4:-}"
  [ -n "$event_id" ] || event_id="$(gen_uuid)"
  local envelope
  envelope="$(printf '{"eventId":"%s","eventType":"%s","eventVersion":"1.0","occurredAt":"%s","correlationId":"%s","producer":"demo-script","data":%s}' \
    "$event_id" "$event_type" "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$(gen_uuid)" "$data_json")"
  case "$TARGET" in
    local)
      aws_local sns publish --topic-arn "$(topic_arn "$topico")" --message "$envelope" >/dev/null \
        || fail "Falha ao publicar $event_type em $topico"
      ;;
    eks)
      # o topico e ignorado: o evento entra direto na fila de entrada real do Execucao
      local origem="OS Service"
      case "$event_type" in orcamento.*) origem="Billing Service" ;; esac
      aws sqs send-message --queue-url "$EKS_QUEUE_URL" --message-body "$envelope" >/dev/null \
        || fail "Falha ao injetar $event_type na fila do Execução"
      info "evento $event_type injetado direto na fila do Execução (no lugar do $origem)"
      ;;
  esac
}

# api <METHOD> <path> [body-json]: imprime o corpo; retorna nao-zero em HTTP >= 400
api() {
  local method="$1" path="$2" body="${3:-}"
  local args=(-s -w '\n%{http_code}' -X "$method" -H "X-Internal-Api-Key: $API_KEY")
  if [ -n "$body" ]; then
    args+=(-H 'Content-Type: application/json' -d "$body")
  fi
  local resp code corpo
  resp="$(curl "${args[@]}" "$API_URL$path")" || return 1
  code="${resp##*$'\n'}"
  corpo=""
  if [ "$resp" != "$code" ]; then corpo="${resp%$'\n'*}"; fi
  if [ -n "$corpo" ]; then printf '%s\n' "$corpo"; fi
  case "$code" in
    [0-9][0-9][0-9]) [ "$code" -lt 400 ] ;;
    *) return 1 ;;
  esac
}

# json_field <campo>: le JSON do stdin e imprime o valor string do campo
json_field() {
  # grep -o separa as ocorrencias; head pega a PRIMEIRA (o sed guloso pegaria a ultima)
  grep -o '"'"$1"'"[[:space:]]*:[[:space:]]*"[^"]*"' | head -n 1 | sed 's/^[^:]*:[[:space:]]*"\(.*\)"$/\1/' || true
}

exec_status() {
  api GET "/execucao/$1" 2>/dev/null | json_field status || true
}

# expect_status <osId> <esperado>: poll de 1 s por ate 40 s
expect_status() {
  local os_id="$1" esperado="$2" atual="" i
  for i in $(seq 1 40); do
    atual="$(exec_status "$os_id")"
    if [ "$atual" = "$esperado" ]; then
      ok "Status da execucao: $esperado"
      return 0
    fi
    sleep 1
  done
  fail "Timeout aguardando status '$esperado' (ultimo status: '${atual:-<nenhum>}')"
}

# esperar_evento <eventType> <osId>: imprime o JSON da mensagem da fila de verificacao
esperar_evento() {
  local event_type="$1" os_id="$2"
  local fim=$((SECONDS + 40)) linhas handle corpo
  local re_tipo='"eventType"[[:space:]]*:[[:space:]]*"'"$event_type"'"'
  local re_os='"osId"[[:space:]]*:[[:space:]]*"'"$os_id"'"'
  while [ "$SECONDS" -lt "$fim" ]; do
    linhas="$(aws_local sqs receive-message --queue-url "$VERIF_QUEUE_URL" \
      --max-number-of-messages 10 --wait-time-seconds 2 \
      --query 'Messages[].[ReceiptHandle,Body]' --output text 2>/dev/null | tr -d '\r')" || linhas=""
    if [ -n "$linhas" ] && [ "$linhas" != "None" ]; then
      # fd 3: o docker exec -T dentro do loop consumiria o stdin e perderia as demais linhas
      while IFS=$'\t' read -r -u 3 handle corpo; do
        [ -n "$handle" ] || continue
        if [[ "$corpo" =~ $re_os ]]; then
          if [[ "$corpo" =~ $re_tipo ]]; then
            aws_local sqs delete-message --queue-url "$VERIF_QUEUE_URL" --receipt-handle "$handle" >/dev/null
            printf '%s\n' "$corpo"
            return 0
          fi
          # mesma OS, outro tipo de evento: devolve para uma espera futura
          aws_local sqs change-message-visibility --queue-url "$VERIF_QUEUE_URL" \
            --receipt-handle "$handle" --visibility-timeout 0 >/dev/null 2>&1 || true
        else
          # outra OS / execucao anterior: lixo, descarta para nao travar a fila
          aws_local sqs delete-message --queue-url "$VERIF_QUEUE_URL" \
            --receipt-handle "$handle" >/dev/null 2>&1 || true
        fi
      done 3<<< "$linhas"
    fi
    sleep 1
  done
  fail "Timeout aguardando evento $event_type para a OS $os_id"
}

verificar_ambiente_local() {
  local rodando svc
  rodando="$(docker compose -f "$COMPOSE_FILE" ps --status running --services 2>/dev/null || true)"
  for svc in localstack wiremock api; do
    if ! grep -qx "$svc" <<< "$rodando"; then
      fail "Ambiente fora do ar (servico ausente: $svc). Rode: docker compose -f demo/docker-compose.demo.yml up -d --build"
    fi
  done
}

EKS_NAMESPACE="oficina"
EKS_LOCAL_PORT=5002
EKS_SSM_PREFIX="/oficina/homolog"
EKS_TOPICOS=(diagnostico.concluido execucao.iniciada execucao.finalizada)
EKS_QUEUE_URL=""
EKS_TOPIC_ARNS=()
EKS_PF_PID=""
PROBE_QUEUE_URL=""
PROBE_SUBS=()
PROBE_PREFIX="oficina-demo-probe-"

# ssm_valor <nome>: imprime o valor do parametro SSM (falha se ausente)
ssm_valor() {
  aws ssm get-parameter --name "$1" --query Parameter.Value --output text 2>/dev/null | tr -d '\r'
}

# varrer_sobras_eks: remove filas oficina-demo-probe-* e assinaturas delas deixadas por execucoes mortas
varrer_sobras_eks() {
  local arn subs sub q
  for arn in "${EKS_TOPIC_ARNS[@]}"; do
    subs="$(aws sns list-subscriptions-by-topic --topic-arn "$arn" \
      --query "Subscriptions[?contains(Endpoint,'$PROBE_PREFIX')].SubscriptionArn" --output text 2>/dev/null | tr -d '\r' | tr '\t' '\n')" || subs=""
    for sub in $subs; do
      case "$sub" in arn:aws:sns:*) aws sns unsubscribe --subscription-arn "$sub" >/dev/null 2>&1 || true ;; esac
    done
  done
  for q in $(aws sqs list-queues --queue-name-prefix "$PROBE_PREFIX" --query 'QueueUrls[]' --output text 2>/dev/null | tr -d '\r' | tr '\t' '\n'); do
    case "$q" in http*) aws sqs delete-queue --queue-url "$q" >/dev/null 2>&1 || true ;; esac
  done
  return 0
}

# limpar_eks: idempotente; cancela assinaturas, apaga a fila de verificacao e encerra o port-forward
limpar_eks() {
  local sub winpid
  if [ "${#PROBE_SUBS[@]}" -gt 0 ] || [ -n "$PROBE_QUEUE_URL" ] || [ -n "$EKS_PF_PID" ]; then
    info "Limpando recursos temporários da demo (assinaturas, fila de verificação, port-forward)..."
  fi
  for sub in "${PROBE_SUBS[@]}"; do
    aws sns unsubscribe --subscription-arn "$sub" >/dev/null 2>&1 || true
  done
  PROBE_SUBS=()
  if [ -n "$PROBE_QUEUE_URL" ]; then
    aws sqs delete-queue --queue-url "$PROBE_QUEUE_URL" >/dev/null 2>&1 || true
    PROBE_QUEUE_URL=""
  fi
  if [ -n "$EKS_PF_PID" ]; then
    # no Git Bash o kubectl.exe tem PID do Windows distinto; captura antes do kill
    winpid="$(ps -p "$EKS_PF_PID" 2>/dev/null | awk 'NR==2{print $4}')" || winpid=""
    kill "$EKS_PF_PID" >/dev/null 2>&1 || true
    sleep 1
    if kill -0 "$EKS_PF_PID" >/dev/null 2>&1; then
      kill -9 "$EKS_PF_PID" >/dev/null 2>&1 || true
      if [ -n "$winpid" ] && command -v taskkill >/dev/null 2>&1; then
        taskkill //F //PID "$winpid" >/dev/null 2>&1 || true
      fi
    fi
    EKS_PF_PID=""
  fi
  return 0
}

verificar_ambiente_eks() {
  local faltando="" cmd i code nome q_arn policy attrs sub
  for cmd in aws kubectl curl; do
    command -v "$cmd" >/dev/null 2>&1 || faltando="$faltando $cmd"
  done
  [ -z "$faltando" ] || fail "Pré-requisitos ausentes no host:$faltando"

  aws sts get-caller-identity >/dev/null 2>&1 \
    || fail "Sessão AWS inválida ou expirada. Atualize as credenciais do AWS Academy."
  ok "Sessão AWS válida"

  EKS_QUEUE_URL="$(ssm_valor "$EKS_SSM_PREFIX/execucao/queue-url")" || EKS_QUEUE_URL=""
  [ -n "$EKS_QUEUE_URL" ] && [ "$EKS_QUEUE_URL" != "None" ] \
    || fail "Não consegui ler $EKS_SSM_PREFIX/execucao/queue-url no SSM (o deploy do Execução existe?)."
  EKS_TOPIC_ARNS=()
  for nome in "${EKS_TOPICOS[@]}"; do
    q_arn="$(ssm_valor "$EKS_SSM_PREFIX/messaging/topics/$nome/arn")" || q_arn=""
    [ -n "$q_arn" ] && [ "$q_arn" != "None" ] \
      || fail "Não consegui ler o ARN do tópico $nome no SSM ($EKS_SSM_PREFIX/messaging/topics/$nome/arn)."
    EKS_TOPIC_ARNS+=("$q_arn")
  done

  # a chave nunca e impressa
  API_KEY="$(kubectl get secret oficina-execucao-secrets -n "$EKS_NAMESPACE" \
    -o jsonpath='{.data.InternalApi__ApiKey}' 2>/dev/null | base64 -d 2>/dev/null | tr -d '\r\n')" || API_KEY=""
  [ -n "$API_KEY" ] \
    || fail "Não consegui ler a chave InternalApi__ApiKey do secret oficina-execucao-secrets (kubectl aponta para o cluster certo?)."
  export API_KEY

  if curl -s -o /dev/null --max-time 2 "http://localhost:$EKS_LOCAL_PORT/" 2>/dev/null; then
    fail "A porta $EKS_LOCAL_PORT já está respondendo (port-forward esquecido?). Encerre o processo que a usa e rode de novo: não vou falar com um serviço desconhecido."
  fi

  # trap antes de abrir qualquer acesso/recurso: Ctrl+C ou falha em qualquer ponto limpa tudo
  trap limpar_eks EXIT
  trap 'exit 130' INT TERM

  info "Abrindo port-forward svc/oficina-execucao-service $EKS_LOCAL_PORT:80 (namespace $EKS_NAMESPACE)..."
  kubectl port-forward svc/oficina-execucao-service "$EKS_LOCAL_PORT:80" -n "$EKS_NAMESPACE" >/dev/null 2>&1 &
  EKS_PF_PID=$!
  code=""
  for i in $(seq 1 30); do
    code="$(curl -s -o /dev/null --max-time 2 -w '%{http_code}' "http://localhost:$EKS_LOCAL_PORT/health/ready" 2>/dev/null || true)"
    [ "$code" = "200" ] && break
    sleep 1
  done
  [ "$code" = "200" ] || fail "O serviço não ficou pronto em 30 s via port-forward (/health/ready retornou '${code:-sem resposta}')."
  ok "Execução API acessível em http://localhost:$EKS_LOCAL_PORT (/health/ready = 200)"

  varrer_sobras_eks

  nome="${PROBE_PREFIX}$(gen_uuid | cut -c1-8)"
  PROBE_QUEUE_URL="$(aws sqs create-queue --queue-name "$nome" --query QueueUrl --output text 2>/dev/null | tr -d '\r')" || PROBE_QUEUE_URL=""
  [ -n "$PROBE_QUEUE_URL" ] && [ "$PROBE_QUEUE_URL" != "None" ] || { PROBE_QUEUE_URL=""; fail "Não consegui criar a fila de verificação $nome."; }
  q_arn="$(aws sqs get-queue-attributes --queue-url "$PROBE_QUEUE_URL" --attribute-names QueueArn \
    --query Attributes.QueueArn --output text 2>/dev/null | tr -d '\r')" || q_arn=""
  [ -n "$q_arn" ] && [ "$q_arn" != "None" ] || fail "Não consegui obter o ARN da fila de verificação."

  policy="$(printf '{"Version":"2012-10-17","Statement":[{"Effect":"Allow","Principal":{"Service":"sns.amazonaws.com"},"Action":"sqs:SendMessage","Resource":"%s","Condition":{"ArnEquals":{"aws:SourceArn":["%s","%s","%s"]}}}]}' \
    "$q_arn" "${EKS_TOPIC_ARNS[0]}" "${EKS_TOPIC_ARNS[1]}" "${EKS_TOPIC_ARNS[2]}")"
  attrs="{\"Policy\":\"${policy//\"/\\\"}\"}"
  aws sqs set-queue-attributes --queue-url "$PROBE_QUEUE_URL" --attributes "$attrs" >/dev/null \
    || fail "Não consegui aplicar a policy na fila de verificação."

  for nome in "${EKS_TOPIC_ARNS[@]}"; do
    sub="$(aws sns subscribe --topic-arn "$nome" --protocol sqs --notification-endpoint "$q_arn" \
      --attributes RawMessageDelivery=true --query SubscriptionArn --output text 2>/dev/null | tr -d '\r')" || sub=""
    case "$sub" in
      arn:aws:sns:*) PROBE_SUBS+=("$sub") ;;
      *) fail "Não consegui assinar a fila de verificação no tópico $nome." ;;
    esac
  done

  API_URL="http://localhost:$EKS_LOCAL_PORT"
  export API_URL
  VERIF_QUEUE_URL="$PROBE_QUEUE_URL"
  ok "Fila de verificação temporária criada e assinada nos 3 tópicos de saída"
}

verificar_ambiente() {
  case "$TARGET" in
    local) verificar_ambiente_local ;;
    eks) verificar_ambiente_eks ;;
  esac
}

# abrir_os_simulada: define OS_ID (local: Wiremock simulando o OS Service)
abrir_os_simulada() {
  local resp
  case "$TARGET" in
    local)
      resp="$(curl -s -f -X POST "$WIREMOCK_URL/ordens-servico" -H 'Content-Type: application/json' -d '{}')" \
        || fail "Wiremock nao respondeu em $WIREMOCK_URL/ordens-servico"
      OS_ID="$(printf '%s' "$resp" | json_field id)"
      [ -n "$OS_ID" ] || fail "Resposta do Wiremock sem id: $resp"
      info "$resp"
      ;;
    eks)
      OS_ID="$(gen_uuid)"
      info "O OS Service não existe neste ambiente: o id da OS foi gerado pelo script."
      ;;
  esac
  ok "OS aberta: $OS_ID"
}

# aprovar_orcamento_simulado: define ORCAMENTO_ID (local: Wiremock simulando o Billing Service)
aprovar_orcamento_simulado() {
  ORCAMENTO_ID="$(gen_uuid)"
  case "$TARGET" in
    local)
      curl -s -f -o /dev/null -X POST "$WIREMOCK_URL/orcamentos/$ORCAMENTO_ID/aprovar" \
        || fail "Wiremock nao aprovou o orcamento $ORCAMENTO_ID"
      ok "Orcamento $ORCAMENTO_ID aprovado"
      ;;
    eks)
      info "O Billing Service não existe neste ambiente: o id do orçamento foi gerado pelo script."
      ok "Orçamento $ORCAMENTO_ID gerado (a aprovação será o evento injetado a seguir)"
      ;;
  esac
}

verificar_ambiente
