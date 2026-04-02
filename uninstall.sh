#!/usr/bin/env bash
set -euo pipefail

APP_NAME="docker-ps"
TARGET_BIN="/usr/local/bin/$APP_NAME"

if [[ ! -e "$TARGET_BIN" ]]; then
  echo "Nothing to remove: $TARGET_BIN does not exist."
  exit 0
fi

if [[ $EUID -eq 0 ]]; then
  rm -f "$TARGET_BIN"
else
  if ! command -v sudo >/dev/null 2>&1; then
    echo "Error: removing $TARGET_BIN requires root privileges (sudo not found)." >&2
    exit 1
  fi
  sudo rm -f "$TARGET_BIN"
fi

echo "Removed: $TARGET_BIN"
