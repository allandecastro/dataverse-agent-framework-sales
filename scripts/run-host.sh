#!/usr/bin/env bash
# Runs the console host - the same workflow as the companion, streamed to the terminal.
# Useful for the identity beat and for `graph` (prints the workflow as Mermaid).
#
# Endpoints and the seller id are configuration, not secrets. Identity comes from the
# git-ignored scripts/zava.env (reader/writer app users) when present, otherwise from the
# interactive az login. Run `az login` first. Pass `graph` to print the graph and exit.
#
# Approval is non-interactive here: ZAVA_APPROVE = none (default), all, or a comma-separated
# list of proposal numbers. Keep it none to rehearse without writing.
set -euo pipefail

export ZAVA_DATAVERSE_URL="https://dv-mafdemo-dev.crm4.dynamics.com"
export ZAVA_OPENAI_ENDPOINT="https://aif-zava-salesintel.openai.azure.com/"
# Both squad agents (Thinker and Operator) run on the reasoning deployment; the cheaper model
# is used only by the samples, which set ZAVA_OPENAI_DEPLOYMENT themselves.
export ZAVA_OPENAI_REASONING_DEPLOYMENT="gpt-5.6-sol"
export ZAVA_SELLER_USER_ID="6c20a779-e08b-f111-8076-002248a0acb3"
export ZAVA_APPROVE="${ZAVA_APPROVE:-none}"

if [ -f "$(dirname "$0")/zava.env" ]; then set -a; . "$(dirname "$0")/zava.env"; set +a; fi

cd "$(dirname "$0")/.."
exec dotnet run --project src/Zava.SalesIntelligence.Host/Zava.SalesIntelligence.Host.csproj -- "$@"
