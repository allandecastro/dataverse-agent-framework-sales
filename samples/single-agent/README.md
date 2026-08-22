# single-agent - the comparison

One agent, every read tool, answering the same question the squad answers:
*what should I focus on this week?*

This is act 0 of the talk. Run it, then run the squad, and the difference is the point.

## What it is

A single `AIAgent` on the fast deployment, handed the Watcher's reads as tools
(`get_accounts`, `get_open_opportunities`, `get_recent_activities`). It decides when to call
them and in what order, then answers in prose. There is no workflow graph, no superstep
barrier, no deterministic Ranker, no approval gate, and no identity separation.

It is read-only on purpose. The point is not that one agent cannot read Dataverse - it can,
and it does a decent job. The point is what is missing the moment the task grows past
reading.

## What it shows, next to the squad

| | Single agent | The squad |
|---|---|---|
| Order of work | whatever the model decides | a workflow graph, fixed |
| Comparing accounts | one at a time, in context | a superstep barrier - all assessed before ranking |
| Ranking | prose, different every run | deterministic code, same every run |
| Evidence | mentioned, not structured | BANT per axis, each citing a record id |
| Writes | none here - and nowhere to gate them | four tools behind an approval gate |
| Identity | the signed-in user | reader for retrieval, writer for writes, scoped |

The single agent gives a reasonable answer. It just gives a *different* reasonable answer
each run, with no place to stand for determinism, governance, or an identity boundary. The
harness is what buys you those - and building it by hand is what makes them visible.

## Run it

```bash
az login
export ZAVA_DATAVERSE_URL="https://dv-mafdemo-dev.crm4.dynamics.com"
export ZAVA_OPENAI_ENDPOINT="https://<your-resource>.openai.azure.com/"
export ZAVA_OPENAI_DEPLOYMENT="<fast-deployment>"
export ZAVA_SELLER_USER_ID="<seller systemuserid>"   # optional; else reads the signed-in user's accounts

dotnet run --project samples/single-agent
```
