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

Not written yet. It will create the Entra app registrations, the Dataverse
application users, the two custom security roles and the Azure OpenAI
deployments, per section 2 of `docs/build-brief.md`. It needs authentication
that stays with Allan, so it lands alongside phase 4.
