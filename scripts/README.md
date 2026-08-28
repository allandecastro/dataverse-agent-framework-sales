# Scripts

Provisioning and dataset seeding for the demo environment.

## Prerequisites

- PowerShell 7 or later (`pwsh`)
- Azure CLI, signed in: `az login`
- The signed-in identity has write access to the target Dataverse environment

## seed.ps1

Seeds the phase 2 dataset from `data/dataset.json`. Idempotent: every record is
written to a deterministic GUID, so re-running updates in place and never
duplicates. Relative date offsets are resolved to absolute dates at run time.

```pwsh
./scripts/seed.ps1 -EnvironmentUrl https://org.crm.dynamics.com
```

Records are owned by the caller by default. Pass `-OwnerId <systemuserid>` to
assign them to a specific user, for example one of the phase 4 test users.

The `expected.archetype` oracle in the dataset is not seeded. The model must
derive the archetype, never read it.

## provision.ps1

Creates the two agent identities and their least-privilege security roles for the
phase 4 identity boundary: the `zava-watcher` and `zava-operator` app registrations
(with client secrets), their Dataverse application users, and the `Zava Agent Reader`
and `Zava Agent Writer` roles.

```pwsh
./scripts/provision.ps1 -EnvironmentUrl https://org.crm.dynamics.com
```

Run by Allan, with tenant admin. Validate the role scopes at the top of the script
first. It prints client ids and secrets to store in user secrets (`ZAVA_TENANT_ID`,
`ZAVA_READER_CLIENT_ID`/`SECRET`, `ZAVA_WRITER_CLIENT_ID`/`SECRET`) - never commit them.
See `docs/identity.md`. Restricting the writer to the opportunity stage and close-date
fields needs a Field Security Profile, configured in the maker portal.

## reset-demo.ps1

Restores the demo to its canonical state so every run reads identical data. Once approved,
the Operator writes real opportunity edits (stage, close date) that mutate the fields the
Watcher reads on the next run, so run this before each demo or rehearsal: it re-seeds the
opportunities from `data/dataset.json` and deletes the tasks and appointments the agents
created. Runs under the signed-in az identity, never the agent app users.

```pwsh
./scripts/reset-demo.ps1 -EnvironmentUrl https://org.crm.dynamics.com
```
