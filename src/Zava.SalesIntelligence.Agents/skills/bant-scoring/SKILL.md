---
name: bant-scoring
description: Assess a sales opportunity against Budget, Authority, Need and Timing using Dataverse evidence, and classify it into an action archetype. Use when evaluating pipeline health, qualification quality, or deal risk.
license: MIT
metadata:
  author: zava-sales-intelligence
  version: "1.0"
---

# BANT scoring

## The four axes

**Budget** - can they pay, and is the money allocated?
Confirmed: an amount is validated in writing by the customer, or a
budget holder is named against a figure.
Partial: an amount is discussed but not validated, or a range only.
Missing: no record mentions money on the customer side.

**Authority** - is the person who signs identified and engaged?
Confirmed: a decision maker is a contact on the account and appears in
at least one activity.
Partial: a decision maker is named in a note but never met.
Missing: all known contacts are operational, or no buying process is
described.

**Need** - is the problem documented in the customer's own terms?
Confirmed: a note or activity records a specific pain, a metric, or a
consequence of inaction.
Partial: a generic interest is recorded without specifics.
Missing: nothing describes what the customer is trying to solve.

**Timing** - is there a date the customer owns?
Confirmed: the customer states a deadline, an event, or a committee
date.
Partial: a timeframe exists but only in the seller's words.
Missing: the close date is the only date, and nothing supports it.

## Stage contradiction

Compare the CRM stage with the evidence, not with the amount.

A stage at or beyond Proposal with a BANT score of 2 or less is a
contradiction. Report it in the rationale. This is the single most
useful thing you can tell a seller.

## Archetypes

Assign exactly one, in this order of precedence.

**Slipping** - the deal is being lost.
A competitor is named in any record in the last 60 days, OR the close
date has moved more than once, OR a previously engaged decision maker
has no activity in 30 days.

**AtRisk** - the deal is not qualified enough for its stage.
Stage is at or beyond Proposal AND BANT score is 2 or less.

**Reconnect** - the deal is going quiet.
BANT score is 3 or more AND no activity in the last 30 days.

**ReadyToClose** - the deal is ripe.
BANT score is 4, OR score is 3 with Timing confirmed, AND activity
volume in the last 30 days is higher than the previous 30.

If none apply, return InsufficientSignal.

## Confidence

High - three or more axes carry evidence from distinct records.
Medium - two axes carry evidence, or several cite the same record.
InsufficientSignal - fewer than two axes documented.

## Worked examples

See references/examples.md for four assessments, including one that
correctly refuses to conclude.
