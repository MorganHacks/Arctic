#!/usr/bin/env bash
#
# The whole stack, on this machine, in one command.
#
# What it starts, and why each one has to be here:
#
#   postgres, azurite, mailpit   docker compose
#   atlas    :5080               the API
#   harbor   :5050               the reverse proxy — NOT optional, see below
#   admin    :3001               the organizer console
#   forms    :3002               the public form
#
# Harbor is the part that surprises people. Both consoles proxy /api/* to
# their own origin, and atlas serves /auth/me, not /api/auth/me — stripping
# that prefix is harbor's job. Point a console straight at atlas and every
# request 404s behind the scenes, which surfaces as an endless redirect to
# the sign-in page with nothing in any log to explain it.
#
# Ctrl+C stops everything it started. The containers are left up, because
# they hold the database and tearing that down on every exit means seeding
# a super admin every morning.
#
# Usage:  deploy/local/dev.sh [email]

set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT"

EMAIL="${1:-${ARCTIC_SUPER_ADMIN_EMAIL:-}}"
LOGS="$ROOT/.local-logs"
mkdir -p "$LOGS"

PIDS=()

say()  { printf '\n\033[1m%s\033[0m\n' "$*"; }
step() { printf '  %-34s' "$*"; }
ok()   { printf '\033[32mok\033[0m\n'; }
fail() { printf '\033[31m%s\033[0m\n' "${1:-failed}"; }

cleanup() {
  printf '\n\nStopping.\n'
  for pid in "${PIDS[@]:-}"; do
    [ -n "${pid:-}" ] && kill "$pid" 2>/dev/null
  done
  wait 2>/dev/null
  printf 'Containers left running. "docker compose down" to stop them too.\n'
}
trap cleanup EXIT INT TERM

# Wait for a URL to answer anything at all, bounded by wall clock rather than
# by a count of iterations — a loop that counts tries is a loop that waits an
# unknown length of time.
wait_for() {
  local url="$1" name="$2" deadline=$((SECONDS + 90))
  while [ $SECONDS -lt $deadline ]; do
    # Any answer counts, including a 404. portalforms has no route at /
    # at all — every page is /<code> — so demanding a 2xx here waits forever
    # on an app that is already up and answering correctly.
    code=$(curl -sS -o /dev/null -w '%{http_code}' --max-time 3 "$url" 2>/dev/null)
    if [ -n "$code" ] && [ "$code" != "000" ]; then return 0; fi
    sleep 1
  done
  fail "no answer in 90s"
  echo "      last lines of $LOGS/$name.log:" >&2
  tail -6 "$LOGS/$name.log" 2>/dev/null | sed 's/^/      /' >&2
  return 1
}

port_busy() { lsof -ti tcp:"$1" >/dev/null 2>&1; }

# ---------------------------------------------------------------- checks ---
say "Checking what is needed"

for tool in docker dotnet npm curl openssl; do
  step "$tool"
  command -v "$tool" >/dev/null 2>&1 && ok || { fail "not installed"; exit 1; }
done

step "docker running"
docker info >/dev/null 2>&1 && ok || { fail "start Docker Desktop"; exit 1; }

for port in 5080 5050 3001 3002; do
  step "port $port free"
  if port_busy "$port"; then
    fail "in use"
    echo "      something is already on $port. Stop it, or:  lsof -ti tcp:$port | xargs kill" >&2
    exit 1
  fi
  ok
done

# ------------------------------------------------------------ containers ---
say "Starting containers"
step "docker compose up"
docker compose up -d >"$LOGS/compose.log" 2>&1 && ok || { fail; tail -8 "$LOGS/compose.log"; exit 1; }

step "postgres accepting connections"
deadline=$((SECONDS + 60))
until docker compose exec -T postgres pg_isready -U arctic -d morganhacks >/dev/null 2>&1; do
  [ $SECONDS -ge $deadline ] && { fail "not ready in 60s"; exit 1; }
  sleep 1
done
ok

# ------------------------------------------------------------ migrations ---
say "Database"
step "migrations"
if [ -n "$EMAIL" ]; then
  (cd src/atlas && ARCTIC_SUPER_ADMIN_EMAIL="$EMAIL" dotnet run --project MorganHacks.Migrations) \
    >"$LOGS/migrations.log" 2>&1 && ok || { fail; tail -12 "$LOGS/migrations.log"; exit 1; }
else
  (cd src/atlas && dotnet run --project MorganHacks.Migrations) \
    >"$LOGS/migrations.log" 2>&1 && ok || { fail; tail -12 "$LOGS/migrations.log"; exit 1; }
fi

# -------------------------------------------------------------- services ---
say "Starting services"

step "atlas  :5080"
(cd src/atlas && dotnet run --project MorganHacks.Api --urls http://localhost:5080) \
  >"$LOGS/atlas.log" 2>&1 &
PIDS+=($!)
wait_for http://localhost:5080/health atlas && ok || exit 1

step "harbor :5050"
(cd src/harbor && dotnet run --project MorganHacks.Harbor --urls http://localhost:5050) \
  >"$LOGS/harbor.log" 2>&1 &
PIDS+=($!)
wait_for http://localhost:5050/api/health harbor && ok || exit 1

for app in portaladmin:3001 portalforms:3002; do
  name="${app%%:*}"; port="${app##*:}"
  step "$name :$port"
  if [ ! -d "src/$name/node_modules" ]; then
    printf 'installing… '
    (cd "src/$name" && npm install --silent) >"$LOGS/$name-install.log" 2>&1 \
      || { fail "npm install failed"; tail -6 "$LOGS/$name-install.log"; exit 1; }
  fi
  (cd "src/$name" && PORT="$port" API_ORIGIN=http://localhost:5050 npm run dev) \
    >"$LOGS/$name.log" 2>&1 &
  PIDS+=($!)
  wait_for "http://localhost:$port/" "$name" && ok || exit 1
done

# ---------------------------------------------------------------- signin ---
COOKIE=""
if [ -n "$EMAIL" ]; then
  say "Signing you in"
  step "session for $EMAIL"
  COOKIE=$("$ROOT/deploy/local/sign-in.sh" "$EMAIL" 2>/dev/null \
    | grep -oE 'mh_session=[A-Za-z0-9_-]+' || true)
  [ -n "$COOKIE" ] && ok || fail "could not mint one"
fi

# ----------------------------------------------------------------- ready ---
cat <<EOF

  Organizer console   http://localhost:3001
  Public form         http://localhost:3002/<code>
  API (through YARP)  http://localhost:5050/api
  Mail                http://localhost:8025

EOF

if [ -n "$COOKIE" ]; then
  cat <<EOF
  Organizer sign-in is Google only, so to get in locally open the console on
  http://localhost:3001, and paste this into the browser console:

    document.cookie = "${COOKIE}; path=/"

  Then reload. Good for 7 days.

EOF
else
  cat <<EOF
  No email given, so nobody was signed in. To sign in:

    deploy/local/dev.sh you@morgan.edu

EOF
fi

echo "  Logs in .local-logs/. Ctrl+C stops everything."
echo

wait
