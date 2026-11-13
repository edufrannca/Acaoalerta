#!/usr/bin/env bash
# Gera AlertaNearby.xcodeproj. Uso: DEVELOPMENT_TEAM=<seu team id> BUNDLE_ID=br.alertaemacao.nearby.<seunome> ./gerar-projeto.sh
set -euo pipefail
cd "$(dirname "$0")"
command -v xcodegen >/dev/null || { echo "Instale o XcodeGen: brew install xcodegen"; exit 1; }
export DEVELOPMENT_TEAM="${DEVELOPMENT_TEAM:-}"
export BUNDLE_ID="${BUNDLE_ID:-br.alertaemacao.nearby.demo}"
xcodegen generate
echo "Projeto gerado. Abra AlertaNearby.xcodeproj, escolha seu iPhone e pressione Run (⌘R)."
echo "Bundle id: $BUNDLE_ID · Team: ${DEVELOPMENT_TEAM:-(selecione em Signing & Capabilities)}"
