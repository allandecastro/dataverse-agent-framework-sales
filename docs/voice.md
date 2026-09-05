# Voice

Voice shipped inside the companion (there is no separate voice project). This is the design
as built, and the line it holds.

## The principle

**The voice is an interface over the one run, not a second brain.**

Speech only transcribes and synthesises. What the seller says goes through the same
`PipelineConversation` as the typed question box: a grounded spokesperson agent whose only
knowledge is the current run's analysis (baked into its instructions), with no tools and no
Dataverse access. It cannot re-judge, cannot query, cannot write. When the seller says
"approve the Litware appointment", the spokesperson returns a structured `ConversationTurn`
with `Action = Approve` and the proposal numbers, and the companion pushes that into the same
`RequestPort` approval gate the checkboxes use. Approval by voice never bypasses the gate,
and the write still runs under the writer identity.

If the voice ever calls Dataverse or re-assesses, it becomes a second orchestrator and the
demo has lost its point. That is the line.

## How it works

- **Azure AI Speech on the Foundry resource** (`aif-zava-salesintel`), STT and TTS. No new
  Azure resource; no key in the browser. `GET /api/speech-token` on the companion returns a
  short-lived keyless authorization token (`aad#<resourceId>#<token>`, from the same
  `AzureCliCredential` the rest of the app uses) plus the region.
- **Browser SDK self-hosted** at `wwwroot/lib/speech-sdk.js`, so conference wifi cannot
  leave `SpeechSDK` undefined on stage. `wwwroot/js/voice-interop.js` wraps it:
  `recognizeOnce` (push to talk, one utterance), `speak`, `stopSpeaking`, `stopListening`.
- **Playback through Web Audio.** Blazor Server answers arrive over SignalR, outside any user
  gesture, and Safari and Chrome only start audio inside one. The interop creates and resumes
  a single `AudioContext` on a capture-phase pointer/touch/key listener, synthesises the answer
  to MP3 bytes, decodes them, and plays through that unlocked context. This is what makes TTS
  work on every browser.
- **Push to talk**, one utterance per press. The mic button becomes a stop button while
  listening and while speaking. Error text is shown in the conversation but never read
  aloud.
- **Multi-turn memory** comes from the MAF `AgentSession`: one session per run, so "approve
  the appointment" resolves after "why is Litware ready" without naming the account again.

## The interaction

- "Why is Litware ready?" -> spoken answer from the already-computed assessment, citing
  evidence in human terms ("the note from 20 August"), never a record id.
- "Approve the Litware appointment." -> the proposal goes through the gate, the appointment
  is written under the writer identity, and an "Open record" link appears under the answer
  and in the Done panel as the write lands.
- "What about Wide World Importers?" -> drill by voice, grounded in the same run.

## Stage risks and mitigations

Accents, room noise, latency. Mitigations: the screen shows everything the voice says, the
typed box is the same path, and a stop button always ends listening or speaking. Speech is
in `en-US`; the STT is good at sales vocabulary but can mishear names - the spokesperson is
told to match loose names to the proposals.
