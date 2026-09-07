# Identity boundary

Retrieval and writes run under different credentials, decided in `SquadFactory` at the
Dataverse client layer. The executors never see which one they hold, and the LLM agents
(Thinker, Operator, Scorer) hold no Dataverse credential at all - identity is
decided outside agent code.

- The **Watcher** reads through a `ServiceClient` built from the reader credential
  (`zava-watcher` app user, `Zava Agent Reader` role).
- The **Execute** stage writes through a `ServiceClient` built from the writer credential
  (`zava-operator` app user, `Zava Agent Writer` role).
- `AgentIdentity.WithAuthority` is a MAF run middleware (`AIAgentBuilder.Use`) wrapped
  around each LLM agent. It does not add security; it stamps the authority the agent runs
  under on every call (`[identity] Thinker running under authority: reasoning only - no
  tools, no Dataverse identity`), so "the Thinker has no identity" is visible in the log,
  not just asserted in a comment.

## Configuration

The app users' client ids and secrets live in the git-ignored `scripts/zava.env`, sourced by
the run scripts. Never in the repo.

```
ZAVA_TENANT_ID
ZAVA_READER_CLIENT_ID       ZAVA_READER_CLIENT_SECRET     # the zava-watcher app user, Zava Agent Reader
ZAVA_WRITER_CLIENT_ID       ZAVA_WRITER_CLIENT_SECRET     # the zava-operator app user, Zava Agent Writer
ZAVA_SELLER_USER_ID                                       # the seller whose accounts the Watcher reads
```

Without the secrets both credentials fall back to the interactive `az login`, so the
workflow still runs but the boundary is not enforced. Startup says which mode is active:
`[identity] reader: app user <id>` or `[identity] reader: interactive az login (fallback -
identity boundary NOT enforced)`. Rehearse with the app users, so the boundary shown on
stage is real.

The reader app user owns no accounts, so the account query filters by the seller's owner id
(`ZAVA_SELLER_USER_ID`) rather than `EqualUserId`. The agent keeps its own least-privilege
identity - this is not impersonation.

## What is provisioned (`scripts/provision.ps1`, needs a tenant admin)

- Two Entra app registrations: `zava-watcher`, `zava-operator`. Note that the script rotates
  their client secrets on every run (`az ad app credential reset`); to add a privilege to a
  live role, call `AddPrivilegesRole` for that privilege rather than re-running the script.
- Two Dataverse application users bound to them.
- Two security roles:
  - `Zava Agent Reader` - read on account, opportunity, activitypointer, annotation,
    contact. Nothing else.
  - `Zava Agent Writer` - create and append on activity (task, appointment), append-to on
    opportunity and contact, write on opportunity, and the privileges an appointment with
    attendees needs (read async operation, share activity). No delete anywhere. The app only
    ever writes the opportunity stage and estimated close date, but that field scope is
    enforced by the fixed write tools, not by a Field Security Profile - do not claim
    field-level enforcement.

## What the writes look like

Appointments are real meetings: the seller (`ZAVA_SELLER_USER_ID`) is the organizer and a
required attendee, and the client contact the action names is invited too; the owner stays
the writer app user so `scripts/cleanup-demo-writes.ps1` can find and remove them. Tasks stay
internal. The demo contacts have no email address, so no invitation leaves the tenant.

## Acceptance

The reader credential provably cannot write: the Execute stage runs under the writer, and
the reader role has no create or write privilege, so a write attempted under the reader
identity is refused by Dataverse.
