# Order System MVP — project conventions

## Build → review → fix, every task

After `/agent-skills:build` implements a task, always run
`/agent-skills:review` against that task's diff before marking it done,
and fix any Critical findings it reports before moving on.

## Generation time tracking

`tasks/todo.md` records, per completed task, the total wall-clock time
for build + review + critical-finding fixes combined (not build alone).
Note the start time when beginning a task's `/agent-skills:build` run and
the end time once its review's critical findings are fixed, then record
the elapsed time on that task's checklist line in `tasks/todo.md` when
checking it off.
