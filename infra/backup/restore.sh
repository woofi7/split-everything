#!/bin/sh
# Restores a dump written by backup.sh.
#
# Deliberately manual and deliberately loud: it drops and recreates every object
# in the target database, so it asks for the file explicitly rather than guessing
# at "the latest".
set -eu

if [ $# -ne 1 ]; then
    echo "Usage: restore.sh /backups/split_everything-YYYYMMDD-HHMMSS.sql.gz" >&2
    exit 2
fi

DUMP="$1"

if [ ! -f "$DUMP" ]; then
    echo "No such dump: $DUMP" >&2
    exit 1
fi

echo "This will overwrite the contents of ${PGDATABASE} on ${PGHOST}."
printf 'Type the database name to continue: '
read -r confirmation

if [ "$confirmation" != "$PGDATABASE" ]; then
    echo "Aborted." >&2
    exit 1
fi

echo "Stop the api container first, or it will write while the restore runs."
gunzip -c "$DUMP" | psql --set ON_ERROR_STOP=on
echo "Restored ${DUMP}."
