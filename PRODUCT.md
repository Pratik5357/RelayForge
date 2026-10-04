# Product

<!-- impeccable:product-schema 1 -->

## Platform

web

## Users
Two audiences, equally weighted (confirmed): (1) a viewer with no distributed-systems background watching RelayForge being demonstrated, who should grasp a concept without reading code or docs; (2) engineers who submit and monitor jobs by hand and need a usable console.

## Product Purpose
A small distributed job scheduler built to be watched while it runs. A user submits a job made of steps with dependencies; RelayForge runs the steps, tracks progress, and shows what is happening and why. Success: a newcomer watches a demo and understands a real reliability concept; an engineer can submit, browse and inspect jobs without friction.

## Positioning
Not a black-box queue: the running system is the explanation. Real jobs, real state, shown live rather than simulated.

## Operating Context
Existing Next.js 16 frontend (frontend/) over a .NET 8 API with SQL Server (Docker). Frontend polls the API. Routes present: `/` (home with scenario cards), `/jobs`, `/jobs/new`, `/jobs/[id]`, `/concepts`. Docs: docs/PROJECT_OVERVIEW.md, docs/PROJECT_SPEC.md.

## Capabilities and Constraints
- Backend is phased; check the plan/README for what is live before surfacing a feature. Retry, dead-letter, cancellation and SignalR may not be wired yet.
- Terminology to preserve: job, step, dependency, retry with growing delay, dead-letter ("parked"), cancel, at-least-once delivery.
- Redesign scope (confirmed): all existing pages plus shared components and tokens. Behavior, routes and copy stay; the visual layer is replaced.

## Evidence on Hand
No testimonials, customers, benchmarks or metrics exist. Do not fabricate any.

## Product Principles
- Explain by showing: state and cause are visible, not hidden behind jargon.
- Plain words first, technical term second.
- Honest state: partial failure, parked work and cancellation are shown as they are.
- Usable as a console and as a demo, without a mode switch.

## Accessibility & Inclusion
No product-specific standard established; state must never rely on color alone.
