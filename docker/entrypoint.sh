#!/bin/sh
set -eu

CAPTVTY_DIR=/opt/captvty
CAPTVTY_EXE="$CAPTVTY_DIR/Captvty.exe"

if [ ! -f "$CAPTVTY_EXE" ]; then
  echo "Erreur : Captvty.exe n'est pas préparé." >&2
  echo "Exécutez d'abord :" >&2
  echo "  scripts/prepare.sh /chemin/vers/le/dossier-Captvty" >&2
  exit 1
fi

cd "$CAPTVTY_DIR"
exec env WINEDEBUG="${WINEDEBUG:--all}" wine Captvty.exe
