#!/usr/bin/env bash
set -Eeuo pipefail

ROOT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
API_DIR="$ROOT_DIR/BulkEmailSender/src/BulkEmailSender.Api"
API_PROJECT="$API_DIR/BulkEmailSender.Api.csproj"
WEB_DIR="$ROOT_DIR/BulkEmailSenderFrontend"
LOG_DIR="$(mktemp -d "${TMPDIR:-/tmp}/bulk-email-sender.XXXXXX")"
API_LOG="$LOG_DIR/api.log"
WEB_LOG="$LOG_DIR/frontend.log"
API_PID=""
WEB_PID=""

cleanup() {
  local status=$?
  trap - EXIT INT TERM
  if [[ -n "$WEB_PID" ]] && kill -0 "$WEB_PID" 2>/dev/null; then
    kill "$WEB_PID" 2>/dev/null || true
  fi
  if [[ -n "$API_PID" ]] && kill -0 "$API_PID" 2>/dev/null; then
    kill "$API_PID" 2>/dev/null || true
  fi
  if [[ -n "$WEB_PID" || -n "$API_PID" ]]; then
    wait "$WEB_PID" "$API_PID" 2>/dev/null || true
  fi
  if [[ $status -ne 0 ]]; then
    echo "Startup failed. Logs are in: $LOG_DIR" >&2
  fi
  exit "$status"
}
trap cleanup EXIT INT TERM

fail() {
  echo "ERROR: $*" >&2
  exit 1
}

has_setting() {
  local key="$1"
  local env_name="$2"
  local env_value="${!env_name-}"
  local secrets=""
  local file_value=""

  if [[ -n "$env_value" ]]; then
    return 0
  fi

  if secrets="$(dotnet user-secrets list --project "$API_PROJECT" 2>/dev/null)"; then
    if printf '%s\n' "$secrets" | grep -Eq "^${key} = .+"; then
      return 0
    fi
  fi

  # Also recognize a locally configured appsettings file. Prefer environment
  # variables or .NET User Secrets for credentials; do not commit secrets.
  if [[ -f "$API_DIR/appsettings.json" ]] && command -v grep >/dev/null 2>&1; then
    file_value="$(grep -E '"ClientId"[[:space:]]*:[[:space:]]*"[^"[:space:]]+' "$API_DIR/appsettings.json" 2>/dev/null | head -n 1 || true)"
    if [[ "$key" == "GoogleOAuth:ClientId" && -n "$file_value" ]]; then
      return 0
    fi
    file_value="$(grep -E '"ClientSecret"[[:space:]]*:[[:space:]]*"[^"[:space:]]+' "$API_DIR/appsettings.json" 2>/dev/null | head -n 1 || true)"
    if [[ "$key" == "GoogleOAuth:ClientSecret" && -n "$file_value" ]]; then
      return 0
    fi
  fi
  return 1
}

echo "Checking local prerequisites..."
command -v dotnet >/dev/null 2>&1 || fail ".NET 8 SDK was not found. Install the .NET 8 SDK and run this script again."
command -v node >/dev/null 2>&1 || fail "Node.js was not found. Install Node.js and run this script again."
command -v npm >/dev/null 2>&1 || fail "npm was not found. Install Node.js with npm and run this script again."
command -v open >/dev/null 2>&1 || fail "The macOS 'open' command was not found. Run this script on macOS."

DOTNET_MAJOR="$(dotnet --version | cut -d. -f1)"
[[ "$DOTNET_MAJOR" == "8" ]] || fail "This project targets .NET 8; detected SDK $(dotnet --version). Install the .NET 8 SDK."

dotnet ef --version >/dev/null 2>&1 || fail "The EF Core command-line tool is missing. Install it with: dotnet tool install --global dotnet-ef --version 8.0.20"

echo "Checking Google OAuth configuration..."
has_setting "GoogleOAuth:ClientId" "GoogleOAuth__ClientId" || fail "Google OAuth ClientId is missing. Configure .NET User Secrets or GoogleOAuth__ClientId. See BulkEmailSender/GOOGLE-OAUTH-SETUP.md."
has_setting "GoogleOAuth:ClientSecret" "GoogleOAuth__ClientSecret" || fail "Google OAuth ClientSecret is missing. Configure .NET User Secrets or GoogleOAuth__ClientSecret. See BulkEmailSender/GOOGLE-OAUTH-SETUP.md."

if [[ ! -x "$WEB_DIR/node_modules/.bin/ng" ]]; then
  echo "Installing frontend dependencies..."
  (cd "$WEB_DIR" && npm ci) || fail "npm ci failed."
fi

echo "Applying database migrations..."
(cd "$API_DIR" && dotnet ef database update --project "$API_PROJECT" --startup-project "$API_PROJECT") || fail "Database migration failed. The app was not started."

echo "Starting backend and frontend..."
(cd "$API_DIR" && dotnet run --no-launch-profile --urls http://localhost:5016) >"$API_LOG" 2>&1 &
API_PID=$!
(cd "$WEB_DIR" && npm start) >"$WEB_LOG" 2>&1 &
WEB_PID=$!

ready=0
for _ in $(seq 1 60); do
  if curl --silent --fail http://localhost:4200/ >/dev/null 2>&1 && curl --silent --fail http://localhost:5016/api/auth/status >/dev/null 2>&1; then
    ready=1
    break
  fi
  if ! kill -0 "$API_PID" 2>/dev/null; then
    cat "$API_LOG" >&2
    fail "The API stopped during startup."
  fi
  if ! kill -0 "$WEB_PID" 2>/dev/null; then
    cat "$WEB_LOG" >&2
    fail "The frontend stopped during startup."
  fi
  sleep 1
done

if [[ "$ready" -ne 1 ]]; then
  echo "The app did not become ready in time. Recent logs:" >&2
  tail -n 40 "$API_LOG" "$WEB_LOG" >&2
  fail "Startup timed out."
fi

open "http://localhost:4200"
echo "Bulk Email Sender is running at http://localhost:4200."
echo "Keep this Terminal window open while using the app. Press Ctrl+C to stop it."
echo "Logs: $LOG_DIR"

wait "$API_PID" "$WEB_PID"
