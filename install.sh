#!/usr/bin/env bash
set -euo pipefail

APP_NAME="docker-ps"
SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_PATH="$SCRIPT_DIR/DockerPsTui"

if ! command -v dotnet >/dev/null 2>&1; then
  echo "Error: dotnet CLI is required but was not found in PATH." >&2
  exit 1
fi

if [[ ! -f "$PROJECT_PATH/DockerPsTui.csproj" ]]; then
  echo "Error: project file not found at $PROJECT_PATH/DockerPsTui.csproj" >&2
  exit 1
fi

case "$(uname -m)" in
  x86_64) RUNTIME_ID="linux-x64" ;;
  aarch64|arm64) RUNTIME_ID="linux-arm64" ;;
  *)
    echo "Error: unsupported architecture '$(uname -m)'." >&2
    echo "Supported: x86_64, aarch64/arm64." >&2
    exit 1
    ;;
esac

echo "Publishing $APP_NAME for $RUNTIME_ID..."
dotnet publish "$PROJECT_PATH" \
  -c Release \
  -r "$RUNTIME_ID" \
  --self-contained true \
  /p:PublishSingleFile=true \
  /p:PublishTrimmed=false

SOURCE_BIN="$PROJECT_PATH/bin/Release/net10.0/$RUNTIME_ID/publish/DockerPsTui"
TARGET_BIN="/usr/local/bin/$APP_NAME"

if [[ ! -f "$SOURCE_BIN" ]]; then
  echo "Error: expected published binary not found at $SOURCE_BIN" >&2
  exit 1
fi

if [[ $EUID -eq 0 ]]; then
  install -m 0755 "$SOURCE_BIN" "$TARGET_BIN"
else
  if ! command -v sudo >/dev/null 2>&1; then
    echo "Error: installation to /usr/local/bin requires root privileges (sudo not found)." >&2
    exit 1
  fi
  sudo install -m 0755 "$SOURCE_BIN" "$TARGET_BIN"
fi

echo "Installed: $TARGET_BIN"
echo "Run with: $APP_NAME"
