#!/usr/bin/env bash
# Act 0 of the talk: the smallest thing that works. One agent, every read tool, real data.
# Run it live before the squad; run it twice to show the answer changes. The contrast is the point.
# Usage: ./scripts/run-single-agent.sh   (needs `az login` first)
set -euo pipefail
cd "$(dirname "$0")/.."

export ZAVA_DATAVERSE_URL="${ZAVA_DATAVERSE_URL:-https://dv-mafdemo-dev.crm4.dynamics.com}"
export ZAVA_OPENAI_ENDPOINT="${ZAVA_OPENAI_ENDPOINT:-https://aif-zava-salesintel.openai.azure.com/}"
# The sample uses one chat deployment. Point this at your fast deployment for the demo.
export ZAVA_OPENAI_DEPLOYMENT="${ZAVA_OPENAI_DEPLOYMENT:-gpt-5.6-sol}"
export ZAVA_SELLER_USER_ID="${ZAVA_SELLER_USER_ID:-6c20a779-e08b-f111-8076-002248a0acb3}"

dotnet run --project samples/single-agent
