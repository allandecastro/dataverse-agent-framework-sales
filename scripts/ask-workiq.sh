#!/usr/bin/env bash
# PPCC26 coda - the live Work IQ moment.
# Calls the Dataverse Ask API (POST /api/iq/v1.0/ask) against a semantic model,
# scoped to the signed-in user's identity, and pretty-prints the answer.
# Usage: ./scripts/ask-workiq.sh ["your question"] [Auto|QuickResponse|ThinkDeeper]
# Auth: sign in once first -> `npx -y @microsoft/dataverse auth create -env <url> --deviceCode`
set -euo pipefail

ENVURL="${ZAVA_DATAVERSE_URL:-https://dv-mafdemo-dev.crm4.dynamics.com}"
MODEL="${WORKIQ_MODEL:-SalesSpecificQnA}"
QUESTION="${1:-Which open opportunity has the highest revenue and when does it close?}"
MODE="${2:-Auto}"

# Prefer the globally installed CLI (instant); fall back to npx (first run downloads).
if command -v dataverse >/dev/null 2>&1; then DV=(dataverse); else DV=(npx -y @microsoft/dataverse); fi

# Build the request body safely (handles quotes in the question).
BODY=$(QUESTION="$QUESTION" MODEL="$MODEL" MODE="$MODE" python3 -c '
import json, os
print(json.dumps({"query": os.environ["QUESTION"],
                  "semanticModelName": os.environ["MODEL"],
                  "searchMode": os.environ["MODE"]}))')

echo ""
echo "  POST ${ENVURL}/api/iq/v1.0/ask    model=${MODEL}  mode=${MODE}"
echo "  \"${QUESTION}\""
echo ""

"${DV[@]}" api request --target dataverse --environment "$ENVURL" \
  --path "api/iq/v1.0/ask" -X POST --header "Content-Type:application/json" \
  --body "$BODY" 2>/dev/null | python3 -c '
import sys, json
d = json.load(sys.stdin)
s = (d.get("summary") or "").strip()
print("  SUMMARY")
for line in (s.splitlines() or ["(none)"]):
    print("    " + line)
rows = d.get("rawResult") or []
print("")
print("  ROWS ({})".format(d.get("totalResultCount", len(rows))))
for r in rows[:5]:
    name = r.get("name") or r.get("@primaryNameValue") or "(row)"
    val  = r.get("estimatedvalue_") or r.get("est_revenue_base_") or ""
    date = (r.get("estimatedclosedate_") or "")[:10]
    print("    - {} | {} | {}".format(name, val, date))
    links = r.get("@recordLinks") or []
    if links:
        print("      -> " + links[0])
cites = d.get("citationLinks") or []
if cites:
    print("")
    print("  CITATION  " + cites[0])
'
echo ""
