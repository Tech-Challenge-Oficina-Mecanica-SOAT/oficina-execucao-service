#!/usr/bin/env bash
# Fluxo completo de uma OS pelos 4 microsserviços reais no EKS do AWS Academy.
#   bash demo/saga-completa.sh happy      # caminho feliz: OS Concluida
#   bash demo/saga-completa.sh rollback   # compensação: orçamento recusado, OS Cancelada
# Requisitos: aws e kubectl com sessão válida, kubeconfig do oficina-eks-homolog, curl, openssl,
# e os 4 serviços deployados no namespace oficina (passo a passo em docs/fluxo-completo.md).
# O acesso aos serviços é por kubectl port-forward nas portas 5010 a 5013 (nenhum serviço tem
# URL pública). Leva de 60 a 90 s, porque os eventos viajam por SNS e SQS entre os serviços.
set -euo pipefail
export MSYS_NO_PATHCONV=1 AWS_DEFAULT_REGION="${AWS_DEFAULT_REGION:-us-east-1}"
MODO="${1:-happy}"
case "$MODO" in happy|rollback) ;; *) echo "Uso: $0 happy|rollback" >&2; exit 1;; esac
NS=oficina
P_OS=5010; P_CAD=5011; P_EXE=5012; P_BIL=5013
URL_OS="http://localhost:$P_OS"; URL_CAD="http://localhost:$P_CAD"; URL_EXE="http://localhost:$P_EXE"; URL_BIL="http://localhost:$P_BIL"

if [ -t 1 ]; then Y=$'\033[1;33m'; G=$'\033[1;32m'; R=$'\033[1;31m'; C=$'\033[0;36m'; Z=$'\033[0m'; else Y=; G=; R=; C=; Z=; fi
N=0
step() { N=$((N+1)); printf '\n%s▶ %d. %s%s\n' "$Y" "$N" "$*" "$Z"; }
ok()   { printf '%s✅ %s%s\n' "$G" "$*" "$Z"; }
info() { printf '%s   %s%s\n' "$C" "$*" "$Z"; }
fail() { printf '%s❌ %s%s\n' "$R" "$*" "$Z" >&2; exit 1; }

# json_campo <campo>: primeiro valor string do campo, lendo stdin
json_campo() { grep -o '"'"$1"'"[[:space:]]*:[[:space:]]*"[^"]*"' | head -n 1 | sed 's/^[^:]*:[[:space:]]*"\(.*\)"$/\1/' || true; }

PIDS=()
limpar() { for p in "${PIDS[@]:-}"; do [ -n "$p" ] && { kill "$p" 2>/dev/null || taskkill /F /PID "$(ps -p "$p" 2>/dev/null | awk 'NR==2{print $4}')" >/dev/null 2>&1 || true; }; done; }
trap limpar EXIT
trap 'limpar; exit 130' INT TERM

command -v kubectl >/dev/null && command -v aws >/dev/null && command -v curl >/dev/null || fail "Faltam kubectl, aws ou curl no PATH."
aws sts get-caller-identity >/dev/null 2>&1 || fail "Sessão AWS inválida ou expirada. Atualize as credenciais do AWS Academy."

abrir() { # <nome-svc> <porta-local> <url>
  local svc="$1" port="$2" url="$3"
  if curl -s -o /dev/null --max-time 2 "$url/health/ready"; then fail "A porta $port já está respondendo (port-forward antigo?). Encerre-o e tente de novo."; fi
  kubectl port-forward "svc/$svc" "$port:80" -n "$NS" >/dev/null 2>&1 &
  PIDS+=("$!")
  local i; for i in $(seq 1 30); do
    [ "$(curl -s -o /dev/null -w '%{http_code}' --max-time 2 "$url/health/ready" || true)" = 200 ] && return 0; sleep 1
  done
  fail "$svc não ficou pronto em 30 s (kubectl get pods -n $NS)."
}

step "Preparando o acesso aos 4 serviços (port-forward)"
abrir oficina-cadastros-service "$P_CAD" "$URL_CAD"; ok "Cadastros  $URL_CAD"
abrir oficina-os-service        "$P_OS"  "$URL_OS";  ok "OS         $URL_OS"
abrir oficina-execucao-service  "$P_EXE" "$URL_EXE"; ok "Execução   $URL_EXE"
abrir oficina-billing-service   "$P_BIL" "$URL_BIL"; ok "Billing    $URL_BIL"
API_KEY=$(kubectl get secret oficina-execucao-secrets -n "$NS" -o jsonpath='{.data.InternalApi__ApiKey}' | base64 -d)
[ -n "$API_KEY" ] || fail "Não consegui ler a API key da Execução no cluster."

# req <METODO> <url> [corpo] [header-extra]: imprime o corpo; define HTTP_CODE (chame sem $(...) para preservar)
HTTP_CODE=
BODY=
NL=$'\n'
req() {
  local metodo="$1" url="$2" corpo="${3:-}" header="${4:-}" resp
  # -w acrescenta "\n<status>" ao fim da resposta: a última linha é o código HTTP
  local args=(-s -w "${NL}%{http_code}" -X "$metodo" -H "X-Internal-Api-Key: $API_KEY")
  [ -n "$corpo" ] && args+=(-H 'Content-Type: application/json' -d "$corpo")
  [ -n "$header" ] && args+=(-H "$header")
  resp=$(curl "${args[@]}" "$url" || true)
  HTTP_CODE="${resp##*"$NL"}"
  BODY="${resp%"$NL"*}"
  case "$HTTP_CODE" in [0-9][0-9][0-9]) ;; *) HTTP_CODE=000; BODY="";; esac
}
exigir_2xx() { case "$HTTP_CODE" in 2??) ;; *) fail "$1 falhou: HTTP $HTTP_CODE ${BODY:0:300}";; esac; }

# esperar_valor <rotulo> <esperado> <comando que imprime o valor>: poll de 1 s por até 60 s
esperar_valor() {
  local rot="$1" esp="$2"; shift 2; local v="" i
  for i in $(seq 1 90); do v="$("$@" 2>/dev/null || true)"; [ "$v" = "$esp" ] && { ok "$rot: $esp"; return 0; }; sleep 1; done
  fail "Timeout em '$rot': esperado '$esp', último valor '${v:-<vazio>}'"
}
status_os()  { curl -s "$URL_OS/ordens-servico/$OS_ID" | json_campo status; }
status_exe() { curl -s -H "X-Internal-Api-Key: $API_KEY" "$URL_EXE/execucao/$OS_ID" | json_campo status; }
status_orc() { curl -s "$URL_BIL/orcamentos/$OS_ID" | json_campo status; }

SUF=$(openssl rand -hex 4)
CPF=$(printf '%011d' "$(( (0x$(openssl rand -hex 4) % 90000000000) + 10000000000 ))")
LET=ABCDEFGHJKLMNPQRSTUVWXYZ
PLACA="${LET:$((RANDOM%24)):1}${LET:$((RANDOM%24)):1}${LET:$((RANDOM%24)):1}$((RANDOM%10))${LET:$((RANDOM%24)):1}$(printf '%02d' $((RANDOM%100)))"

step "Cadastros: criar cliente, veículo, peça e serviço"
req POST "$URL_CAD/clientes" "{\"nome\":\"Ana $SUF\",\"cpf\":\"$CPF\",\"email\":\"ana$SUF@teste.com\"}"; exigir_2xx "Criar cliente"
CLIENTE_ID=$(printf '%s' "$BODY" | json_campo id); [ -n "$CLIENTE_ID" ] || fail "Cliente sem id: $BODY"; ok "Cliente $CLIENTE_ID (CPF $CPF)"
req POST "$URL_CAD/veiculos" "{\"placa\":\"$PLACA\",\"modelo\":\"Onix\",\"marca\":\"GM\",\"ano\":2020,\"clienteId\":\"$CLIENTE_ID\"}"; exigir_2xx "Criar veículo"
ok "Veículo placa $PLACA"
req POST "$URL_CAD/pecas" "{\"codigo\":\"P-$SUF\",\"nome\":\"Pastilha de freio\",\"precoUnitario\":150.00,\"estoque\":10}"; exigir_2xx "Criar peça"
PECA_ID=$(printf '%s' "$BODY" | json_campo id); [ -n "$PECA_ID" ] || fail "Peça sem id: $BODY"; ok "Peça $PECA_ID (R\$ 150,00)"
req POST "$URL_CAD/servicos" "{\"nome\":\"Troca de pastilha $SUF\",\"precoUnitario\":200.00,\"tempoEstimadoHoras\":1.5}"; exigir_2xx "Criar serviço"
SERVICO_ID=$(printf '%s' "$BODY" | json_campo id); [ -n "$SERVICO_ID" ] || fail "Serviço sem id: $BODY"; ok "Serviço $SERVICO_ID (R\$ 200,00)"

step "OS Service: abrir a ordem de serviço (valida cliente e veículo no Cadastros)"
req POST "$URL_OS/ordens-servico" "{\"cpf\":\"$CPF\",\"placa\":\"$PLACA\",\"defeitoRelatado\":\"Freio rangendo\"}"; exigir_2xx "Abrir OS"
OS_ID=$(printf '%s' "$BODY" | json_campo id); [ -n "$OS_ID" ] || fail "OS sem id: $BODY"; ok "OS aberta: $OS_ID"
info "O OS Service publicou os.criada no SNS; o evento chega à fila da Execução."

step "Execução Service: a OS entra na fila (evento os.criada)"
esperar_valor "Execução" AguardandoDiagnostico status_exe

step "Execução Service: o mecânico registra o diagnóstico"
req POST "$URL_EXE/execucao/$OS_ID/diagnostico" "{\"pecas\":[{\"pecaId\":\"$PECA_ID\",\"quantidade\":2}],\"servicos\":[{\"servicoId\":\"$SERVICO_ID\",\"quantidade\":1}],\"tempoEstimadoHoras\":2,\"observacoes\":\"Troca das pastilhas dianteiras\"}"; exigir_2xx "Registrar diagnóstico"
ok "Diagnóstico registrado (a Execução publicou diagnostico.concluido)"
esperar_valor "OS Service" EmDiagnostico status_os

step "Billing Service: gera o orçamento (consulta preços no Cadastros)"
esperar_valor "OS Service" AguardandoAprovacao status_os
req GET "$URL_BIL/orcamentos/$OS_ID"; exigir_2xx "Consultar orçamento"
ORC_ID=$(printf '%s' "$BODY" | json_campo id); [ -n "$ORC_ID" ] || fail "Orçamento sem id: $BODY"
ok "Orçamento $ORC_ID (status $(printf '%s' "$BODY" | json_campo status))"
info "Esperado: 2 x R\$ 150,00 + 1 x R\$ 200,00 = R\$ 500,00"; info "Resposta: ${BODY:0:240}"

if [ "$MODO" = "rollback" ]; then
  step "Billing Service: o cliente RECUSA o orçamento (início da compensação)"
  req POST "$URL_BIL/orcamentos/$ORC_ID/recusar" '{"motivo":"valor_alto"}'; exigir_2xx "Recusar orçamento"
  ok "Orçamento recusado (Billing publicou orcamento.recusado)"
  step "OS Service compensa: OS cancelada e os.cancelada publicada"
  esperar_valor "OS Service" Cancelada status_os
  step "Execução Service e Billing reagem ao os.cancelada"
  esperar_valor "Execução" Cancelado status_exe
  FILA=$(curl -s -H "X-Internal-Api-Key: $API_KEY" "$URL_EXE/fila"); case "$FILA" in *"$OS_ID"*) fail "A OS cancelada ainda aparece na fila da Execução";; *) ok "OS fora da fila de execução";; esac
  step "Histórico da OS"; info "$(curl -s "$URL_OS/ordens-servico/$OS_ID/historico" | cut -c1-700)"
  printf '\n%s============================================================\n  🎉 Compensação da Saga entre os 4 serviços (rollback)\n============================================================%s\n' "$G" "$Z"
  exit 0
fi

step "Billing Service: o cliente aprova o orçamento"
req POST "$URL_BIL/orcamentos/$ORC_ID/aprovar"; exigir_2xx "Aprovar orçamento"
ok "Orçamento aprovado (Billing publicou orcamento.aprovado)"
esperar_valor "OS Service" Aprovada status_os
esperar_valor "Execução" AguardandoReparo status_exe

step "Execução Service: iniciar o reparo"
req POST "$URL_EXE/execucao/$OS_ID/iniciar-reparo"; exigir_2xx "Iniciar reparo"
esperar_valor "OS Service" EmReparo status_os

step "Execução Service: finalizar o reparo (dispara a cobrança no Billing)"
req POST "$URL_EXE/execucao/$OS_ID/finalizar" '{"tempoRealHoras":1.8}'; exigir_2xx "Finalizar"
esperar_valor "OS Service" AguardandoPagamento status_os

step "Billing Service: pagamento no Mercado Pago (modo fake) confirmado por webhook"
for i in $(seq 1 30); do req GET "$URL_BIL/pagamentos/$OS_ID"; [ "$HTTP_CODE" = 200 ] && break; sleep 1; done; exigir_2xx "Consultar pagamento"
PAG_ID=$(printf '%s' "$BODY" | json_campo id); [ -n "$PAG_ID" ] || fail "Pagamento sem id: $BODY"; ok "Pagamento $PAG_ID criado"
req POST "$URL_BIL/pagamentos/webhook/mercado-pago?data.id=fake-approved-$PAG_ID&type=payment" "" "X-Idempotency-Key: demo-$PAG_ID"; exigir_2xx "Webhook do Mercado Pago"
ok "Webhook processado (Billing publicou pagamento.confirmado)"
esperar_valor "OS Service" Concluida status_os

step "Histórico da OS (todas as transições)"
info "$(curl -s "$URL_OS/ordens-servico/$OS_ID/historico" | cut -c1-900)"
printf '\n%s============================================================\n  🎉 Saga completa: OS %s passou por Cadastros, OS, Execução e Billing\n============================================================%s\n' "$G" "$OS_ID" "$Z"
