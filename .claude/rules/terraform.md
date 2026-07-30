---
paths:
  - "infra/**"
  - "services/*/infra/**"
---

# Terraform conventions

- Apply order: bootstrap → shared → per-service. See
  [README.md](../../README.md#setup) for the full sequence.
- Service Bus RBAC is scoped per-topic, never namespace-wide: grant a
  service's managed identity `Azure Service Bus Data Sender` only on the
  topics it publishes (in that service's own Terraform), and
  `Azure Service Bus Data Receiver` only on the subscription it consumes
  (granted in the Terraform of whichever service owns that topic). Never
  use `Azure Service Bus Data Owner` — with no central orchestrator,
  topic-level RBAC is the only boundary preventing one compromised
  service from forging or destroying another's events.
