#!/bin/sh
set -eu

ROOT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
cd "$ROOT_DIR"

if [ "$#" -ne 2 ]; then
  echo "Usage :" >&2
  echo "  $0 <Captvty.exe original> <Captvty.exe patché>" >&2
  exit 2
fi

INPUT=$1
OUTPUT=$2
PATCHER="$ROOT_DIR/patch-captvty30124-wine.exe"

if [ ! -f "$INPUT" ]; then
  echo "Erreur : fichier d'entrée introuvable : $INPUT" >&2
  exit 2
fi

if [ ! -f "$PATCHER" ]; then
  echo "Le patcheur n'est pas encore compilé."
  "$ROOT_DIR/scripts/build.sh"
fi

mono "$PATCHER" "$INPUT" "$OUTPUT"
