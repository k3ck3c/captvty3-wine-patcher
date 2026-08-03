#!/bin/sh
set -eu

ROOT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)

if [ "$#" -ne 1 ]; then
  echo "Usage : $0 /chemin/vers/le/dossier-Captvty" >&2
  exit 2
fi

SOURCE_DIR=$(realpath "$1")
RUNTIME_DIR="$ROOT_DIR/runtime"
ORIGINAL_EXE="$SOURCE_DIR/Captvty.exe"
PATCHED_EXE="$RUNTIME_DIR/Captvty.exe"

if [ ! -f "$ORIGINAL_EXE" ]; then
  echo "Erreur : Captvty.exe introuvable dans :" >&2
  echo "  $SOURCE_DIR" >&2
  exit 1
fi

echo "[1/4] Compilation du patcheur"
"$ROOT_DIR/scripts/build.sh"

echo "[2/4] Préparation du répertoire d'exécution"
find "$RUNTIME_DIR" -mindepth 1 ! -name .gitkeep -exec rm -rf {} +
cp -a "$SOURCE_DIR"/. "$RUNTIME_DIR"/

echo "[3/4] Application du patch"
"$ROOT_DIR/scripts/patch.sh" \
  "$ORIGINAL_EXE" \
  "$PATCHED_EXE.tmp"

mv -f "$PATCHED_EXE.tmp" "$PATCHED_EXE"

echo "[4/4] Vérification"
test -f "$PATCHED_EXE"

echo
echo "Captvty patché et prêt dans :"
echo "  $RUNTIME_DIR"
echo
echo "Lancement Docker Compose :"
echo "  docker compose up"
