#!/usr/bin/env bash
# Starts the image the way the NAS will and asks it what a browser on the LAN would ask. Run by CI
# before the image is pushed, so a container that is healthy inside and unreachable outside, runs as
# root, keeps its data outside /data or tells the time in UTC never reaches the NAS.
#
#   bash scripts/probe-image.sh <image>
set -euo pipefail

image="$1"
name="todo-probe-$$"
port=18080
base="http://127.0.0.1:$port"
data="$(mktemp -d)"

# The container's user is not the runner's, so the folder standing in for the NAS's volume has to be
# writable by anybody - on the NAS the compose file's user: owns it instead.
chmod 777 "$data"

# The password "publish-probe" with one iteration, the same throwaway hash publish.ps1 uses: it
# protects nothing, because this container lives for a minute and only this script talks to it.
hash='pbkdf2-sha256$1$AAECAwQFBgcICQoLDA0ODw==$+OuWg3PolMxZE+0j6HpDErvcym7/av5Pp6NpLGZ7uCI='

cleanup() {
  echo '--- container log ---'
  docker logs "$name" 2>&1 | tail -40 || true
  docker rm -f "$name" >/dev/null 2>&1 || true
  # The files are the container user's, so the runner may need sudo to take them away.
  rm -rf "$data" 2>/dev/null || sudo rm -rf "$data"
}
trap cleanup EXIT

# A user the image has no account for, the way the NAS runs it: the compose file's user: is the NAS
# user that owns the data folder, and nothing in the image knows that number. The image's own user
# would only measure a path the NAS never takes.
docker run -d --name "$name" --user 4321:4321 -p "$port:8080" -v "$data:/data" \
  -e "Auth__PasswordHash=$hash" "$image" >/dev/null

status() { curl -s -o /dev/null -w '%{http_code}' "$@" || true; }

ready=''
for _ in $(seq 1 60); do
  if [ "$(status "$base/api/health")" = 200 ]; then ready=yes; break; fi
  sleep 1
done
if [ -z "$ready" ]; then
  echo 'PROBE: /api/health did not answer 200 from outside the container within a minute.' >&2
  exit 1
fi

failures=0
fail() { echo "PROBE: $*" >&2; failures=$((failures + 1)); }

# Not merely a 200: the fallback answers 200 with index.html for anything, so the body is what tells
# a served frontend from a served fallback of nothing.
#
# Read into a variable before it is searched: under pipefail, `curl | grep -q` fails by chance when
# grep stops reading early and curl dies on SIGPIPE.
index="$(curl -s "$base/" || true)"
case "$index" in
  *'<app-root'*) ;;
  *) fail '/ answered without <app-root in the body.' ;;
esac
[ "$(status "$base/scalar/")" = 200 ] || fail '/scalar/ did not answer 200.'

# The login is switched on in the image: without the cookie the API says 401, and with it 200.
[ "$(status "$base/api/tasks")" = 401 ] || fail '/api/tasks answered without a cookie.'

jar="$(mktemp)"
login="$(status -c "$jar" -H 'Content-Type: application/json' -d '{"password":"publish-probe"}' "$base/api/auth/login")"
[ "$login" = 204 ] || fail "the login answered $login, not 204."
[ "$(status -b "$jar" "$base/api/tasks")" = 200 ] || fail '/api/tasks did not answer 200 with the cookie.'
rm -f "$jar"

# Inside the container.
[ "$(docker exec "$name" id -u)" = 4321 ] || fail 'the app does not run as the user it was given.'

zone="$(docker exec "$name" date +%Z)"
case "$zone" in
  CET | CEST) ;;
  *) fail "the container tells the time in $zone, not Danish time - is tzdata in the image?" ;;
esac

# The Jira and ADO status pickers build da-DK's culture, which needs ICU: an image swapped for one in
# invariant mode - Alpine, chiseled - would answer them with 500, and nothing else here would notice.
[ -z "$(docker exec "$name" printenv DOTNET_SYSTEM_GLOBALIZATION_INVARIANT || true)" ] \
  || fail 'globalization runs in invariant mode, so da-DK cannot be built.'
[ "$(docker exec "$name" printenv LANG)" = da_DK.UTF-8 ] || fail 'LANG is not da_DK.UTF-8.'

docker exec "$name" test -f /data/todo.db || fail 'the database is not in /data.'
docker exec "$name" test -d /data/keys || fail 'the keys that encrypt the login cookie are not in /data.'

if [ "$failures" -gt 0 ]; then
  echo "PROBE: $failures claim(s) failed." >&2
  exit 1
fi

echo 'PROBE: the image answers from outside, logs in, runs as a user, tells Danish time and keeps its data in /data.'
