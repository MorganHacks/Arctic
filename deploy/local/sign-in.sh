#!/usr/bin/env bash
#
# A session on the local stack, without Google.
#
# Organizer sign-in is Google and only Google — there is no password to check
# against, deliberately. That is right for a deployed environment and leaves
# local development needing an OAuth client to look at a page, which is a poor
# trade for a team that changes every year.
#
# So this mints a session the way the database sees one: a random token, its
# SHA-256 in identity.sessions, the raw value in a cookie. No code path in
# atlas is involved and nothing here exists in a deployed environment — it
# needs the database credentials, which is the point. This is not an auth
# bypass; it is the same act as an administrator writing a row.
#
# Usage:  deploy/local/sign-in.sh [email]

set -euo pipefail

EMAIL="${1:-${ARCTIC_SUPER_ADMIN_EMAIL:-}}"
if [ -z "$EMAIL" ]; then
  echo "Which person? Pass an email, or set ARCTIC_SUPER_ADMIN_EMAIL." >&2
  exit 1
fi

PSQL=(docker compose exec -T postgres psql -U arctic -d morganhacks -qAt)

PERSON=$("${PSQL[@]}" -c \
  "SELECT id FROM identity.people WHERE lower(email) = lower('${EMAIL}') AND revoked_at IS NULL;" \
  | tr -d '[:space:]')

if [ -z "$PERSON" ]; then
  echo "No active person with that address." >&2
  echo "Seed one first:" >&2
  echo "  cd src/atlas && ARCTIC_SUPER_ADMIN_EMAIL=${EMAIL} dotnet run --project MorganHacks.Migrations" >&2
  exit 1
fi

# Base64url, matching SecureToken.Issue.
TOKEN=$(openssl rand 32 | base64 | tr '+/' '-_' | tr -d '=\n')
HASH=$(printf '%s' "$TOKEN" | openssl dgst -sha256 -binary | od -An -tx1 | tr -d ' \n')

"${PSQL[@]}" -c "INSERT INTO identity.sessions (person_id, token_hash, expires_at, user_agent)
  VALUES ('${PERSON}', decode('${HASH}', 'hex'), now() + interval '7 days', 'sign-in.sh');" >/dev/null

cat <<EOF

  Signed in as ${EMAIL} for 7 days.

  Open the console on http://localhost:3001 (or 3000, or 3002) and paste:

    document.cookie = "mh_session=${TOKEN}; path=/"

  Then reload.

EOF
