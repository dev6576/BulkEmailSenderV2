#!/usr/bin/env sh
set -eu
ROOT="$(CDPATH= cd -- "$(dirname "$0")/.." && pwd)"
cd "$ROOT"
docker compose --env-file deployment/.env -f deployment/docker-compose.yml build
docker compose --env-file deployment/.env -f deployment/docker-compose.yml up -d
docker compose --env-file deployment/.env -f deployment/docker-compose.yml ps
curl --fail --silent --show-error https://mail.example.com/health
printf '\n'
