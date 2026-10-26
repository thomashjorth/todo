#!/bin/sh
# Pulls the newest image and restarts the app if it changed. Run every five minutes by a scheduled
# task in DSM - in place of Watchtower, which was archived in December 2025 and gets no security
# fixes. `up -d` restarts only a container whose image changed, so a run with nothing new does
# nothing at all.
set -eu

cd "$(dirname "$0")"

docker compose pull --quiet
docker compose up -d --remove-orphans

# Every push leaves the previous image behind, and the NAS's disk is not CI's.
docker image prune -f >/dev/null
