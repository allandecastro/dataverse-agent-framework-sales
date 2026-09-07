# Architecture

How the pieces fit, and which Microsoft Agent Framework primitive each one is meant to
make legible. If you are reading the code after the talk, start here.

The guiding rule for the whole repo: the framework is the subject, not the plumbing.
Where a shortcut and a framework primitive both work, the code takes the primitive, so
the machinery a platform would normally hide stays visible.

---

## The question

A seller asks one thing: *what should I focus on this week?* Everything below exists to
answer it from the live pipeline in Dataverse, with cited evidence, and to prepare the
follow-up actions without anyone opening Dynamics.

---

## Projects

| Project | Responsibility |
|---|---|
| `Zava.SalesIntelligence.Agents` | Agent instructions, the contracts every stage passes, and the skills (`bant-scoring`, `meddpicc-scoring`, `next-best-action`). Contracts live here on purpose - there is no `Shared` or `Core` catch-all. |
| `Zava.SalesIntelligence.Dataverse` | The `ServiceClient` factory, the five fixed read queries, and the four write tools. All Dataverse access is here. |
| `Zava.SalesIntelligence.Workflow` | The executors, the edges between them, the deterministic Ranker, the approval gate, the identity middleware, and `SquadFactory` - the one place the squad is wired from configuration. |
| `Zava.SalesIntelligence.Host` | Console host that builds and streams the workflow. Prints the graph with `graph`. |
| `Zava.SalesIntelligence.Companion` | Blazor Server companion that runs the workflow in process and renders it as three screens, with a grounded spokesperson for questions and voice. |

Contracts are records, services are classes, namespaces are file scoped, and every async
signature carries a `CancellationToken`. See `CLAUDE.md` for the full set of conventions.

---

## The agents

Three agents, and each owns its tools and its identity. No agent reaches for another's
credential or tool, even to unblock something - the boundary is the point. The AE persona
adds a fourth, the Scorer, built to the same discipline as the Thinker.

### Watcher - retrieves

Runs the five fixed queries in `WatcherQueries` (accounts, opportunities, activities,
notes, contacts) and assembles an `AccountContext` per account in `WatcherService`. The
queries are `QueryExpression` objects fixed in code with bounded parameters and an explicit
order, so the input to the Ranker is the same every run. The model chooses *when* to
retrieve, never *what* to ask - there is no free-form FetchXML built from model output.
Identity: the reader credential, scoped to the seller's accounts by owner. Writes: none.

### Thinker - judges

Scores every open opportunity against BANT (Budget, Authority, Need, Timing) and assigns
an archetype, each axis citing a Dataverse record id. It has **no tools and no identity**.
This is deliberate and load-bearing: no tools means no attack surface, and it works only
on what the Watcher already retrieved. The judgement uses the `bant-scoring` skill and
the reasoning deployment.

Structured output is produced with `AgentRunOptions.ResponseFormat =
ChatResponseFormat.ForJsonSchema<T>()`, then the response text is deserialized with
tolerant options (a `JsonStringEnumConverter` plus an archetype converter that falls back
rather than throwing; `Confidence` governs). MAF's generic `RunAsync<T>` ignores custom
converters, which is why the deserialization is done by hand - see `StructuredAgent.cs`. A
malformed or empty completion is retried once; if it fails again, that one account degrades
(dropped as `InsufficientSignal`, or kept with its first assessment, or given no actions)
instead of faulting the run.

### Operator - acts

Turns an assessment into at most three prepared actions, each tied to a specific signal
the Thinker cited, written from the evidence rather than a template. It uses the
`next-best-action` skill and the reasoning deployment (the cheaper model under-produced
actions, so both squad agents run on the reasoning model - the cheaper one is used only by
the samples). The Operator agent holds no Dataverse identity; the approved writes run later
under the writer credential in the execute stage. Before it prepares an action that rests on
a Missing axis it can challenge the Thinker once - see the challenge edge below.

### Scorer - the AE persona's MEDDPICC lens

The companion has a persona toggle. **SDR** is the fast BANT triage described above. **AE**
(account executive) adds a second scorer that reads the same `AccountContext` and returns a
`MeddpiccScorecard`: the eight MEDDPICC elements (Metrics, Economic buyer, Decision
criteria, Decision process, Paper process, Identify pain, Champion, Competition), each with
status, reasoning and cited evidence, plus the archetype they imply and a one-line rationale.
In AE that archetype places the card on the board and the evidence panel shows the eight
elements; the BANT triage still runs underneath and still drives the Operator's actions. The
scorer uses the `meddpicc-scoring` skill, has no tools and no identity, and does not exist in
the SDR run, so the SDR determinism gate is untouched. The host reads the persona from
`ZAVA_PERSONA`; the companion passes the toggle.

### Ranker - orders (not an agent)

The Ranker is plain code in `Ranker.cs`, not a prompt. Judgement is the model's job;
ordering is not. Keeping it deterministic is what lets the demo produce the same result
every run. Do not move ranking into a prompt.

---

## The workflow graph

`SalesWorkflow.Build` wires the stages with `WorkflowBuilder`; `docs/workflow-graph.md` has
the Mermaid the built graph prints:

```
Watcher -> Thinker -> Ranker -> Operator ->  challenge -> Actions -> approval -> Execute
                                         \-> (no challenge) ------/       ^          |
                                                                          \----------/
```

Each executor is an `Executor<TInput>` that overrides `HandleAsync` and routes by the
concrete message record type. The edges are supersteps: the next stage starts only when
the previous one has finished for every account. That barrier is why the assessments can
be compared against each other rather than judged in isolation.

The Watcher and Thinker fan out concurrently, one unit of work per account, inside their
executors.

### The challenge edge

The Operator has two outgoing edges: one to the Challenge executor (the Thinker
re-examines a Missing axis) and one straight to action preparation. The edge is conditional
by message type, not by a predicate: the Operator sends either a `ChallengeBatch` or a
`ChallengedBatch`, and the workflow routes on the type. Both converge on the Actions stage,
so the round trip is capped at one - a conditional return path in the graph, not a literal
cycle and not a plain function call, which is what keeps the stage timing predictable.

### Pacing

`SalesWorkflow.Build` takes a `TimeSpan` pacing delay applied in front of each stage, set
from `ZAVA_STEP_DELAY_MS`. Pacing is server side: the workflow decides when to emit, the
client renders what it receives. There is no client-side queuing or animation.

---

## The approval gate

Write tools are approval required, and the gate is a framework primitive, not an `if` in
application code. The Actions stage sends an `ApprovalRequest` to a
`RequestPort.Create<ApprovalRequest, ApprovalDecision>("approval")`. The run pauses and
emits a `RequestInfoEvent`; the host reads the payload with
`requestInfo.Request.Data.As<ApprovalRequest>()`, decides, and replies with
`run.SendResponseAsync(requestInfo.Request.CreateResponse(new ApprovalDecision(...)))`.
Only the approved actions route on to the Execute executor, which calls the four write
tools. Nothing reaches Dataverse without a decision.

The gate is iterative. `ApprovalDecision` carries the approved proposals and, optionally,
`Next`: an `ApprovalRequest` holding the proposals the seller kept for another round. The
Execute executor writes the approved batch, and when `Next` is set it sends that request back
to the port (the `execute --> approval` edge) instead of finishing, so approving some actions
never discards the rest. It accumulates every write across rounds and yields the final
`WriteReport` only when the seller is done. Each write also raises an `AgentWriteEvent` the
moment it lands, so the companion can show the record and its "Open record" link live.

The console host decides from the `ZAVA_APPROVE` policy (`all`, `none`, or a list of
numbers). The companion presents the same request as checkboxes and blocks on a
`TaskCompletionSource` until the seller decides - by button, by typed question, or by voice
("approve the Litware appointment") - then flows the decision back through the same port.

---

## The write tools

Four tools in `WriterTools`, the only sanctioned writes, each mapping to a permission on
the writer role: create task, create appointment, update opportunity stage, update
estimated close date. Fixed shapes, bounded parameters, no delete anywhere. They run only
after the gate. The model decides *when* an action is due; code bounds *what* is written: a
stage update only ever walks an over-stated deal back to Qualify, and a close-date update
uses the Operator's evidence-grounded suggested date. Appointments carry the seller as
organizer and required attendee and invite the client contact the action names; tasks stay
internal. Stage and close-date writes mutate the fields the Watcher reads, so
`scripts/reset-demo.ps1` restores the dataset before each run.

---

## Two event types, two code paths

The companion renders a dialogue from two kinds of workflow event that must not share a
code path (`TraceEvents.cs`):

- `AgentProgressEvent` is emitted by the executors and is deterministic: a query and its
  count, a stage starting, the Ranker's order. Same input, same line, every run.
- `AgentThoughtEvent` carries a model's output: an archetype call with its rationale, a
  re-examination, a proposed action. It varies between runs - it is the intelligence.

(`AgentWriteEvent`, a record that just landed, is a third, deterministic kind on the progress
path.) Keeping them separate is what lets the demo control pacing and reason about
reliability. In the companion the stage thread is driven from the executor lifecycle
(`ExecutorCompletedEvent`), including the live "X is reasoning..." indicator that fills the
gap between stages - derived from the lifecycle, not from model output, so it stays on the
progress path. The activity log drawer shows both kinds, labelled.

---

## Identity boundary

Identity lives at the Dataverse client layer, decided in `SquadFactory` outside agent code.
It builds a reader credential and a writer credential (`AppUserCredential`, which uses a
`ClientSecretCredential` when the app-user secrets are set and otherwise falls back to the
interactive login, and logs which mode is active), constructs a Dataverse `ServiceClient` for
each, and hands retrieval the reader and writes the writer. The executors never see which
credential they hold, and the LLM agents hold none.

`AgentIdentity.WithAuthority` is a MAF run middleware (`AIAgentBuilder.Use`) wrapped around
each LLM agent. It adds no security - the roles do that - but it stamps the authority the
agent acts under on every call, so "the Thinker has no tools and no identity" is visible in
the log rather than asserted in a comment. It is the framework's middleware primitive applied
to a cross-cutting concern.

Provisioning the two application users and their least-privilege roles is a separate,
admin-gated step in `scripts/provision.ps1`. The acceptance for the boundary is that the
reader identity provably cannot write. See `docs/identity.md`.

---

## Determinism

The demo must produce the same archetypes every run. Two things guarantee it: the Ranker
is plain code, and the dataset is sharpened until any reasonable model finds the same
answer. When output is unstable the fix is in the dataset, not the prompt - the validation
gate is ten consecutive runs on the same data producing the same archetype for every
account, with every BANT axis citing a record id that actually exists in Dataverse. The gate
is stated for the SDR (BANT) persona; the AE board is a model judgement over eight elements
and is checked the same way. Run `scripts/reset-demo.ps1` before each run, because approved
writes edit the source data.

---

## Companion

A Blazor Server app that runs the workflow in process (`SquadFactory` via `WorkflowFactory`)
and renders three screens only: the squad thread, four archetype cards, and the evidence
detail panel. Evidence clicks open the internal panel, never Dynamics - the thesis is that
the seller does not open the CRM. The page is interactive per `@rendermode InteractiveServer`,
and the workflow is built off the render dispatcher so the thread streams as the run
progresses.

A right sidebar holds the spokesperson: `PipelineConversation`, a grounded agent whose only
knowledge is this run's analysis, with no tools and no Dataverse access. It answers typed or
spoken questions and turns "approve X" into a decision on the existing gate - see
`docs/voice.md`. The persona toggle (SDR / AE) applies to the next run; a refresh button
re-runs the squad. Run it with `scripts/run-companion.sh`.
