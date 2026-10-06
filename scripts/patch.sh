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

if [ ! -f "$INPUT" ]; then
  echo "Erreur : fichier d'entrée introuvable : $INPUT" >&2
  exit 2
fi

SHA=$(sha256sum "$INPUT" | awk '{print $1}')

SHA_30127=a4fd6bc53c41804225e1760b4fb4bfb339ed06895953339ee1318d7d8276b69c

case "$SHA" in
  "$SHA_30127")
    VERSION="3.0.1.27"
    PATCHER="$ROOT_DIR/patch-captvty30127-wine.exe"
    ;;

  *)
    echo "Erreur : Captvty.exe non reconnu." >&2
    echo "SHA-256 fourni :" >&2
    echo "  $SHA" >&2
    echo >&2
    echo "Ce patcheur attend le Captvty.exe original de Captvty 3.0.1.27 :" >&2
    echo "  $SHA_30127" >&2
    echo >&2
    echo "Aucun fichier n'a été modifié." >&2
    exit 3
    ;;
esac

echo "SHA-256 : $SHA"
echo "Patcheur sélectionné : $VERSION"

if [ ! -f "$PATCHER" ]; then
  echo "Le patcheur n'est pas encore compilé."
  "$ROOT_DIR/scripts/build.sh"
fi

mono "$PATCHER" "$INPUT" "$OUTPUT"
