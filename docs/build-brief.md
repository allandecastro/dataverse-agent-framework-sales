# Zava Sales Intelligence - Build Brief

Working brief for building the PPCC26 demo. Written to be handed to Claude Code.

Ownership tags:
- **[ALLAN]** - authentication, admin approval, judgement calls
- **[CC]** - Claude Code executes
- **[BOTH]** - Claude Code drafts, Allan validates

---

## 0. Objective - read this before anything else

### The session
PPCC26, 50 minutes. Title: *AI Agents Meet Dataverse: Building Intelligent Automations with Microsoft Agent Framework*.

Audience: Power Platform and Dynamics 365 architects and developers, mostly Copilot Studio users.

### Positioning
Copilot Studio does not remove the need to understand what an agent is. It demands it.

In Copilot Studio you choose your harness. In Microsoft Agent Framework you build it.

The session shows the machinery that Copilot Studio runs on your behalf, so the audience is better at both.

### Non-negotiables
1. **MAF is the protagonist, not the plumbing.** Every demo second must make a framework capability visible on screen. If a moment does not show a MAF brick, cut it.
2. **The abstract is the contract.** It promises: retrieves accounts, opportunities and activities; analyses customer data; recommends next best actions; exposes Dataverse as tools; handles authentication securely; reliable prompts; powered by Azure OpenAI; live demo; working blueprint. All of it must be true.
3. **The repo is public and will be read as a reference.** Clarity over cleverness. No abstraction that was not asked for.
4. **Plain hyphens everywhere.** Never em dashes or en dashes, in code, docs or output.
5. **All deliverables in English.**

### What the audience must feel
- The seller never opens Dynamics
- The agents visibly collaborate, including one challenging another
- The recommendations cite proof, not scores
- Writing to Dataverse is bounded by identity and gated by approval

---

## 1. What each demo beat must prove

| Session block | Artifact | MAF capability made visible |
|---|---|---|
| 0 - What is a harness | slides only | - |
| 1 - MAF intro | architecture slide, `samples/single-agent` | agent, thread, tools, hosting |
| 2 - Orchestration patterns | `samples/magentic` code diff, not executed | the who-decides axis |
| 3 - Tools | agent cards, tool signatures, SKILL.md | function tools vs MCP vs skills, determinism |
| 4 - The squad | companion + DevUI | fan-out, superstep barrier, challenge edge, approval gate |
| 5 - Identity | identity middleware, two app users | credential injected outside agent code |
| 6 - Headless | companion + voice on one AG-UI session | AG-UI as the door, not the UI |

---

## 2. Prerequisites

Everything here is scriptable once Allan is authenticated. Output should be `scripts/provision.ps1`, idempotent and re-runnable, committed to the repo.

### 2.1 Authentication
- **[ALLAN]** `az login` against the target tenant
- **[ALLAN]** `pac auth create` against the target environment
- **[ALLAN]** Create the Dataverse sandbox environment if tenant policy requires admin approval

### 2.2 Provisioning script
- **[CC]** Entra app registrations `zava-watcher`, `zava-operator`, `zava-companion` via `az ad app create`, secrets written to user secrets, never to the repo
- **[CC]** Dataverse application users bound to the first two, via `pac admin` or the Web API
- **[CC]** Two custom security roles, authored as solution XML so they are versioned:
  - `Zava Agent Reader` - read on account, opportunity, activitypointer, annotation, contact. Nothing else.
  - `Zava Agent Writer` - create on task and appointment, write on opportunity limited to stage and estimated close date. No delete anywhere.
- **[CC]** Azure OpenAI resource and two deployments via `az cognitiveservices`: one cheap model, one reasoning model
- **[CC]** Two test users with different account ownership, for the identity switch beat
- **[ALLAN]** Validate role scopes before running anything against data

### 2.3 Judgement calls that stay with Allan
- Naming and scoping of the security roles
- Entra Agent ID preview eligibility - if the tenant does not qualify, that beat stays a slide and blocks nothing
- Realism review of the dataset note bodies

### 2.4 Spikes - decision, not execution
- **[CC]** Clone and run `microsoft/Generative-AI-for-beginners-dotnet`, `samples/AgentFx/AgentFx-AIWebChatApp-AG-UI`
- **[ALLAN]** Judge whether the Blazor AG-UI client is rich enough to intercept the event stream and render custom components. If not, fall back to CopilotKit and accept React in the repo.
- **[CC]** Prove the workflow can delay between supersteps. The front-end simplicity depends on server-side pacing.

---

## 3. Repo skeleton

```
dataverse-agent-framework-sales/
├── src/
│   ├── Zava.SalesIntelligence.Agents/        agents, instructions, contracts, skills
│   ├── Zava.SalesIntelligence.Dataverse/     client, tools, auth
│   ├── Zava.SalesIntelligence.Workflow/      executors, edges, ranker, approval
│   ├── Zava.SalesIntelligence.Host/          ASP.NET Core, AG-UI, DevUI
│   ├── Zava.SalesIntelligence.Companion/     Blazor client
│   └── Zava.SalesIntelligence.Voice/         Blazor voice shell
├── samples/
│   ├── single-agent/                         act 0 comparison
│   └── magentic/                             bloc 2 code diff
├── scripts/                                  provisioning and seed
├── data/                                     dataset definition
├── docs/                                     diagrams, session mapping
└── README.md
```

Contracts live in `Agents`, not in a shared or core project. A catch-all project is the first sign of a rushed public repo.

---

## 4. Contracts - write these first

```csharp
public record OpportunityAssessment(
    string AccountName,
    string OpportunityName,
    decimal Amount,
    DateOnly? CloseDate,
    string CrmStage,
    BantAssessment Bant,
    Archetype Archetype,
    string Rationale,          // two sentences max, written to be read aloud
    Confidence Confidence);

public record BantAssessment(
    BantCriterion Budget,
    BantCriterion Authority,
    BantCriterion Need,
    BantCriterion Timing)
{
    public int Score => new[] { Budget, Authority, Need, Timing }
        .Count(c => c.Status == BantStatus.Confirmed);
}

public record BantCriterion(
    BantStatus Status,          // Confirmed, Partial, Missing
    string Reasoning,
    IReadOnlyList<Evidence> Evidence);

public record Evidence(
    string EntityLogicalName,   // annotation, appointment, phonecall, email
    Guid RecordId,
    DateOnly Date,
    string Excerpt);            // 20 words max, verbatim from source

public enum Archetype { Reconnect, AtRisk, ReadyToClose, Slipping }
public enum Confidence { High, Medium, InsufficientSignal }

public record PreparedAction(
    ActionType Type,            // Task, Appointment, StageUpdate, CloseDateUpdate
    string AccountName,
    Guid TargetRecordId,
    string Title,
    string Body,                // written from the evidence, never generic
    DateOnly? DueDate,
    string TriggeringSignal,
    bool RequiresApproval);

public abstract record CompanionEvent(string AgentName, DateTimeOffset At);

public record ProgressEvent(string AgentName, DateTimeOffset At,
    string Message, int? Current, int? Total) : CompanionEvent(AgentName, At);

public record AgentMessageEvent(string AgentName, DateTimeOffset At,
    string Text, IReadOnlyList<Evidence>? Citations) : CompanionEvent(AgentName, At);

public record AssessmentReadyEvent(string AgentName, DateTimeOffset At,
    OpportunityAssessment Assessment) : CompanionEvent(AgentName, At);

public record ApprovalRequestEvent(string AgentName, DateTimeOffset At,
    PreparedAction Action, string RequestId) : CompanionEvent(AgentName, At);
```

**Critical distinction to preserve.** `ProgressEvent` is emitted by the workflow and is deterministic. `AgentMessageEvent` comes from a model and varies. The companion renders both as dialogue, but they are not equally reliable and must not share a code path.

---

## 5. Agents

### Watcher - retrieves

Instructions:
```
You retrieve Dataverse data. You do not judge, prioritise or conclude.
If a query returns nothing, say so. Never fill a gap with an assumption.
Do not paraphrase note content. Return it as written.
Always preserve record ids in your output.
```

Tools - fixed FetchXML in code, bounded parameters, the model chooses when to call, never what to ask:

| Tool | Parameters | Returns |
|---|---|---|
| `get_accounts` | ownerId, top (max 25) | accounts owned by the seller |
| `get_open_opportunities` | accountId | open opportunities, stage, amount, close date |
| `get_activities` | accountId, sinceDays (max 180) | calls, emails, appointments with dates and subjects |
| `get_notes` | regardingId | annotation bodies |
| `get_contacts` | accountId | contacts, job titles, active state |

Identity: `Zava Agent Reader`. Skills: none. Writes: no. Execution: concurrent, one per account.

If tool selection becomes erratic, merge `get_activities` and `get_notes` - they serve the same need.

### Thinker - judges

Instructions:
```
You assess a single sales opportunity against the BANT framework.

You have no tools. You work only from the account context you are given.

## Rules

Every judgement must rest on evidence. Evidence is a dated Dataverse
record - an activity, a note, a contact, a field value. An impression,
an inference, or an absence of contradiction is not evidence.

If an axis has no supporting evidence, its status is Missing. Never
Partial. Partial means you found evidence that points both ways, or
evidence that covers the axis incompletely.

If fewer than two axes are documented, return InsufficientSignal and
stop. Do not assign an archetype. Refusing to conclude is a valid and
expected outcome.

You may contradict the CRM. If the recorded stage is not supported by
the evidence, say so plainly and explain which evidence is missing.

Never invent a record id, a date, or a quotation. If you cannot cite
it, you did not find it.

## Output

Return the OpportunityAssessment schema. Nothing else - no preamble,
no markdown, no commentary outside the schema.

Rationale is at most two sentences and is written to be read aloud.
Lead with the finding, not the method.

Excerpt is at most 20 words, taken verbatim from the source record.
```

Skill: `bant-scoring` (see section 6). Tools: none. Identity: none. Writes: no. Model: the reasoning deployment. Execution: concurrent, one per opportunity.

### Operator - acts

Instructions:
```
You turn an assessment into prepared actions.

Every action is tied to a specific signal identified by the Thinker.
The body is written from the evidence, never from a template.
Three actions maximum.

If the assessment is InsufficientSignal, propose nothing.

Before preparing an action that rests on a Missing axis, call
challenge_assessment and ask the Thinker which evidence ruled it out.
Call it at most once.
```

Tools:

| Tool | Approval |
|---|---|
| `challenge_assessment` | no - routes back through the workflow edge |
| `create_task` | yes |
| `create_appointment` | yes |
| `update_opportunity_stage` | yes |
| `update_close_date` | yes |

Identity: `Zava Agent Writer`. Skill: `next-best-action`. Writes: yes, after approval.

`challenge_assessment` is not a plain function call. It is a conditional return edge in the graph, capped at one round trip so demo timing stays predictable.

---

## 6. bant-scoring skill

`src/Zava.SalesIntelligence.Agents/skills/bant-scoring/SKILL.md`

```markdown
---
name: bant-scoring
description: Assess a sales opportunity against Budget, Authority, Need and Timing using Dataverse evidence, and classify it into an action archetype. Use when evaluating pipeline health, qualification quality, or deal risk.
license: MIT
metadata:
  author: zava-sales-intelligence
  version: "1.0"
---

# BANT scoring

## The four axes

**Budget** - can they pay, and is the money allocated?
Confirmed: an amount is validated in writing by the customer, or a
budget holder is named against a figure.
Partial: an amount is discussed but not validated, or a range only.
Missing: no record mentions money on the customer side.

**Authority** - is the person who signs identified and engaged?
Confirmed: a decision maker is a contact on the account and appears in
at least one activity.
Partial: a decision maker is named in a note but never met.
Missing: all known contacts are operational, or no buying process is
described.

**Need** - is the problem documented in the customer's own terms?
Confirmed: a note or activity records a specific pain, a metric, or a
consequence of inaction.
Partial: a generic interest is recorded without specifics.
Missing: nothing describes what the customer is trying to solve.

**Timing** - is there a date the customer owns?
Confirmed: the customer states a deadline, an event, or a committee
date.
Partial: a timeframe exists but only in the seller's words.
Missing: the close date is the only date, and nothing supports it.

## Stage contradiction

Compare the CRM stage with the evidence, not with the amount.

A stage at or beyond Proposal with a BANT score of 2 or less is a
contradiction. Report it in the rationale. This is the single most
useful thing you can tell a seller.

## Archetypes

Assign exactly one, in this order of precedence.

**Slipping** - the deal is being lost.
A competitor is named in any record in the last 60 days, OR the close
date has moved more than once, OR a previously engaged decision maker
has no activity in 30 days.

**AtRisk** - the deal is not qualified enough for its stage.
Stage is at or beyond Proposal AND BANT score is 2 or less.

**Reconnect** - the deal is going quiet.
BANT score is 3 or more AND no activity in the last 30 days.

**ReadyToClose** - the deal is ripe.
BANT score is 4, OR score is 3 with Timing confirmed, AND activity
volume in the last 30 days is higher than the previous 30.

If none apply, return InsufficientSignal.

## Confidence

High - three or more axes carry evidence from distinct records.
Medium - two axes carry evidence, or several cite the same record.
InsufficientSignal - fewer than two axes documented.

## Worked examples

See references/examples.md for four assessments, including one that
correctly refuses to conclude.
```

The precedence order and the numeric thresholds are deliberate. The model judges what is hard - does this note document a need - and applies what is mechanical. Without fixed precedence, an account that is both Slipping and AtRisk will be classified differently between runs.

`references/examples.md` needs four worked assessments. The fourth must correctly refuse to conclude, or the model will not believe that option is real. This file does more for demo stability than the instructions do.

---

## 7. Build phases

### Phase 1 - One agent reads Dataverse
- **[CC]** Dataverse client with app-user auth
- **[CC]** `get_accounts` as a fixed-query function tool
- **[CC]** One `ChatClientAgent` with that tool, console host

**Acceptance:** console answers "how many accounts do I have" against real data.

### Phase 2 - Three agents, called by hand
- **[CC]** All five Watcher tools, `AccountContext` output preserving record ids
- **[CC]** `bant-scoring` SKILL.md plus `references/examples.md`
- **[CC]** Thinker with structured output, no tools
- **[CC]** `next-best-action` SKILL.md and Operator
- **[BOTH]** Iterate until output is stable

**Acceptance - this is the gate for the whole demo.** Ten consecutive runs on the same data produce the same archetype for every account, and every BANT axis cites a real record id that exists. Do not proceed until this passes. If output wavers, sharpen the dataset, not the prompt.

### Phase 3 - The workflow
- **[CC]** `WorkflowBuilder` graph: Watcher fan-out concurrent, barrier, Thinker fan-out, barrier, Ranker, Operator
- **[CC]** `Ranker` as a plain code executor - sorts on BANT score, then amount, then days to close, then days silent; drops `InsufficientSignal`
- **[CC]** Conditional challenge edge, capped at one round trip
- **[CC]** DevUI wiring
- **[CC]** Configurable delay between supersteps for stage pacing

**Acceptance:** the graph renders in DevUI, one run produces a ranked list and prepared actions, and the same dataset produces the same order every time.

### Phase 4 - Approval and write-back
- **[CC]** Four write tools, fixed parameters, approval-required
- **[CC]** Approval gate wired to the workflow pause
- **[CC]** Identity middleware injecting credentials per agent, outside agent code
- **[ALLAN]** Verify records in Dynamics and check attribution

**Acceptance:** approving in console writes to Dataverse under the writer identity, and the reader identity provably cannot write.

### Phase 5 - Companion
- **[CC]** AG-UI endpoint, shared session id across clients
- **[CC]** Blazor client, three screens only: conversation thread, four archetype cards, evidence detail panel
- **[CC]** Evidence click opens the internal panel, never Dynamics
- **[BOTH]** Visual pass - readable from the back of a ballroom, no animation, no branding, no responsive

**Acceptance:** the full run is watchable end to end with no console visible.

### Phase 6 - Voice - optional, first to be cut
- **[CC]** Second Blazor client, speech in and out, same AG-UI session, no second orchestrator
- **[CC]** Pulsing indicator, nothing else
- **[ALLAN]** Rehearse the window switch with a headset

Design in `docs/voice.md`. The principle: the voice is an interface over the one run, not
a second brain - its intents only read what the run produced or push a decision into the
existing approval gate, so the squad stays the only decision-maker. Two-way interaction
(ask, drill into evidence, approve by voice) comes from that, not from a second
orchestrator. Needs a realtime speech component (flag the dependency first). It only makes
sense once the run, the identity boundary, and the session samples are locked - so it
comes after phases 4 and 7, which is why it is last and first to be cut.

### Phase 7 - Session assets
- **[CC]** `samples/single-agent` and `samples/magentic`
- **[CC]** README mapping session blocks to folders
- **[CC]** Mermaid workflow diagram in the README
- **[ALLAN]** Slides, recordings, rehearsals

---

## 8. Dataset

Ten accounts. Two carry the inversion, eight are neutral filler.

**At-risk account** - large amount, close date within two weeks, stage at Proposal or beyond, economic buyer silent for six weeks, a note recording a budget committee pushed to next quarter, a competitor named in a meeting note, one key contact deactivated.

**Expansion account** - small amount, early stage, activity volume tripled over thirty days, three new contacts added, a note mentioning two additional sites, a senior title entering the thread.

Design rule: signals must be lopsided enough that any reasonable model finds the inversion.

- **[CC]** Idempotent seed script in `scripts/`
- **[ALLAN]** Review note bodies for realism - a Dynamics audience will judge this

---

## 9. Milestones

| When | What | Notes |
|---|---|---|
| Mid August | Prerequisites, provisioning script, spikes | Blocking |
| End August | Phases 1 to 4 | **Protect this.** Session is deliverable with DevUI alone from here |
| Mid September | Phase 5 companion | Allan goes independent this month, assume reduced capacity |
| End September | Phase 6 voice, or cut | Decision point, not a deadline |
| Early October | Recordings, fallbacks, rehearsals | Ten full run-throughs minimum |
| Mid October | Slides frozen | Re-verify MAF and Copilot Studio news first |

---

## 10. Risks

| Risk | Impact | Mitigation |
|---|---|---|
| Thinker output unstable | Kills the demo | Phase 2 gate, sharpen data not prompts |
| Blazor AG-UI client too thin | Two weeks lost | Spike before committing, CopilotKit fallback |
| September capacity | Companion slips | Session works with DevUI alone after phase 4 |
| Voice scope creep | Eats October | Cut without hesitation |
| MAF or Copilot Studio ships something new | Slides go stale | Re-verify mid September and mid October |
| Conference network and Azure latency | Live demo fails | Recorded fallback per sequence, bounded fan-out |

---

## 11. Open decisions

- Fan-out size for the live run
- Blazor Web only, or a MAUI host added later
- Repo public from day one, or flipped at session time
- Entra Agent ID demoed, or slide only
