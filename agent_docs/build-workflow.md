# Build workflow

Applies whenever running `/agent-skills:build` or `/agent-skills:review`
against a task, whether standalone or via the feature orchestrator
(`.claude/commands/new-feature-with-agent-skills.md`).

## Build → review → fix, every task

After `/agent-skills:build` implements a task, always run
`/agent-skills:review` against that task's diff before marking it done,
and fix any Critical findings it reports before moving on. Then push the
changes and open a pull request.

## Generation time tracking

`tasks/todo.md` records, per completed task, the total wall-clock time
for build + review + critical-finding fixes combined (not build alone).
Note the start time when beginning a task's `/agent-skills:build` run and
the end time once its review's critical findings are fixed, then record
the elapsed time on that task's checklist line in `tasks/todo.md` when
checking it off.

## No task-number references in code

Never reference `tasks/todo.md`/`tasks/plan.md` task numbers (e.g. "task
9", "tasks 14/17/19") in code comments, commit-adjacent docs (READMEs,
`.tf`/`.yml` comments), or other checked-in content outside `tasks/`
itself. That plan is a planning artifact, not part of the shipped system
— its numbering will drift or the file may not exist at all once the
MVP is done, leaving a dangling reference future engineers can't resolve.
Instead, describe the actual mechanism, file, or resource being referred
to (e.g. "each service's own Terraform" instead of "tasks 10/14/17/19",
"the event consumers" instead of "task 9").
