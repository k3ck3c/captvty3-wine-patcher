# Installation

## Dépendances du patcheur

Sous Debian ou Ubuntu :

sudo apt update
sudo apt install mono-devel libmono-cecil-private-cil

## Compilation

scripts/build.sh

## Application

scripts/patch.sh \
  /chemin/vers/Captvty.exe \
  /chemin/vers/Captvty-wine.exe

Le fichier d'entrée doit être l'exécutable original de Captvty 3.0.1.24.

## Exécution

Le fichier patché peut être utilisé avec :

- Wine natif
- Docker
- Docker Compose

