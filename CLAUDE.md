# Order System

Demo/learning project: choreography-based, event-driven e-commerce order
system (Order, Inventory, Payment, Fulfillment services). Not production —
scoped as an MVP to demonstrate correct inventory/payment consistency
without a saga orchestrator. Full spec: `docs/SPEC.md` (read it before
making design changes — it's the source of truth, not this file).

## Project state

Pre-implementation: only the spec exists. No source code, no build/test
commands yet. When code is added, update this file's Commands section.

## Tech stack (pinned in docs/SPEC.md's Tech Stack section)

- C# / .NET 10 (LTS), one service per: Order, Inventory, Payment, Fulfillment
- Azure Container Apps, Azure Service Bus (topic per event type, sessioned
  by `orderId`), Azure SQL Database (Serverless, one DB per service)
- Terraform for IaC, GitHub Actions for CI/CD

## Workflow

This repo drives feature work through the `agent-skills` plugin pipeline:
`/spec -> /plan -> /build auto -> /review -> /ship`, orchestrated by
`.claude/commands/new-feature-with-agent-skills.md`. Each feature gets its
own worktree at `.claude/worktrees/<slug>` on branch `feature/<slug>`.

- `docs/SPEC.md` is amended in place, never regenerated wholesale. Every
  spec change gets a matching entry in `docs/changes/yyyy-mm-dd-<slug>.md`;
  SPEC.md itself never accumulates a change log.
- Sections above "Tech Stack" in SPEC.md are deliberately tech-agnostic —
  don't leak concrete technology into them.

## Boundaries (from SPEC.md)

- **Ask first:** adding a saga/orchestrator, locking or TTL-based
  reservations, services beyond the four listed, changing the pinned tech
  stack, adding customer-initiated cancellation or refund handling.
- **Never:** mark an order `CONFIRMED` without a successful payment event;
  decrement inventory outside the `OrderCreated`-triggered reservation
  step; invent order states beyond those in SPEC.md's state machine.
- Every event consumer must be idempotent; every state transition must be
  caused by exactly one recorded event.
