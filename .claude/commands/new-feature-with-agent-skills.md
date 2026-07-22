---
description: Create a new feature using agent-skills. 
---

# new-feature-with-agent-skills

Orchestrates the agent-skills plugin's stage commands into one pipeline for a single feature: `/spec` -> `/plan` -> `/build auto` -> `/review` -> `/ship` -> PR. A human checks in after each stage; the run can continue in the same session or be picked up later by re-invoking this command.

Arguments: `$ARGUMENTS`

## Resuming vs. starting new

1. Run `git status`. If the current directory is inside a feature worktree created by this command (i.e. `tasks/feature-stage.md` exists somewhere up the tree), this is a **resume**. Note: `tasks/` is deleted as the last step of the ship stage once a PR exists (see stage 5), so by design a fully-shipped feature has no `tasks/feature-stage.md` left to resume from — that's expected, not a bug.
   - Read `tasks/feature-stage.md` to find the last completed stage.
   - If every stage is already checked off, tell the human this feature is already shipped and stop — do not re-invoke any stage.
   - Otherwise, if arguments were also passed, don't discard them silently: tell the human this worktree has an in-progress feature at stage X and that you're resuming it instead of starting the new description they gave — they can say so if they meant to start fresh elsewhere.
   - Announce which stage is next and continue from there (go to the matching step below).
2. Otherwise this is a **new feature** and arguments (the feature description) are required. If none were given, ask for one and stop.
   - Derive a kebab-case slug from the description (e.g. "add dark mode toggle" -> `add-dark-mode-toggle`). Reuse this slug for the branch name, worktree directory, and `docs/changes/` filenames.
   - Invoke the `git-workflow-and-versioning` skill to create the feature branch `feature/<slug>` and a worktree at `.claude/worktrees/<slug>` (matching this repo's existing worktree convention).
   - Inside the new worktree, create `tasks/feature-stage.md` with all five stages listed as pending:
     ```markdown
     # Feature stage tracker: <slug>

     - [ ] spec
     - [ ] plan
     - [ ] build
     - [ ] review
     - [ ] ship (+ PR)
     ```

## Stage gate pattern

After **every** stage below:
1. Update `tasks/feature-stage.md`, checking off the completed stage, and commit it.
2. Present a concise summary of what the stage produced.
3. Offer both continuation paths and let the human pick in the moment:
   - **Continue now**: use AskUserQuestion with options approve / revise / stop. On approve, proceed immediately to the next stage in this same turn.
   - **Continue later**: the human can also just end the session here. Say explicitly that re-running `/new-feature-with-agent-skills` from inside this worktree (no arguments needed) will resume from the next pending stage.
4. Never skip this gate, even in a single continuous run.

## Stages

### 1. Spec
Invoke `/agent-skills:spec` (spec-driven-development). Feed it the feature description (or, on resume, whatever context is needed to pick the conversation back up). A brand-new spec is always written to `docs/SPEC.md` under the relevant project root (repo root for a single-project repo, `<subproject>/docs/SPEC.md` in a monorepo) — never a bare `SPEC.md` at the project root.

- Before assuming this is the first spec ever, search the whole repo for existing specs — not just `docs/` (e.g. `find . -iname SPEC.md`). If exactly one exists (under `docs/` or elsewhere), **amend it in place**: read it first, add or update only the sections relevant to this feature, and leave unrelated existing content untouched. Never regenerate the whole file from scratch, and never create a second, disconnected spec elsewhere. If the found spec is **not** under a `docs/` directory, still amend it where it is — don't silently move it — but tell the human it's a pre-existing location drift from the `docs/SPEC.md` convention, so they can decide whether to relocate it separately. If the search finds **more than one** spec (e.g. a monorepo with several subproject specs), confirm with the human which one this feature belongs to before writing, regardless of whether one of them happens to be at root.
- Every write to the spec — including the very first one ever — gets a matching entry in `docs/changes/yyyy-mm-dd-<slug>.md` describing what changed and why. **SPEC.md itself never accumulates this history**: don't append dated `## Change: ...` sections to it. Instead, edit the relevant existing sections (Data Model, Pages, Boundaries, etc.) in place so SPEC.md always reads as the current state, not a log. The changelog narrative belongs only in `docs/changes/*.md`.
- Get explicit human approval of the spec content itself (this is part of the skill's own flow) before checking off this stage.
- **Before moving to stage 3, commit the spec and its `docs/changes/*.md` entry.** `/build auto`'s own clean-baseline check (`git status --porcelain`) does not whitelist `docs/changes/*`, so an uncommitted changelog file would make it stop and ask. Committing here also satisfies `/build auto`'s requirement that planning artifacts not bleed into the first task's commit.

### 2. Plan
Invoke `/agent-skills:plan`.

- **Before moving to stage 3, commit `tasks/plan.md` and `tasks/todo.md`.** `/build auto` only commits `tasks/plan.md` itself when *it* generates the plan inline (its "no plan exists yet" branch) — since this stage already created it, that branch never fires, and the plan would otherwise stay uncommitted through the rest of the pipeline.

### 3. Build
Invoke `/agent-skills:build auto`.

### 4. Review
Invoke `/agent-skills:review`.

### 5. Ship
Invoke `/agent-skills:ship`.

- Once the human is satisfied and the decision is GO, create the PR with `gh pr create` as a normal, ready-for-review PR — do **not** pass `--draft`.
- Once `gh pr create` has succeeded and returned a PR URL, remove the `tasks/` directory (`tasks/feature-stage.md`, `tasks/plan.md`, `tasks/todo.md`), commit that deletion on its own (e.g. `chore: remove tasks folder`), and push. Since the PR already tracks this branch, the push lands in the PR automatically. Do this only *after* the PR URL is confirmed — deleting the resume tracker before the PR exists would strand the feature with no save point if `gh pr create` fails.
- **Stop after this cleanup commit is pushed.** Do not merge the PR — that stays a manual step for the human.

## Blockers

Each invoked command enforces its own stop conditions (e.g. `/build auto` already stops on a failing test, an ambiguous spec, or a high-risk/irreversible change — see that command's own rules). This orchestrator adds one more on top: an unresolved Critical finding from `/review` or `/ship` is always a blocker. On any blocker, stop and ask rather than push through, and leave `tasks/feature-stage.md` reflecting the true state so a later resume picks up correctly.
