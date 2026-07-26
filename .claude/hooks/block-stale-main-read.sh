#!/usr/bin/env bash
# PreToolUse hook (Read): blocks reading tasks/todo.md or tasks/plan.md from
# a local `main` that is behind `origin/main` — catches stale-checkout
# mistakes where task status is read off out-of-date local history instead
# of what's actually merged upstream.
set -euo pipefail

input=$(cat)
file_path=$(printf '%s' "$input" | jq -r '.tool_input.file_path // empty' 2>/dev/null || true)

case "$file_path" in
  */tasks/todo.md|*/tasks/plan.md|tasks/todo.md|tasks/plan.md) ;;
  *) exit 0 ;;
esac

branch=$(git rev-parse --abbrev-ref HEAD 2>/dev/null) || exit 0
case "$branch" in
  main|master) ;;
  *) exit 0 ;;
esac

repo_root=$(git rev-parse --show-toplevel 2>/dev/null) || exit 0
key=$(printf '%s' "$repo_root" | (md5 2>/dev/null || md5sum | cut -d' ' -f1))
cache_file="/tmp/.claude-main-fetch-check-$key"
now=$(date +%s)

do_fetch=1
if [ -f "$cache_file" ]; then
  read -r cached_ts < "$cache_file" 2>/dev/null || true
  if [ -n "${cached_ts:-}" ] && [ $((now - cached_ts)) -lt 300 ]; then
    do_fetch=0
  fi
fi

if [ "$do_fetch" -eq 1 ]; then
  git fetch origin "$branch" --quiet 2>/dev/null || true
  echo "$now" > "$cache_file"
fi

local_head=$(git rev-parse "$branch" 2>/dev/null) || exit 0
remote_head=$(git rev-parse "origin/$branch" 2>/dev/null) || exit 0

if [ "$local_head" = "$remote_head" ]; then
  exit 0
fi

behind=$(git rev-list --count "$branch..origin/$branch" 2>/dev/null || echo 0)

if [ "$behind" -gt 0 ]; then
  echo "Blocked: local '$branch' is $behind commit(s) behind 'origin/$branch'." >&2
  echo "Reading $file_path off a stale local main can misreport which tasks are actually done." >&2
  echo "Run: git pull --ff-only origin $branch" >&2
  exit 2
fi

exit 0
