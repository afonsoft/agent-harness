#!/usr/bin/env bash
# check-spec-status.sh — detect drift between a SPEC's declared Status and
# delivery evidence (closed ticket Issue or merged PR/commit referencing it).
#
# SPEC-20260922-spec-status-enforcement RF-001/RF-002.
#
# Exit codes: 0 = clean | 1 = drift found (stale specs listed) | 2 = env error.
# Usage: check-spec-status.sh [--fix] [--specs-dir DIR]
#   --fix         rewrite stale Status lines to `Done` — only when merged-PR
#                 evidence exists; issue-closed-only flags are listed for
#                 human review and never auto-rewritten.
#   --specs-dir   spec directory (default: .specs at repo root / cwd).
#
# Detection:
#   terminal   := Done | Implemented | Completed | Deprecated | Superseded |
#                 Merged | Closed | Cancelled
#   stale      := non-terminal status AND (ticket Issue CLOSED via gh
#                 OR git log on main references the spec id / ticket number)
# Env: SPEC_CHECK_NO_GH=1 forces git-only detection (as if gh were absent).

set -euo pipefail

FIX=false
SPECS_DIR=".specs"
TERMINAL_RE='^(Done|Implemented|Completed|Deprecated|Superseded|Merged|Closed|Cancelled)'
NONTERMINAL_RE='^(Draft|Approved|In[[:space:]]+implementation|In[[:space:]]+progress)'

usage() {
    sed -n '2,20p' "$0" | sed 's/^# \{0,1\}//'
    exit "${1:-0}"
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        --fix) FIX=true; shift ;;
        --specs-dir)
            [[ $# -ge 2 ]] || usage 2
            SPECS_DIR="$2"; shift 2 ;;
        -h|--help) usage 0 ;;
        *) echo "unknown arg: $1" >&2; usage 2 ;;
    esac
done

[[ -d "$SPECS_DIR" ]] || { echo "error: specs dir '$SPECS_DIR' not found" >&2; exit 2; }
command -v git >/dev/null 2>&1 || { echo "error: git not found" >&2; exit 2; }

HAVE_GH=false
if [[ "${SPEC_CHECK_NO_GH:-0}" != "1" ]] && command -v gh >/dev/null 2>&1; then
    HAVE_GH=true
else
    echo "warn: gh unavailable — git-log detection only" >&2
fi

# extract_status FILE → prints normalized status value (empty when unparseable)
extract_status() {
    local file="$1" line
    line=$(grep -m1 -iE '^[[:space:]]*(\|[[:space:]]*Status[[:space:]]*\||-[[:space:]]*(\*\*)?Status(\*\*)?:|(\*\*)?Status(\*\*)?:)' "$file" || true)
    [[ -n "$line" ]] || return 0
    sed -E '
        s/^[[:space:]]*\|[[:space:]]*Status[[:space:]]*\|[[:space:]]*//I
        s/^[[:space:]]*-?[[:space:]]*(\*\*)?Status(\*\*)?:[[:space:]]*//I
        s/\|[[:space:]]*$//
        s/`//g
        s/\*\*//g
        s/^[[:space:]]+//; s/[[:space:]]+$//
    ' <<<"$line"
}

# extract_issue FILE → first #NNN / issues/NNN in the Ticket row
extract_issue() {
    grep -m1 -iE 'Ticket' "$1" | grep -oE '#[0-9]+|issues/[0-9]+' | head -1 | grep -oE '[0-9]+' || true
}

# issue_closed NUM → 0 when the issue state is CLOSED (gh only)
issue_closed() {
    $HAVE_GH || return 1
    [[ "$(gh issue view "$1" --json state --jq '.state' 2>/dev/null || true)" == "CLOSED" ]]
}

# closing_pr NUM → PR number that closed the issue (empty when unknown)
closing_pr() {
    $HAVE_GH || return 1
    gh issue view "$1" --json closedByPullRequestsReferences \
        --jq '.closedByPullRequestsReferences[0].number // empty' 2>/dev/null || true
}

# evidence_commits SPEC_STEM ISSUE_NUM SPEC_FILE → prints commits that
# reference the spec or ticket WITHOUT touching the spec file itself.
# Commits that modify the spec (creation, status bumps) are never evidence.
evidence_commits() {
    local stem="$1" issue="${2:-}" file="$3" c names
    local commits
    commits=$(
        git log --format='%H' --grep="$stem" 2>/dev/null
        [[ -n "$issue" ]] && git log -E --format='%H' --grep="#${issue}([^0-9]|$)" 2>/dev/null
        true
    )
    while IFS= read -r c; do
        [[ -n "$c" ]] || continue
        names=$(git show --name-only --format= "$c" 2>/dev/null || true)
        printf '%s\n' "$names" | grep -qxF "$file" || printf '%s\n' "$c"
    done <<<"$commits"
}

# pr_from_evidence COMMIT_LIST ISSUE_NUM → first "(#N)" token on an evidence
# subject. With an issue, only subjects containing "(#issue)" qualify and the
# issue token itself is excluded (squash merges look like "… (#issue) (#pr)").
pr_from_evidence() {
    local issue="${2:-}" c subj prn=""
    while IFS= read -r c; do
        [[ -n "$c" ]] || continue
        subj=$(git log --format='%s' -n1 "$c" 2>/dev/null || true)
        if [[ -n "$issue" ]]; then
            [[ "$subj" == *"(#$issue)"* ]] || continue
            prn=$(grep -oE '\(#[0-9]+\)' <<<"$subj" | tr -d '(#)' | grep -vxF "$issue" | head -1 || true)
        else
            prn=$(grep -oE '\(#[0-9]+\)' <<<"$subj" | tr -d '(#)' | head -1 || true)
        fi
        [[ -n "$prn" ]] && { printf '%s' "$prn"; return 0; }
    done <<<"$1"
    return 0
}

# rewrite_status FILE PR_REF — bump the Status line to Done in place
rewrite_status() {
    local file="$1" ref="$2" tmp
    tmp=$(mktemp)
    awk -v ref="$ref" '
        done == 0 && $0 ~ /^[[:space:]]*\|[[:space:]]*Status[[:space:]]*\|/ {
            sub(/\|[[:space:]]*Status[[:space:]]*\|[^|]*\|/, "| Status | `Done" ref "` |"); done=1; print; next
        }
        done == 0 && $0 ~ /^[[:space:]]*-?[[:space:]]*\*\*Status\*\*:/ {
            sub(/\*\*Status\*\*:.*/, "**Status**: Done" ref); done=1; print; next
        }
        { print }
    ' "$file" >"$tmp" && mv "$tmp" "$file"
}

stale=()
unparseable=()
shopt -s nullglob
for spec in "$SPECS_DIR"/SPEC-*.md; do
    base=$(basename "$spec" .md)
    status=$(extract_status "$spec")
    if [[ -z "$status" ]]; then
        unparseable+=("$base")
        continue
    fi
    if [[ "$status" =~ $TERMINAL_RE ]]; then
        continue
    fi
    if [[ ! "$status" =~ $NONTERMINAL_RE ]]; then
        unparseable+=("$base ($status)")
        continue
    fi

    issue=$(extract_issue "$spec" || true)
    reason=""
    pr_ref=""
    if [[ -n "$issue" ]] && issue_closed "$issue"; then
        reason="issue #${issue} closed"
        pr=$(closing_pr "$issue" || true)
        [[ -n "$pr" ]] && pr_ref=" — delivered in PR #${pr}"
    fi
    ev_list=$(evidence_commits "$base" "$issue" "$spec" || true)
    if [[ -n "$ev_list" ]]; then
        reason="${reason:+$reason; }merged evidence in git log"
        if [[ -z "$pr_ref" ]]; then
            prn=$(pr_from_evidence "$ev_list" "$issue" || true)
            [[ -n "$prn" ]] && pr_ref=" — delivered in PR #${prn}"
        fi
    fi
    [[ -n "$reason" ]] || continue   # non-terminal without delivery evidence → pending, not stale

    stale+=("$base|$reason|$pr_ref")
done

for u in "${unparseable[@]:-}"; do
    [[ -n "$u" ]] && echo "unparseable: $u" >&2
done

if [[ ${#stale[@]} -eq 0 ]]; then
    echo "OK: no spec status drift"
    exit 0
fi

for entry in "${stale[@]}"; do
    IFS='|' read -r base reason pr_ref <<<"$entry"
    echo "STALE: $base — $reason"
    if $FIX; then
        if [[ -n "$pr_ref" ]]; then
            rewrite_status "$SPECS_DIR/$base.md" "$pr_ref"
            echo "  fixed: Status → Done$pr_ref"
        else
            echo "  skipped: no merged-PR evidence — manual review required"
        fi
    fi
done
exit 1
