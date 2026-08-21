# Dataset

The demo dataset. Ten accounts owned by one seller, engineered so the Thinker
produces the same archetype for every account on every run. This file is the
fixture for the phase 2 gate: ten consecutive runs must reproduce every
`expected.archetype` below, and every cited record id must exist.

The data is fictional. Zava is Microsoft's standard fictional company; the
accounts are Zava's customers, named from the usual fictional set. `Contoso`
is used as a competitor, never as an account.

## How to read `dataset.json`

- **Dates are relative.** Every date is an offset in days from the moment the
  seed runs (`dateOffsetDays`). Negative is the past, positive is the future.
  `seed.ps1` resolves them to absolute dates at seed time, so the signals stay
  true whenever the dataset is re-seeded. There are no absolute dates anywhere.
- **Keys are stable.** Every record carries a symbolic `key`. The seed derives a
  deterministic GUID from it, so ids never change between seeds. The Thinker
  cites those ids, and the gate checks they exist.
- **`expected` is an oracle, not data.** The `expected` block on each account
  states the archetype and BANT the account is built to produce, with the reason
  mapped to the `bant-scoring` skill. It is the acceptance target. The seed does
  not write it to Dataverse - the model must derive the archetype, never read it.

## What each account is built to prove

| Key | Account | Archetype | The point |
|---|---|---|---|
| ZAVA-001 | Fabrikam | **Slipping** | Inversion. CRM looks strong (Propose, large amount) but a competitor is named, the close date moved twice and the champion was deactivated. |
| ZAVA-002 | Alpine Ski House | **ReadyToClose** | Inversion. CRM looks minor (small amount, early stage) but budget is approved in writing, the COO is engaged and the customer owns a date. |
| ZAVA-003 | Northwind Traders | Slipping | Well qualified, but a competitor is named and the engaged CFO has gone quiet, so precedence beats Reconnect. |
| ZAVA-004 | Tailspin Toys | AtRisk | Stage at Propose with BANT 2 and no competitor, so precedence lands on AtRisk, not Slipping. |
| ZAVA-005 | Fourth Coffee | InsufficientSignal | Fewer than two axes documented. The model must refuse to conclude. Dropped by the Ranker. |
| ZAVA-006 | Proseware | InsufficientSignal | Same, second refusal, so the behaviour is not a one-off. |
| ZAVA-007 | Adventure Works | ReadyToClose | Neutral. CRM already looks strong and the evidence agrees. No inversion. |
| ZAVA-008 | Wingtip Toys | Reconnect | Neutral. Solid qualification gone quiet. |
| ZAVA-009 | Litware | ReadyToClose | Neutral. BANT 3 with Timing confirmed and rising activity. |
| ZAVA-010 | Wide World Importers | AtRisk | Neutral. Under-qualified for its stage, no competitor. |

Distribution: Slipping 2, ReadyToClose 3, Reconnect 1, AtRisk 2, InsufficientSignal 2.
All four archetype cards light up, and the refusal to conclude is shown twice.

## Design rule

Every account is engineered, including the neutral ones. There are no throwaway
accounts: an account with ambiguous signals would waver between runs and break
the gate. If output is unstable, sharpen the signals here, never the prompt.
