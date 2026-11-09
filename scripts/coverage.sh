#!/usr/bin/env bash
# Roda os testes unitários e de integração, junta a cobertura e confere o mínimo.
# Uso (na raiz do repositório):  bash scripts/coverage.sh
# Requer: dotnet tool install --global dotnet-reportgenerator-globaltool
set -euo pipefail

CONFIG="${CONFIG:-Release}"

rm -rf coverage
dotnet build -c "$CONFIG"

for projeto in Unit Integration; do
  dotnet test "tests/OficinaExecucao.Tests.${projeto}" --no-build -c "$CONFIG" \
    --settings coverlet.runsettings \
    --collect:"XPlat Code Coverage" \
    --results-directory "./coverage/${projeto}"
done

reportgenerator \
  -reports:"coverage/**/coverage.cobertura.xml" \
  -targetdir:coverage/report \
  -reporttypes:"Cobertura;TextSummary;MarkdownSummaryGithub;Html"

bash scripts/check-coverage.sh coverage/report/Cobertura.xml
