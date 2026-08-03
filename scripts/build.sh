#!/bin/sh
set -eu

ROOT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
cd "$ROOT_DIR"

CECIL_DLL=$(
  find /usr/lib/mono/gac/Mono.Cecil \
    -name Mono.Cecil.dll 2>/dev/null |
  sort -V |
  tail -1
)

if [ -z "$CECIL_DLL" ]; then
  echo "Erreur : Mono.Cecil.dll introuvable." >&2
  echo "Sous Debian/Ubuntu, installez :" >&2
  echo "  sudo apt install mono-devel libmono-cecil-private-cil" >&2
  exit 1
fi

echo "Utilisation de Mono.Cecil : $CECIL_DLL"

mcs \
  -r:"$CECIL_DLL" \
  -r:System.Drawing \
  -out:patch-captvty30124-wine.exe \
  src/patch-captvty30124-wine.cs

echo
echo "Patcheur construit :"
echo "  $ROOT_DIR/patch-captvty30124-wine.exe"
