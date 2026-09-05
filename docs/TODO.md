# TODO / demo readiness

Working notes for the PPCC26 live session. A checklist, not a spec. See `docs/build-brief.md`
for the original plan, `docs/architecture.md` for what was built, and `CLAUDE.md` for the rules.

## Where it stands (2026-09-05)

Everything is on `main`. The squad runs end to end under the reader/writer app users; all
four write tools write for real (task, appointment with attendees, stage -> Qualify,
close date -> suggested date); the approval gate is iterative; the companion has the
SDR / AE persona toggle (full MEDDPICC in AE), the spokesperson with voice (STT + TTS + stop),
multi-turn memory, live "Open record" links, and a right sidebar layout. The SDR determinism
gate passed on 2026-08-28 (archetype-level, 8 of 8 complete runs identical; the BANT score
can wobble by one without flipping an archetype).

## Before the live demo

- [ ] **Run `scripts/reset-demo.ps1` before every run**, as the seller. Approved stage and
      close-date writes edit the fields the Watcher reads, so without a reset the next run drifts.
- [ ] **AE determinism check.** In AE the archetype is a model judgement over eight elements.
      Run the AE persona several times on the reset dataset and confirm the board is stable. If
      it is not, sharpen the dataset or the `meddpicc-scoring` examples - not the prompt tuning.
- [ ] **Live mic and speaker test on the demo machine** (headless cannot drive a mic): grant mic
      permission on http://localhost:5123, "why is Litware ready", hear it, "approve the Litware
      appointment", see the write and its link, then reset.
- [ ] **Rehearse the full run twice on the demo network and time it.** The reasoning stage
      (about 60-90 s to the gate) is the long pole. Do not re-run back to back on stage: ten
      rapid runs hit rate limits during the determinism check.
- [ ] **Fallback recording** of a clean run, in case the network or Foundry is slow.

## Code anchors to show live

- **Connect to Foundry** - `src/Zava.SalesIntelligence.Workflow/SquadFactory.cs`:
  `AzureOpenAIClient` + `AzureCliCredential`, keyless; the reasoning deployment; the reader
  and writer credentials built side by side.
- **Orchestrate** - `src/Zava.SalesIntelligence.Workflow/SalesWorkflow.cs`: the graph, the
  type-routed challenge edge capped at one round trip, the `RequestPort` approval gate and its
  loop edge; `Executors.cs` for the supersteps; `docs/workflow-graph.md` for the picture.
- **Identity** - `AgentIdentity.cs` (the MAF run middleware stamping each agent's authority),
  `scripts/provision.ps1` (reader vs writer roles), `WriterTools.cs` (the four sanctioned
  writes). The honest line: the boundary is enforced by Dataverse roles at the client layer;
  the middleware makes it visible. The app writes only stage and close date, but that field
  scope is code, not a Field Security Profile - do not claim field-level enforcement.

## Nice to have (only if time allows)

- [ ] "Why this score" explainer: a short legend for BANT and MEDDPICC statuses and a
      one-line summary per card, so the audience reads the board without a narrator.
- [ ] Relabel "Reject all" to "Done" once a round has been approved, to match the iterative gate.
- [ ] A11y leftovers: focus management when panels open, Esc to close the evidence and approval
      panels.
- [ ] A calmer "the squad is working" beat while the reasoning stage runs.

## Parked (after the talk, unless decided otherwise)

- **Ambient companion**: auto-start on login and a compact widget mode once a run finishes.
  Cheaper and more honest than packaging a desktop app.
- **Teams surface** (M365 Agents SDK): the same `PipelineConversation` as another surface.
  Not the hero for a MAF talk - a bot hides the orchestration and identity that make this
  demo different. Real cost: NuGet, bot registration, hosting, auth (ask before any of them).
- **MEDDPICC-driven actions**: today MEDDPICC drives the AE board; the Operator's actions are
  still BANT-driven.
- **MAF upgrade**: dependabot PR #34 (1.17 -> 1.19) was closed to keep the demo pinned; reopen
  after the session.
- `samples/magentic` is drafted but held: the Magentic driver does not pump past one superstep
  on MAF 1.17. The README comparison is the value.
- Ranker `ReferenceEqualityComparer` round-trip reads obscurely; left as is because changing it
  risks the deterministic ranking.
