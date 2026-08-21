# Identity boundary

Retrieval and writes run under different credentials, injected in the host. The
executors never see which one they hold - identity is decided outside agent code.

- The **Watcher** reads through a Dataverse client built from the reader credential.
- The **Execute** stage writes through a client built from the writer credential.

## Making it real

Until the application users exist, both credentials fall back to the interactive
`az login`, so the workflow runs but the boundary is not yet enforced. To enforce it,
set these (in user secrets or environment variables, never in the repo):

```
ZAVA_TENANT_ID
ZAVA_READER_CLIENT_ID       ZAVA_READER_CLIENT_SECRET     # the zava-watcher app user, Zava Agent Reader
ZAVA_WRITER_CLIENT_ID       ZAVA_WRITER_CLIENT_SECRET     # the zava-operator app user, Zava Agent Writer
```

## What must be provisioned (needs a tenant admin)

- Two Entra app registrations: `zava-watcher`, `zava-operator`.
- Two Dataverse application users bound to them.
- Two security roles:
  - `Zava Agent Reader` - read on account, opportunity, activitypointer, annotation,
    contact. Nothing else.
  - `Zava Agent Writer` - create on task and appointment, write on opportunity limited
    to stage and estimated close date. No delete anywhere.

Once set, the reader credential provably cannot write: the Execute stage runs under the
writer, and the reader role has no create or write privilege, so a write attempted under
the reader identity is refused by Dataverse.
