#!/usr/bin/env bash
# Launches the companion against the shared Zava environment. Endpoints and
# deployment names are configuration, not secrets, so they live here. Identity
# comes from `az login`: the reader and writer credentials fall back to the
# signed-in identity when the app-user client secrets are not set (see
# scripts/README.md). Run `az login` first.
set -euo pipefail

export ZAVA_DATAVERSE_URL="https://dv-mafdemo-dev.crm4.dynamics.com"
export ZAVA_OPENAI_ENDPOINT="https://aif-zava-salesintel.openai.azure.com/"
export ZAVA_OPENAI_DEPLOYMENT="gpt-5.4-mini"
export ZAVA_OPENAI_REASONING_DEPLOYMENT="gpt-5.6-sol"

# Identity comes from `az login` (interactive fallback). The provisioned reader/writer app
# users exist and are validated (see scripts/zava.env, git-ignored), but the Watcher filters
# accounts by ownerid = calling user, and the demo data is owned by the signed-in seller - so
# retrieval under the reader app user returns nothing until the data ownership / act-on-behalf
# question is decided. Set ZAVA_WRITER_* only, or source zava.env, once that is resolved.

cd "$(dirname "$0")/.."
exec dotnet run --project src/Zava.SalesIntelligence.Companion/Zava.SalesIntelligence.Companion.csproj
