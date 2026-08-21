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

# The seller whose pipeline the squad reads. A user id, not a secret. The Watcher runs under
# the reader app identity (which owns no accounts) and scopes the account read to this owner,
# so the agent keeps its own least-privilege identity - no impersonation, no escalation.
export ZAVA_SELLER_USER_ID="6c20a779-e08b-f111-8076-002248a0acb3"

# Agent identity secrets (git-ignored), written by scripts/provision.ps1: the Watcher reads
# under the reader app user, writes run under the writer app user. Without the file both fall
# back to the interactive az login.
if [ -f "$(dirname "$0")/zava.env" ]; then set -a; . "$(dirname "$0")/zava.env"; set +a; fi

cd "$(dirname "$0")/.."
exec dotnet run --project src/Zava.SalesIntelligence.Companion/Zava.SalesIntelligence.Companion.csproj
