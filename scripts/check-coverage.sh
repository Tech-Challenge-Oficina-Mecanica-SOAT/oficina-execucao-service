#!/usr/bin/env bash
# Uso: bash scripts/check-coverage.sh <Cobertura.xml>
# Lê o total de linhas do relatório já mesclado e falha se ficar abaixo de COVERAGE_THRESHOLD (padrão 80).
set -euo pipefail

XML="${1:?Informe o caminho do Cobertura.xml}"
THRESHOLD="${COVERAGE_THRESHOLD:-80}"

if [[ ! -f "$XML" ]]; then
  echo "ERRO: relatório de cobertura não encontrado: $XML"
  exit 1
fi

cobertas=$(grep -o 'lines-covered="[0-9]*"' "$XML" | head -1 | grep -o '[0-9]*' || true)
validas=$(grep -o 'lines-valid="[0-9]*"' "$XML" | head -1 | grep -o '[0-9]*' || true)

if [[ -z "${validas:-}" || "$validas" -eq 0 ]]; then
  echo "ERRO: nenhuma linha instrumentada. Confira o Include do coverlet.runsettings."
  exit 1
fi

pct=$(awk -v c="$cobertas" -v v="$validas" 'BEGIN { printf "%.1f", 100 * c / v }')
linha="Cobertura de linhas (unitários + integração): ${pct}% (${cobertas}/${validas}) — mínimo ${THRESHOLD}%"
echo "$linha"

if [[ -n "${GITHUB_STEP_SUMMARY:-}" ]]; then
  { echo "### Cobertura"; echo "$linha"; } >> "$GITHUB_STEP_SUMMARY"
  resumo="$(dirname "$XML")/SummaryGithub.md"
  if [[ -f "$resumo" ]]; then { echo; cat "$resumo"; } >> "$GITHUB_STEP_SUMMARY"; fi
fi

if awk -v p="$pct" -v t="$THRESHOLD" 'BEGIN { exit !(p < t) }'; then
  echo "ERRO: cobertura ${pct}% abaixo do mínimo de ${THRESHOLD}%."
  exit 1
fi
