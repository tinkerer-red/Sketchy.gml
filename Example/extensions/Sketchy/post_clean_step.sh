#!/bin/sh
echo "[Sketchy Hook] executing post_clean_step.sh"
EXT_DIR="$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)/"
PROJ_DIR="${EXT_DIR}../.."
BIN_EXE="${EXT_DIR}Sketchy_bin"

if [ ! -f "$BIN_EXE" ]; then
	echo "[Sketchy Hook] ERROR: Sketchy_bin not found at $BIN_EXE"
	exit 1
fi

chmod +x "$BIN_EXE" >/dev/null 2>&1 || true

echo "[Sketchy Hook] running Sketchy_bin --clean"
"$BIN_EXE" "$PROJ_DIR" --clean
EXITCODE=$?
if [ "$EXITCODE" -ne 0 ]; then
	echo "[Sketchy Hook] ERROR: Sketchy_bin --clean failed with exit code $EXITCODE"
	exit "$EXITCODE"
fi
exit 0
