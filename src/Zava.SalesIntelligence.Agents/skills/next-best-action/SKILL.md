---
name: next-best-action
description: Turn a scored sales opportunity into up to three prepared actions, each tied to a specific signal and written from the evidence. Use after an opportunity has been assessed against BANT.
license: MIT
metadata:
  author: zava-sales-intelligence
  version: "1.0"
---

# Next best action

## Principle

Each action closes one gap the assessment found. If you cannot name the signal
that triggers an action, do not propose it. Three actions at most. If the
assessment is InsufficientSignal, propose nothing.

## Action types

**Task** - a call or a piece of follow up the seller owns. Use for re-engaging a
quiet contact, chasing paperwork, or gathering a missing piece of qualification.

**Appointment** - a meeting to book with a named person, when the next step needs
a conversation rather than a message.

**StageUpdate** - correct the CRM stage when the evidence contradicts it. Use when
the stage is ahead of the qualification, not to advance a deal.

**CloseDateUpdate** - move the close date when the current one is unsupported by any
customer-owned date.

## By archetype

**Slipping** - act on the loss signal first. Re-engage the decision maker, or book
a meeting to address the named competitor. Do not advance the stage.

**AtRisk** - the stage is ahead of the evidence. Prepare a StageUpdate to reflect
reality, and a Task to find the missing economic buyer or budget.

**Reconnect** - the deal is qualified but quiet. Prepare a Task or Appointment to
re-engage, anchored to the most recent evidence.

**ReadyToClose** - move toward signature. A Task to send paperwork, or an
Appointment to confirm terms, tied to the customer-owned date.

## The body

Written from the evidence, never from a template. Name the record, the date, and
the person the signal came from. A seller should recognise the deal from the body
alone.

## Missing axes

Before preparing an action that rests on an axis the Thinker marked Missing,
challenge the assessment once and ask which evidence ruled it out. Do not build an
action on an absence you have not questioned.

## Approval

Every action is a proposal. Set RequiresApproval to true. Nothing is written
without the seller's approval.
