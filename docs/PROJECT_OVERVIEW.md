# RelayForge

## What this is

RelayForge is a working demonstration of how a reliable background job system behaves. When an app needs to do something that takes time or has several steps — send a batch of emails, process an uploaded file, run a multi-stage pipeline — that work usually can't just happen in the moment the user clicks a button. It gets handed off to something that runs it in the background, keeps track of whether it succeeded, and knows what to do when a step fails.

RelayForge *is* that background system, built so you can actually watch it work instead of just reading about it. You submit a "job" made up of one or more steps, and RelayForge runs those steps, tracks their progress, and shows you exactly what's happening as it happens.

## The idea behind it

Real-world background jobs aren't simple. Steps depend on each other. Some steps can run at the same time, others have to wait their turn. Things fail — a network blip, a bad input, a service that's temporarily down — and a good system doesn't just give up or hang forever. It retries sensibly, gives up on truly broken work without blocking everything else, and lets a person cancel something that's still running.

RelayForge exists to make all of that visible and understandable, not just correct under the hood.

## What it does

At the center of the app is a guided tour: six short, one-click demonstrations, each showing off one specific behavior a reliable job system needs to get right:

1. **Steps run in order** — when one step depends on another, RelayForge waits for the first to finish before starting the next, instead of racing ahead.
2. **Independent steps run at the same time** — steps that don't depend on each other run in parallel, so the whole job finishes as fast as its slowest branch, not the sum of every step.
3. **A flaky step retries until it works** — if a step fails, RelayForge doesn't give up immediately. It waits a little longer between each attempt (so it's not hammering a struggling system) and tries again, up to a limit.
4. **A hopeless step gets parked, not retried forever** — if a step keeps failing no matter how many chances it gets, RelayForge stops retrying it and sets it aside as a known failure, rather than looping forever.
5. **One broken branch doesn't sink the rest of the job** — if a job has multiple independent parts and only one of them fails, the healthy parts still get credit for succeeding. The job's final state reflects the mix honestly, rather than treating the whole thing as a total failure.
6. **You can cancel a job that's still running** — if a job is taking too long or is no longer needed, you can stop it. Steps that are still in flight get a chance to wind down; steps that haven't started yet are called off entirely.

Each of these six demos submits a real job, and then shows you a live view of it running: which steps have started, which have finished, which failed, and why — so you're watching the actual behavior, not a simulation of it.

Beyond the guided tour, you can also submit your own custom job by hand, browse a history of every job that's ever run, and look up plain-language definitions of the ideas involved (what a "step" is, what "retrying with a growing delay" means, what it means for work to be "delivered at least once," and so on) if any of the terminology is unfamiliar.

## Who it's for

RelayForge is meant to be shown, not just used. It's built so that someone with no background in distributed systems can watch one of the six demos, immediately see what happened and why it matters, and walk away understanding a real engineering concept — without ever needing to read code or documentation to get there.
