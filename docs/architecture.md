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
| `Zava.SalesIntelligence.Agents` | Agent instructions, the contracts every stage passes, and the skills. Contracts live here on purpose - there is no `Shared` or `Core` catch-all. |
| `Zava.SalesIntelligence.Dataverse` | The `ServiceClient` factory, the five fixed read queries, and the four write tools. All Dataverse access is here. |
| `Zava.SalesIntelligence.Workflow` | The executors, the edges between them, the deterministic Ranker, and the approval gate. |
| `Zava.SalesIntelligence.Host` | Console host that builds and streams the workflow. Prints the graph with `graph`. |
| `Zava.SalesIntelligence.Companion` | Blazor Server companion that runs the workflow in process and renders it as three screens. |

Contracts are records, services are classes, namespaces are file scoped, and every async
signature carries a `CancellationToken`. See `CLAUDE.md` for the full set of conventions.

---

## The agents

Three agents, and each owns its tools and its identity. No agent reaches for another's
credential or tool, even to unblock something - the boundary is the point.

### Watcher - retrieves

Runs the five fixed queries in `WatcherQueries` (accounts, opportunities, activities,
notes, contacts) and assembles an `AccountContext` per account in `WatcherService`. The
queries are `QueryExpression` objects fixed in code with bounded parameters. The model
chooses *when* to retrieve, never *what* to ask - there is no free-form FetchXML built
from model output. Identity: the reader credential. Writes: none.

### Thinker - judges

Scores every open opportunity against BANT (Budget, Authority, Need, Timing) and assigns
an archetype, each axis citing a Dataverse record id. It has **no tools and no identity**.
This is deliberate and load-bearing: no tools means no attack surface, and it works only
on what the Watcher already retrieved. The judgement uses the `bant-scoring` skill and
the reasoning deployment.

Structured output is produced with `AgentRunOptions.ResponseFormat =
ChatResponseFormat.ForJsonSchema<T>()`, then the response text is deserialized with
tolerant options (a `JsonStringEnumConverter` plus an archetype converter that falls back
to `InsufficientSignal` rather than throwing). MAF's generic `RunAsync<T>` ignores custom
converters, which is why the deserialization is done by hand - see `StructuredAgent.cs`.

### Operator - acts

Turns an assessment into at most three prepared actions, each tied to a specific signal
the Thinker cited, written from the evidence rather than a template. It uses the
`next-best-action` skill and the fast deployment. Identity: the writer credential. Writes:
yes, but only after approval. Before it prepares an action that rests on a Missing axis it
can challenge the Thinker once - see the challenge edge below.

### Ranker - orders (not an agent)

The Ranker is plain code in `Ranker.cs`, not a prompt. Judgement is the model's job;
ordering is not. Keeping it deterministic is what lets the demo produce the same result
every run. Do not move ranking into a prompt.

---

## The workflow graph

`SalesWorkflow.Build` wires the stages with `WorkflowBuilder`:

```
Watcher -> Thinker -> Ranker -> Operator ->  challenge -> Actions -> approval -> Execute
                                         \-> (no challenge) ------/
```

Each executor is an `Executor<TInput>` that overrides `HandleAsync` and routes by the
concrete message record type. The edges are supersteps: the next stage starts only when
the previous one has finished for every account. That barrier is why the assessments can
be compared against each other rather than judged in isolation.

The Watcher and Thinker fan out concurrently, one unit of work per account, inside their
executors.

### The challenge edge

The Operator has two outgoing edges: one to the Challenge executor (the Thinker
re-examines a Missing axis) and one straight to action preparation. Both converge on the
Actions stage, so the round trip is capped at one. This is a conditional return path in
the graph, not a literal cycle and not a plain function call - modelling it as an edge is
what keeps the stage timing predictable.

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
tools and yields a `WriteReport`. Nothing reaches Dataverse without a decision.

The console host decides from the `ZAVA_APPROVE` policy (`all`, `none`, or a list of
numbers). The companion presents the same request as checkboxes and blocks on a
`TaskCompletionSource` until the seller clicks, then flows the decision back through the
same port.

---

## The write tools

Four tools in `WriterTools`, the only sanctioned writes, each mapping to one permission on
the writer role: create task, create appointment, update opportunity stage, update
estimated close date. Fixed shapes, bounded parameters, no delete anywhere. They run only
after the gate.

---

## Two event types, two code paths

The companion renders a dialogue from two kinds of event that must not share a code path
(`CompanionEvent`):

- `ProgressEvent` is emitted by the workflow and is deterministic.
- `AgentMessageEvent` comes from a model and varies between runs.

Keeping them separate is what lets the demo control pacing and reason about reliability.
In the companion the deterministic progress is driven from the executor lifecycle
(`ExecutorCompletedEvent`), including the live "X is reasoning..." indicator that fills the
gap between stages - it is derived from the lifecycle, not from model output, so it stays
on the progress path.

---

## Identity boundary

Identity is injected outside agent code. The host builds a reader credential and a writer
credential (`AppUserCredential`, which uses a `ClientSecretCredential` when the app-user
secrets are set and otherwise falls back to the interactive login), constructs a Dataverse
`ServiceClient` for each, and hands retrieval the reader and writes the writer. The
executors never see which credential they hold.

Provisioning the two application users and their least-privilege roles is a separate,
admin-gated step in `scripts/provision.ps1`. The acceptance for the boundary is that the
reader identity provably cannot write. See `docs/identity.md`.

---

## Determinism

The demo must produce the same archetypes every run. Two things guarantee it: the Ranker
is plain code, and the dataset is sharpened until any reasonable model finds the same
answer. When output is unstable the fix is in the dataset, not the prompt - the validation
gate is ten consecutive runs on the same data producing the same archetype for every
account, with every BANT axis citing a record id that actually exists in Dataverse.

---

## Companion

A Blazor Server app that runs the workflow in process through `WorkflowFactory` and renders
three screens only: the conversation thread, four archetype cards, and the evidence detail
panel. Evidence clicks open the internal panel, never Dynamics - the thesis is that the
seller does not open the CRM. The page is interactive per `@rendermode InteractiveServer`,
and the workflow is built off the render dispatcher so the thread streams as the run
progresses. Run it with `scripts/run-companion.sh`.
