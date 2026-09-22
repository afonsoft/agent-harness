#!/usr/bin/env bash
# Fixture tests for check-spec-status.sh — SPEC-20260922-spec-status-enforcement.
# Builds a temp git repo + .specs dir; no network/gh needed (SPEC_CHECK_NO_GH=1).
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
SCRIPT="$ROOT/scripts/check-spec-status.sh"
TMP=$(mktemp -d)
trap 'rm -rf "$TMP"' EXIT
PASS=0; FAIL=0

ok()   { PASS=$((PASS+1)); echo "PASS: $1"; }
bad()  { FAIL=$((FAIL+1)); echo "FAIL: $1"; }
check() { # name expected actual
    if [[ "$2" == "$3" ]]; then ok "$1"; else bad "$1 (expected $2, got $3)"; fi
}
assert_contains()    { if [[ "$2" == *"$1"* ]]; then ok "$3"; else bad "$3 -> $2"; fi; }
assert_not_contains(){ if [[ "$2" != *"$1"* ]]; then ok "$3"; else bad "$3 -> $2"; fi; }

cd "$TMP"
git init -q . && git config user.email t@t && git config user.name t
mkdir -p .specs

# fixture 1: terminal spec — never flagged
cat > .specs/SPEC-99990101-done.md <<'EOF'
| Status | `Done` |
EOF
# fixture 2: non-terminal + ticket-less, referenced by a non-spec commit → stale
cat > .specs/SPEC-99990102-stale.md <<'EOF'
| Status | `Approved` |
EOF
# fixture 3: non-terminal, only its own creation commit mentions it → pending
cat > .specs/SPEC-99990103-pending.md <<'EOF'
| Status | `Approved` |
EOF
# fixture 4: dash format, terminal → never flagged
cat > .specs/SPEC-99990104-dash.md <<'EOF'
- **Status**: Done — delivered
EOF
# fixture 5: malformed → unparseable, not stale
cat > .specs/SPEC-99990105-broken.md <<'EOF'
| Feature | x |
EOF

git add . && git commit -qm "add specs — refs SPEC-99990103-pending"
echo "code" > code.txt && git add code.txt
git commit -qm "feat: deliver SPEC-99990102-stale (#42)"

export SPEC_CHECK_NO_GH=1

rc=0; out=$("$SCRIPT" 2>/dev/null) || rc=$?
check "drift detected" 1 "$rc"
assert_contains "SPEC-99990102-stale" "$out" "stale flagged"
assert_not_contains "99990103-pending" "$out" "pending spec not flagged"
assert_not_contains "99990101-done" "$out" "done spec not flagged"
assert_not_contains "99990104-dash" "$out" "dash-format done not flagged"

err=$("$SCRIPT" 2>&1 >/dev/null) || true
assert_contains "unparseable: SPEC-99990105-broken" "$err" "unparseable reported"
assert_contains "gh unavailable" "$err" "no-gh warning"

"$SCRIPT" --fix >/dev/null 2>&1 || true
assert_contains "Done" "$(cat .specs/SPEC-99990102-stale.md)" "--fix bumps status"

rc=0; "$SCRIPT" >/dev/null 2>&1 || rc=$?
check "clean after fix" 0 "$rc"

rc=0; "$SCRIPT" --specs-dir /nonexistent >/dev/null 2>&1 || rc=$?
check "missing dir -> 2" 2 "$rc"

echo "== $PASS passed, $FAIL failed =="
exit "$FAIL"
