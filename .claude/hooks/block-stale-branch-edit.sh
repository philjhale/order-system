#!/usr/bin/env bash
# PreToolUse hook (Edit|Write|NotebookEdit): blocks edits when the current
# branch's PR is already MERGED — catches stale worktree checkouts left
# over from a finished, already-shipped task before new commits land on them.
set -euo pipefail

branch=$(git rev-parse --abbrev-ref HEAD 2>/dev/null) || exit 0
case "$branch" in
  main|master|HEAD) exit 0 ;;
esac

repo_root=$(git rev-parse --show-toplevel 2>/dev/null) || exit 0
key=$(printf '%s' "$repo_root" | (md5 2>/dev/null || md5sum | cut -d' ' -f1))
cache_file="/tmp/.claude-branch-merge-check-$key"
now=$(date +%s)

state=""
if [ -f "$cache_file" ]; then
  read -r cached_branch cached_state cached_ts < "$cache_file" 2>/dev/null || true
  if [ "${cached_branch:-}" = "$branch" ] && [ -n "${cached_ts:-}" ] && [ $((now - cached_ts)) -lt 300 ]; then
    state="$cached_state"
  fi
fi

if [ -z "$state" ]; then
  state=$(gh pr view "$branch" --json state -q .state 2>/dev/null || echo "NONE")
  echo "$branch $state $now" > "$cache_file"
fi

if [ "$state" = "MERGED" ]; then
  echo "Blocked: branch '$branch' already has a MERGED pull request." >&2
  echo "This looks like a stale worktree checkout left over from a finished task, not a fresh branch for new work." >&2
  echo "Create a new branch off main first, e.g.: git checkout main && git pull && git checkout -b <new-branch-name>" >&2
  exit 2
fi

exit 0
