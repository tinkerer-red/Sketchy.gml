#!/usr/bin/env sh
set -eu

printf '%s\n' '[Sketchy Root Hook] executing pre_project_step.sh'

PROJ_DIR="$(cd "$(dirname "$0")" && pwd)"
BIN_PATH="$PROJ_DIR/extensions/Sketchy/Sketchy_bin"

printf '%s\n' "[Sketchy Root Hook] proj_dir=$PROJ_DIR"
printf '%s\n' "[Sketchy Root Hook] bin=$BIN_PATH"

if [ ! -f "$BIN_PATH" ]; then
	printf '%s\n' 'Sketchy ERROR: Sketchy_bin not found.'
	printf '%s\n' "Sketchy ERROR: Expected at: $BIN_PATH"
	exit 1
fi

chmod +x "$BIN_PATH" 2>/dev/null || true
"$BIN_PATH" "$PROJ_DIR" --pre
