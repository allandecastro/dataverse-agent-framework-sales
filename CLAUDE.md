# CLAUDE.md

Project instructions for Claude Code. Read `docs/build-brief.md` for the full plan.

## What this is

A conference demo for PPCC26: a multi-agent sales intelligence system on Dataverse, built with Microsoft Agent Framework. The repo is public and will be read as a reference by Power Platform and Dynamics 365 developers.

## The one rule that overrides the others

**Microsoft Agent Framework is the subject, not the plumbing.**

Every piece of code should make a framework capability legible. When there are two ways to do something and one of them showcases a MAF primitive (workflow graph, superstep, checkpoint, approval gate, middleware, skills provider, context provider), take that one - even if a shortcut would work.

If you find yourself writing custom code that reimplements something MAF already provides, stop and use the framework instead.

## Writing conventions

- All code, comments, docs and output in English
- Plain hyphens only. Never em dashes or en dashes, anywhere - code, comments, markdown, prompts, seed data
- Comments explain why, not what

## Git conventions

- Never mention Claude, Claude Code or Anthropic as a co-author or author in commits or pushes. Do not add a `Co-Authored-By: Claude` trailer or any equivalent. Commits are authored by Allan.
- Commit or push only when asked.

## Code conventions

- .NET 10, C# latest, nullable enabled. Targeting .NET 10 (LTS) rather than the originally planned .NET 9, which reached end of support in May 2026. The target framework is centralised in `Directory.Build.props`.
- Records for contracts, classes for services
- File-scoped namespaces
- Async all the way, `CancellationToken` on every async signature
- No secrets in the repo. User secrets locally, environment variables in CI

## Architecture rules

**Contracts live in `Zava.SalesIntelligence.Agents`.** Do not create a `Shared`, `Common` or `Core` project. A catch-all project is the first sign of a rushed repo.

**Each agent owns its tools and its identity.** Never let one agent reach for another's credential or tool, even temporarily to unblock something. The identity boundary is the point of the demo.

**The Thinker has no tools and no identity.** This is deliberate and load-bearing. Do not add tools to it to solve a problem - the problem is elsewhere.

**The Ranker is plain code, not an agent.** Ordering is deterministic so the demo produces the same result every run. Do not move ranking logic into a prompt.

**Identity lives at the Dataverse client layer (the reader and writer app users), enforced by Dataverse roles - not in agent code; the LLM agents hold no credential.** A run middleware (`AgentIdentity.WithAuthority`) makes each agent's authority legible on every call.

**Dataverse queries are fixed in code.** The model chooses when to call a tool, never what to ask. Bounded parameters, no free-form query strings, no dynamic FetchXML built from model output.

**Write tools are approval-required.** The approval gate is a framework primitive, not an `if` statement in application code.

## Two event types, two code paths

`ProgressEvent` is emitted by the workflow and is deterministic.
`AgentMessageEvent` comes from a model and varies between runs.

The companion renders both as dialogue but they must not share a code path. Keeping them separate is what lets us control demo pacing and reason about reliability.

## Demo constraints that shape the code

- Event pacing is server-side. The workflow decides when to emit; the client renders what it receives. Do not add client-side queuing or animation.
- The companion has three screens: conversation thread, four archetype cards, evidence detail panel. No navigation, no settings, no history.
- Evidence clicks open the internal detail panel, never Dynamics. The demo thesis is that the seller does not open the CRM.
- The challenge edge from Operator back to Thinker is capped at one round trip so stage timing stays predictable.

## What not to do

- Do not introduce abstractions that were not asked for. Clarity over cleverness - people will read this code after the talk.
- Do not add a dependency without flagging it first.
- Do not refactor working code from a previous phase unless asked.
- Do not add tests for demo scaffolding. Do add them for the Ranker and the contracts.
- Do not "improve" the agent instructions or the SKILL.md without being asked. They are tuned against real output and small edits change behaviour.

## When output is unstable

If the Thinker produces different archetypes across runs on the same data, the fix is in the dataset, not the prompt. Sharpen the signals until any reasonable model finds the same answer. Flag it rather than tuning the prompt to compensate.

## Validation gates

Phase 2 does not pass until ten consecutive runs on the same data produce the same archetype for every account, and every BANT axis cites a record id that actually exists in Dataverse.

Phase 4 does not pass until the reader identity provably cannot write.

## Ask before

- Adding a NuGet package
- Changing a contract in `Zava.SalesIntelligence.Agents`
- Anything that writes to Dataverse outside the Operator's four tools
- Anything that touches security roles or app registrations
