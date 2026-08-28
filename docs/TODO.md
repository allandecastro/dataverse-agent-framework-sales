# TODO / demo-readiness

Working notes for the PPCC26 live session. Not a spec - a checklist and a set of things to
look at. See `docs/build-brief.md` for the plan and `CLAUDE.md` for the rules.

## Status at a glance (2026-08-25)

All four write tools now write for real (task, appointment, stage update -> demote to Qualify,
close-date update -> model-suggested date). Voice works (STT + TTS + stop). The companion has a
floating composer, refresh, multi-turn memory, and "Open record" deep links. All uncommitted on
branch `companion-voice`.

## Must-do before the live demo

- [ ] **Run `scripts/reset-demo.ps1` before every run.** Stage/close-date writes mutate the
      fields the Watcher reads; without a reset the next run drifts. This is now load-bearing.
- [x] **10-run determinism gate (SDR)** - DONE 2026-08-28. Archetype-level: PASS - all 8 complete
      runs produced identical archetypes for every account; MEDDPICC did NOT regress the SDR/BANT
      path. Two notes (both pre-existing, not MEDDPICC): (a) the BANT SCORE wobbles +/-1 run to run
      (an axis flips Confirmed<->Partial) but never flips an archetype - model non-determinism the
      archetype rule absorbs; (b) 2 of 10 runs were degraded (one empty, one missing 3 accounts),
      almost certainly rate-limiting from 10 back-to-back runs in ~15 min (~100 reasoning calls) -
      a single live run does not stress the API like this. Live mitigation: do not re-run rapidly
      on stage, and keep a fallback recording (see the P0 reliability item).
- [ ] **Commit the `companion-voice` batch** in coherent messages (no Claude co-author).
- [ ] **Live mic + speaker test** of the voice path on the demo machine (headless can't drive a
      mic): grant mic permission on http://localhost:5123, ask "why is Litware ready", hear it,
      "approve the Litware appointment" -> write via the gate -> `reset-demo.ps1`.
- [ ] **Rehearse the full run end to end** at least twice on the demo network. Time it. The
      reasoning stage (~60-90s to the gate) is the long pole - know exactly how long you wait.
- [ ] **Have a fallback recording** of a clean run in case the network or Foundry is slow on stage.

## Live-explain: the three code anchors to show

The talk needs Allan to SHOW and EXPLAIN these. Anchors below; confirm each is presentable as-is
(the running code review will flag any confusing bits).

- **Connect to Foundry** -> `src/Zava.SalesIntelligence.Companion/WorkflowFactory.cs`
  (`AzureOpenAIClient` + `AzureCliCredential`, keyless; two deployments - fast vs reasoning).
- **Orchestrate the agents** -> `src/Zava.SalesIntelligence.Workflow/SalesWorkflow.cs` (the graph:
  Watcher -> Thinker -> Ranker -> Operator, supersteps, the one-round challenge edge, the approval
  RequestPort) + `Executors.cs`. `workflow.ToMermaidString()` / `docs/workflow-graph.md` for the picture.
- **Identity** -> `scripts/provision.ps1` (reader vs writer roles, least privilege) + the middleware
  that injects identity + `src/Zava.SalesIntelligence.Dataverse/WriterTools.cs` (the four sanctioned
  writes). The reader-can't-write / writer-can't-read boundary is the money slide.

## Demo-effect enhancements (candidates, not commitments)

- [ ] **Widget / companion mode**: once a run finishes, collapse the cards into compact tiles with
      the mic centered - the "ambient assistant" feel. (Designed but not built.)
- [ ] **Auto-start on login** (startup shortcut opening the local companion) so it feels like a
      product that is "just there". Cheaper and more honest than packaging a desktop app.
- [ ] Smooth the wait to the gate: a more cinematic "the squad is working" beat, or a lower
      reasoning-effort fast path for rehearsals.

## Ideas & deferred (set aside, not required for the talk)

- [~] **MEDDPICC as a persona lens (SDR / AE)** - IN PROGRESS, full spec in `docs/meddpicc-plan.md`.
      Chosen shape: **B-view** (persona toggle; BANT stays the byte-identical deterministic engine;
      MEDDPICC is a separate additive AE-only read on `RankedRecommendation.Meddpicc`, not on
      `OpportunityAssessment`). Persona selection = **UI toggle** (decided). P0 contract DONE
      (`MeddpiccScorecard`/`MeddpiccElement` + nullable field; additive, tests green). Next: P1 the
      `meddpicc-scoring` SKILL + a `MeddpiccScorer` pass wired only for AE, then P2 UI/Q&A, P3
      validation (SDR must stay byte-identical). B-deep (MEDDPICC drives archetypes) is a later step.
- [ ] Small a11y left over: full focus management when panels open, and Esc-to-close on the evidence
      and approval panels.
- [ ] Ranker `ReferenceEqualityComparer` round-trip reads obscurely - carry `Judged` through instead
      (works today; low value, determinism-sensitive).

## Product directions (post-talk unless decided otherwise)

- **Real desktop companion (MAUI / Blazor Hybrid)**: only if the talk narrative needs a true
  desktop app. Recommendation: skip for the talk; auto-start + widget covers the "companion" story.
- **Teams agent (M365 Agents SDK), like the Sales Development Agent**: reuse the SAME
  `PipelineConversation` as another surface. Recommendation: NOT the hero for a MAF talk - a Teams
  bot hides the orchestration/identity that make this demo unique. Keep it as a "same spokesperson,
  many surfaces" talking point / stretch goal. Real cost: NuGet + bot registration + hosting + auth
  (ask before adding any of these).

## Known technical debt / open items

- [ ] Writer field-scope (opportunity stage + close date only) is enforced in application code, not
      by a Dataverse **Field Security Profile**. For the identity story to be airtight, configure the
      FSP in the maker portal (noted in provision.ps1).
- [ ] `samples/magentic` is drafted but held (the Magentic multi-round driver doesn't pump past one
      superstep on MAF 1.17 - needs the official driver sample). The README comparison is the value.
- [ ] The privilege `prvReadAsyncOperation` was added to the LIVE writer role directly and to
      `provision.ps1`; a from-scratch re-provision now includes it.

## Code review findings (2026-08-25)

Ranked most important first. `[v]` = verified by hand in this session.

### P0 - live-demo reliability (could crash or hang on stage) - DONE 2026-08-25

Shipped: `StructuredAgent.RunAsync` retries once; Thinker/Challenge/Operator degrade one account
(drop / keep first / no actions) instead of faulting the run; STT surfaces real `Canceled` errors
and returns empty on a user stop; a stop button cancels listening; the Speech SDK is self-hosted at
`wwwroot/lib/speech-sdk.js` (vendored ~950 KB - flagged); the component is `IDisposable` and releases
the approval gate on a dropped circuit; voice never reads error text aloud; Refresh stops in-flight
voice. (Full SignalR reconnection/resume is still light - see note below.) Original items for the record:

- [ ] **No error resilience around model calls.** Every agent stage (`StructuredAgent.cs:27-33`)
      throws on an empty/undeserializable completion, inside `Task.WhenAll` fan-outs
      (`Executors.cs:23,47,143,180`) -> one flaky completion faults the whole run with a stack
      trace. Only the write stage has try/catch. Action: wrap agent stages / degrade per-account.
- [ ] **STT swallows `Canceled`** (`voice-interop.js` `recognizeOnce`): a bad token, denied mic,
      or dropped network returns `''` -> silent dead mic on stage. Action: surface `Canceled` like
      `speak()` already does.
- [ ] **No cancel while listening + input disabled** (`Home.razor` `AskByVoice`): can hard-lock the
      composer until Azure's silence timeout. Action: give `_listening` a stop button too.
- [ ] **Speech SDK from a live unversioned CDN** in `<body>` (`App.razor:19`): single point of
      failure on conference wifi -> `SpeechSDK` undefined -> all voice throws. Action: self-host a
      pinned bundle (with SRI).
- [ ] **Component is not `IDisposable`; `await _pending.Task` has no timeout** (`Home.razor`): a
      SignalR drop at the gate strands the run ("The squad is working..." forever) and a run
      streaming after disposal calls `StateHasChanged` on a dead circuit. Action: implement dispose
      + guard, consider a cancellation path.
- [ ] **App speaks error text aloud** (`Home.razor` `AskByVoice` -> `speak`): reads `"(error: ...)"`
      strings to the audience. Action: don't TTS failure answers.
- [ ] **Refresh doesn't stop in-flight voice** (`Home.razor` `RunAsync`): audio plays over the new
      run and a voice callback can `_qa.Add` into the just-cleared list. Action: stop TTS + cancel
      the voice turn on refresh.

### P1 - claim-vs-code gaps (Allan explains these live; a sharp audience will catch them) - MOSTLY DONE 2026-08-25

Shipped: a real MAF **identity middleware** (`AgentIdentity.WithAuthority`, `AIAgentBuilder.Use`) now
stamps each agent's authority on every run ("Thinker: reasoning only, no tools, no Dataverse identity";
"Operator: proposes writes for the gate, no identity") - verified firing 23x in a host run without
breaking structured output. Startup now logs the reader/writer identity mode (app user vs interactive
fallback). The stale "two deployments" story is cleaned: both agents on the reasoning model, the cheap
`ZAVA_OPENAI_DEPLOYMENT` removed from the app run-scripts (samples keep it), `architecture.md` fixed. The
challenge-edge comment now says "conditional by message type". Writer field-scope: decided NO Field
Security Profile - keep entity-level write; do not claim field-level enforcement on stage (the app only
writes stage + close date). STILL OPEN: `CLAUDE.md:46` says "identity is injected by middleware" - the
middleware makes authority legible but does not inject the credential (that lives at the Dataverse client
layer, enforced by roles); needs a one-line honest rewrite (proposed, awaiting Allan since it edits the
project constitution). Original items for the record:

- [ ] **[v] No middleware, despite `CLAUDE.md:46` "identity is injected by middleware".** It is
      plain constructor DI (`Program.cs`/`WorkflowFactory.cs`). Decide: build a small identity
      middleware (showcases a MAF primitive AND matches the thesis - strong for the talk), or
      correct CLAUDE.md + the narrative to "DI-injected identity".
- [ ] **[v] The "two deployments, cheap vs reasoning" story is stale.** The main app uses the
      reasoning deployment for BOTH agents; `ZAVA_OPENAI_DEPLOYMENT` is read only by the samples;
      `architecture.md:66` still says the Operator runs on the fast model. Action: fix the docs and
      the slide, or re-wire the cheap tier for the Watcher-tier work.
- [ ] **Writer "limited to stage + close-date fields" is not enforced by provisioning.**
      `provision.ps1` grants entity-level `prvWriteOpportunity`; field scope is a manual Field
      Security Profile (`provision.ps1:201-203`). Action: configure the FSP, or don't claim
      field-level restriction on stage.
- [ ] **The challenge edge is type-routed, not a predicate.** The "conditional" comment
      (`SalesWorkflow.cs`) invites a "where's the condition?" question. Action: tighten the comment;
      rehearse the honest explanation (routes by message type, capped structurally).
- [ ] **[v] Reader/writer silently collapse to one `az login` if the app-user secrets are unset**,
      with no warning (`Program.cs`/`WorkflowFactory.cs`). Action: startup log stating which identity
      mode is active, so the boundary is provably on during the demo.

### P2 / P3 - DONE 2026-08-25 (build + 20 tests + `dotnet format` all green)

Shipped: write target now uses the single opportunity directly (no model-name match) when there is
one, and orders opportunities otherwise; `WatcherQueries` adds explicit `AddOrder` (accounts by
accountnumber, opps by id) to close the ranking-tie determinism hole; the `ArchetypeJsonConverter`
fallback is documented (inert - Confidence governs, Ranker drops it); dead `SalesBrief` record removed
(file renamed to `RankedRecommendation.cs`, which it actually held); dead `.ask*`/`.ask-row` CSS removed;
`cleanup-demo-writes.ps1` identical-if simplified; CLAUDE.md:46 rewritten to be honest about identity.

DONE (were gated, approved and applied 2026-08-25):
- **`PreparedAction.TriggeringSignal` + `RequiresApproval` removed** - schema slimmed; verified the
  Operator still produces 11 actions in a host run, so the change did not destabilise it.
- **Operator prompt "call challenge_assessment" removed** - replaced with honest guidance (the
  challenge is orchestrated by the workflow, not called by the agent); SKILL's RequiresApproval line fixed.
- **Host/WorkflowFactory duplication removed** - both entry points now build via a shared
  `SquadFactory` in the Workflow project (needed 3 existing-in-solution package refs added there,
  flagged); `WorkflowFactory` keeps only the companion-only pieces (Q&A agent, record links). Verified
  end to end via the host (25 identity-middleware hits, all supersteps, gate, 11 actions).

STILL DEFERRED (low value):
- **Ranker `ReferenceEqualityComparer` round-trip** - works, left as-is (changing it risks the
  deterministic ranking). `DataverseClientFactory` `CancellationToken.None` is a non-issue (the token
  provider callback has no ct to thread).

Original items for the record:

### P2 - latent correctness (low risk on the current 1-opp-per-account dataset)

- [ ] **Write target chosen by model-name string equality + `FirstOrDefault` fallback**
      (`Executors.cs:191-193`): a paraphrase can land a stage/close-date write on the wrong open
      opportunity for a multi-opp account. Action: key off the opportunity id.
- [ ] **Queries have no `OrderBy`** (`WatcherQueries.cs`): ranking ties are broken by unspecified
      Dataverse order -> a determinism hole. Action: add `AddOrder` (e.g. accountnumber).
- [ ] **Operator prompt instructs a nonexistent `challenge_assessment` tool** (`Operator.cs:15-19`);
      the challenge is orchestrated structurally. Flag only (do not edit the SKILL/prompt without
      Allan - CLAUDE.md).
- [ ] **`ArchetypeJsonConverter` maps any unknown value to `Reconnect`** silently
      (`OpportunityAssessment.cs`): fine for refusals (Ranker drops them) but could mask a malformed
      non-refusal. Action: sentinel or comment.

### P3 - public-repo cleanups (it will be read as a reference)

- [ ] **[v] Dead contracts:** `SalesBrief`, `PreparedAction.TriggeringSignal`, and
      `PreparedAction.RequiresApproval` are never read (`RequiresApproval` also contradicts
      "approval is a gate primitive, not a flag"). Action: remove.
- [ ] **Dead CSS** in `Home.razor` (`.ask*`, standalone `.mic`) - markup only uses `.composer*`.
- [ ] **Host `Program.cs` and `WorkflowFactory.cs` duplicate ~40 lines** of build (credentials,
      seller scope, agent wiring). Action: a single shared builder in the Workflow project.
- [ ] `cleanup-demo-writes.ps1:51` - both `if` branches are identical. `DataverseClientFactory.cs:19`
      hard-codes `CancellationToken.None`. Ranker's `ReferenceEqualityComparer` round-trip is obscure.

### P4 - accessibility / projector legibility - DONE 2026-08-25

Shipped: `--faint` darkened `#909090` -> `#6e6e6e` (AA on small text); account cards are now `<button>`s
with a focus ring (keyboard-reachable); the evidence panel has a close button; the composer input has an
aria-label; the log drawer is `inert` + `aria-hidden` when closed; the squad thread is an `aria-live`
region. Deferred (lower value): full focus management when panels open, and Esc-to-close. Original items:

- [ ] `--faint #909090` on white is ~3.2:1 (below AA 4.5:1) on small text - washes out on a
      projector. Darken it.
- [ ] Account cards are clickable `<div>`s (`Home.razor`): keyboard-inaccessible and unannounced.
      Make them `<button>`s.
- [ ] Evidence panel has no close/Esc; log drawer stays focusable when closed (add `inert`);
      composer input has no label; streaming thread has no `aria-live`; stacked panels don't manage
      focus.

### Noted but NOT a real issue

- Concurrent `IWorkflowContext.AddEventAsync` from the fan-out was flagged as a race, but MAF's
  event sink is a `ConcurrentQueue` (verified earlier) - event emission is thread-safe. Sequential
  `SendMessageAsync` after `WhenAll` is the only part worth keeping tidy.
