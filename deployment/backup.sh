#!/usr/bin/env sh
set -eu
ROOT="$(CDPATH= cd -- "$(dirname "$0")/.." && pwd)"
DB="$ROOT/deployment/data/bulk-email-sender.db"
OUTDIR="$ROOT/deployment/backups"
[ -f "$DB" ] || { echo "SQLite database not found: $DB" >&2; exit 1; }
mkdir -p "$OUTDIR"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
OUT="$OUTDIR/bulk-email-sender-$STAMP.db"
sqlite3 "$DB" ".backup '$OUT'"
chmod 600 "$OUT"
echo "Created consistent SQLite backup: $OUT"
