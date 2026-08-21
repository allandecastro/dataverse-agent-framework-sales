# Voice - design

Phase 6. Optional and the first thing to cut. This captures the intended design so it is
not lost, whether or not it ships.

## The problem

Today the run is a deterministic spine: one fixed question, one pipeline, one narrated
answer. Voice is what turns that scripted narration into a conversation - a two-way
exchange where the seller can ask, drill in, and act by speaking.

The tension: a two-way voice needs a model that understands free speech, which looks like
a second brain. But the brief says **no second orchestrator** and the squad is the only
decision-maker.

## The principle that resolves it

**The voice is an interface over the one run, not a second brain.**

The voice model has no tool that judges or queries Dataverse freely. Its only intents read
what the run already produced, or push a decision into the existing approval gate. The
squad remains the only thing that assesses; the voice only restitutes and acts. Said on
stage: the voice is a skin, the squad is the only engine.

If we ever let the voice call Dataverse or re-assess, it becomes a second brain and the
demo has lost its point. That is the line to hold.

## The interaction, two-way

- Seller: "What should I focus on this week?" -> triggers the run. The system speaks as it
  streams: "I looked at 10 accounts. Three are ready to close: Adventure Works, Alpine Ski
  House, Litware..."
- Seller: "Why is Litware ready?" -> spoken answer from the already-computed assessment
  (the four BANT axes and their citations). No re-judging, so it is stable and
  deterministic.
- Seller: "Book the meeting with Omar." -> maps to the action the Operator already prepared
  for Litware, and goes through the same approval gate: "I'll book a close-out call with
  Omar. Shall I write it?" -> "yes" -> the write executes under the writer identity.
  Approval by voice, without bypassing the gate.
- Seller: "What about Wide World Importers?" / "Skip Fabrikam." -> navigation and drill by
  voice.

## How it binds to the architecture

- A second Blazor client, sharing the same AG-UI session as the companion, subscribed to
  the same event stream. What the voice says is grounded in the same deterministic run the
  screen shows. This is where the two event types pay off: the voice narrates the
  `ProgressEvent`s deterministically and reads the `AgentMessageEvent` content (assessments,
  actions) on request.
- Bounded intents only: `read_summary`, `explain_account`, `list_actions`,
  `approve`/`reject`, `open_evidence`, `run`. None computes sales judgement; each reads the
  run's output or pushes a decision into the existing approval `RequestPort`. The identity
  and approval boundaries stay intact - voice "approve" flows through the same gate and the
  same writer identity.
- Pulsing indicator, nothing else.

## Prerequisites and risks

- Needs a realtime speech component (for example Azure OpenAI Realtime, or Azure Speech
  STT plus TTS). This is a new dependency and likely a new Azure resource - flag and get
  approval before adding it (see `CLAUDE.md`, "Ask before adding a NuGet package").
- The real risk is the stage: accents, room noise, latency, barge-in. Hence "first to be
  cut." Mitigations: a small fixed intent grammar, and the screen still shows everything -
  the voice is additive, not the only channel.
- Determinism is preserved: the run is stable and the voice only restitutes it.

## What must come first

Voice only makes sense once the run, the identity boundary, and the session samples are
locked, because voice is a skin over that run. Order before voice: the phase 4 identity
boundary and provisioning, then the phase 7 session assets, then Operator action quality.
