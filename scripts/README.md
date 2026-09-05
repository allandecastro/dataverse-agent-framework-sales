# Scripts

Provisioning, dataset seeding, demo reset, and run helpers for the demo environment.

## Prerequisites

- PowerShell 7 or later (`pwsh`)
- Azure CLI, signed in: `az login`
- The signed-in identity has write access to the target Dataverse environment

## seed.ps1

Seeds the dataset from `data/dataset.json`. Idempotent: every record is written to a
deterministic GUID, so re-running updates in place and never duplicates. Relative date
offsets are resolved to absolute dates at run time.

```pwsh
./scripts/seed.ps1 -EnvironmentUrl https://org.crm.dynamics.com
```

Records are owned by the caller by default, and a re-run re-assigns them to the caller. Run
it as the seller (`ZAVA_SELLER_USER_ID`) or pass `-OwnerId <systemuserid>`, otherwise the
Watcher's owner-scoped account query sees nothing.

The `expected.archetype` oracle in the dataset is not seeded. The model must derive the
archetype, never read it.

## provision.ps1

Creates the two agent identities and their least-privilege security roles for the identity
boundary: the `zava-watcher` and `zava-operator` app registrations (with client secrets),
their Dataverse application users, and the `Zava Agent Reader` and `Zava Agent Writer`
roles.

```pwsh
./scripts/provision.ps1 -EnvironmentUrl https://org.crm.dynamics.com
```

Run by Allan, with tenant admin. Validate the role scopes at the top of the script first. It
prints client ids and secrets to store in the git-ignored `scripts/zava.env` (`ZAVA_TENANT_ID`,
`ZAVA_READER_CLIENT_ID`/`SECRET`, `ZAVA_WRITER_CLIENT_ID`/`SECRET`) - never commit them. It
rotates the client secrets on every run, so to add one privilege to a live role prefer a
targeted `AddPrivilegesRole` call and mirror it in the script. See `docs/identity.md`.

## reset-demo.ps1

Restores the demo to its canonical state so every run reads identical data. Once approved,
the Operator writes real opportunity edits (stage, close date) that mutate the fields the
Watcher reads on the next run, so run this before each demo or rehearsal: it re-seeds the
opportunities from `data/dataset.json` and deletes the tasks and appointments the agents
created. Runs under the signed-in az identity, never the agent app users.

```pwsh
./scripts/reset-demo.ps1 -EnvironmentUrl https://org.crm.dynamics.com
```

## cleanup-demo-writes.ps1

The second half of the reset, usable on its own: deletes every task and appointment owned by
the writer app user (`zava-operator`). It does not revert opportunity edits - that is what
`seed.ps1` does. `-WhatIf` lists without deleting.

```pwsh
./scripts/cleanup-demo-writes.ps1 -EnvironmentUrl https://org.crm.dynamics.com -WhatIf
```

## run-companion.sh and run-host.sh

Launch the companion (http://localhost:5123) or the console host against the shared Zava
environment. Endpoints, deployment names, the seller id and the Speech region/resource id
are configuration and live in the scripts; the agent identity secrets are sourced from
`scripts/zava.env` when present, otherwise both credentials fall back to `az login`.

```bash
./scripts/run-companion.sh          # the companion
./scripts/run-host.sh               # the console host; ZAVA_APPROVE=none|all|1,3 decides the gate
./scripts/run-host.sh graph         # prints the workflow as Mermaid and exits
ZAVA_PERSONA=ae ./scripts/run-host.sh   # the AE persona (MEDDPICC) in the console
```
