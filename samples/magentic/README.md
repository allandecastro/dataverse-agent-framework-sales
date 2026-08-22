# magentic - the same squad, autonomous orchestration

The same job as the squad - retrieve, assess, act - but orchestrated by a Magentic manager
that plans and delegates on its own, instead of the fixed workflow graph in
`src/Zava.SalesIntelligence.Workflow`.

This is the other end of the spectrum, and the reason the demo builds the harness by hand.

## What it is

A `MagenticWorkflowBuilder` with a manager agent (reasoning deployment) and three
participants:

- **Retriever** - reads the pipeline with the Watcher's tools
- **Assessor** - judges opportunities against BANT
- **Strategist** - proposes next best actions

The manager writes a plan, picks who speaks next each round, tracks progress, and stops when
it decides the question is answered (`MagenticPlanCreatedEvent`,
`MagenticProgressLedgerUpdatedEvent`, and so on). Nothing places the order by hand.

## What it shows, next to the squad

| | Magentic | The squad (hand-built graph) |
|---|---|---|
| Order of work | the manager decides each round | fixed edges, a superstep barrier |
| Ranking | part of the conversation | deterministic code, same every run |
| Determinism | different path and answer each run | same archetypes every run |
| Where to gate a write | nowhere obvious - the manager is in charge | one approval `RequestPort` on one edge |
| Identity boundary | one context | reader for reads, writer for writes |

Magentic is flexible and genuinely capable. But for a demo that has to be identical every
run, cite real record ids, and gate every write behind approval, "the manager decides" is the
wrong default. Building the graph by hand is what makes order, determinism, and governance
places you can point at. This sample exists so that trade-off is concrete, not asserted.

It is read-only - no writes and no approval gate - by design.

## Run it

```bash
az login
export ZAVA_DATAVERSE_URL="https://dv-mafdemo-dev.crm4.dynamics.com"
export ZAVA_OPENAI_ENDPOINT="https://<your-resource>.openai.azure.com/"
export ZAVA_OPENAI_DEPLOYMENT="<fast-deployment>"
export ZAVA_OPENAI_REASONING_DEPLOYMENT="<reasoning-deployment>"
export ZAVA_SELLER_USER_ID="<seller systemuserid>"   # optional

dotnet run --project samples/magentic
```

Expect it to take longer and cost more than the squad: the manager runs many rounds of
model calls. That cost is part of the point.
