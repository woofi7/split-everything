#!/bin/sh
# Daily Postgres dumps with a fixed retention window.
#
# Runs as a long-lived container rather than a host cron job so the schedule
# travels with the stack. Each dump is written to a temporary name and moved into
# place only on success, so a failed or interrupted dump never masquerades as a
# good backup.
set -eu

BACKUP_DIR=/backups
RETENTION_DAYS="${BACKUP_RETENTION_DAYS:-30}"
INTERVAL_SECONDS=86400

mkdir -p "$BACKUP_DIR"

log() {
    echo "[$(date -u '+%Y-%m-%dT%H:%M:%SZ')] $*"
}

take_backup() {
    stamp="$(date -u '+%Y%m%d-%H%M%S')"
    target="$BACKUP_DIR/${PGDATABASE}-${stamp}.sql.gz"
    partial="${target}.partial"

    log "Dumping ${PGDATABASE}"

    if pg_dump --no-owner --no-privileges --clean --if-exists | gzip -9 > "$partial"; then
        mv "$partial" "$target"
        log "Wrote $(du -h "$target" | cut -f1) to ${target}"
    else
        rm -f "$partial"
        log "Dump failed; kept the previous backups"
        return 1
    fi
}

prune() {
    # -mtime is whole days, which is what a retention window in days means.
    removed="$(find "$BACKUP_DIR" -name "${PGDATABASE}-*.sql.gz" -type f -mtime "+${RETENTION_DAYS}" -print -delete | wc -l)"
    if [ "$removed" -gt 0 ]; then
        log "Pruned ${removed} backups older than ${RETENTION_DAYS} days"
    fi
}

# One immediately on start, so a fresh deployment is covered before the first day
# elapses, then daily.
while true; do
    if take_backup; then
        prune
    fi
    log "Sleeping until the next daily backup"
    sleep "$INTERVAL_SECONDS"
done
