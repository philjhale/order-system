#!/usr/bin/env bash
# SessionStart hook: shell hooks can't prompt the user interactively, so
# on a non-main branch this doesn't switch itself — it surfaces the
# situation via additionalContext so Claude asks the user in chat and
# performs the switch + pull only if they say yes. When already on
# main/master, fetches and fast-forwards local automatically if it's
# behind origin (skipped, with a note, if the tree is dirty or history
# has diverged, since neither is safe to resolve non-interactively).
set -euo pipefail

branch=$(git rev-parse --abbrev-ref HEAD 2>/dev/null) || exit 0

git fetch --quiet 2>/dev/null || true

case "$branch" in
  main|master)
    local_head=$(git rev-parse "$branch" 2>/dev/null) || exit 0
    remote_head=$(git rev-parse "origin/$branch" 2>/dev/null) || exit 0
    [ "$local_head" = "$remote_head" ] && exit 0

    behind=$(git rev-list --count "$branch..origin/$branch" 2>/dev/null || echo 0)
    [ "$behind" -eq 0 ] && exit 0

    if [ -n "$(git status --porcelain 2>/dev/null)" ]; then
      msg="Local '$branch' is $behind commit(s) behind origin, but the working tree has uncommitted changes, so it was not pulled automatically. Mention this to the user."
      jq -n --arg msg "$msg" '{hookSpecificOutput: {hookEventName: "SessionStart", additionalContext: $msg}}'
      exit 0
    fi

    if git merge --ff-only --quiet "origin/$branch" 2>/dev/null; then
      msg="Local '$branch' was $behind commit(s) behind origin and has been fast-forwarded automatically."
      jq -n --arg sysmsg "Pulled $behind new commit(s) into local '$branch'." --arg msg "$msg" \
        '{systemMessage: $sysmsg, hookSpecificOutput: {hookEventName: "SessionStart", additionalContext: $msg}}'
    else
      msg="Local '$branch' has diverged from origin/$branch and could not be fast-forwarded automatically. Mention this to the user."
      jq -n --arg msg "$msg" '{hookSpecificOutput: {hookEventName: "SessionStart", additionalContext: $msg}}'
    fi
    exit 0
    ;;
  HEAD)
    exit 0
    ;;
esac

default_branch="main"
git rev-parse --verify main >/dev/null 2>&1 || default_branch="master"

note=""
if [ -n "$(git status --porcelain 2>/dev/null)" ]; then
  note=" Note: the working tree has uncommitted changes — mention that before switching, since it may need to be stashed or committed first."
fi

msg="Session started on branch '$branch', not '$default_branch'. Ask the user whether they want to switch to '$default_branch' and pull the latest changes; only do it if they say yes.$note"

jq -n --arg msg "$msg" \
  '{hookSpecificOutput: {hookEventName: "SessionStart", additionalContext: $msg}}'

exit 0
