# Optimisation de l'image Docker Captvty 3 sous Wine

**État : 9 octobre 2026 — image `no-netcache` validée**
**Projet :** `captvty3-wine-patcher`
**Plateforme :** Debian 13, Wine Staging 11.16, nouveau WoW64, .NET Framework 4.8

## 1. Objectif et principes

Faire fonctionner Captvty 3 (application Windows .NET 32 bits) dans un conteneur Docker Linux tout en réduisant la taille de l'image **sans dégrader** l'interface, le téléchargement ou les fichiers multimédias produits.

Principes suivis :

- Conserver une image de référence fonctionnelle à chaque étape.
- Ne modifier qu'un groupe de dépendances à la fois.
- Mesurer les tailles réelles avec `docker image inspect`, plutôt que de se fier aux seules tailles affichées par `du`.
- Tester Captvty et un téléchargement après chaque changement significatif.
- Ne pas supprimer des DLL .NET ou des bibliothèques multimédias sur la seule base de leur taille.
- Éviter de supprimer des fichiers dans une couche Docker *après* leur copie : cela masque les fichiers sans récupérer l'espace des couches précédentes.

## 2. Architecture de l'image

L'image finale est basée sur `debian:13-slim`. Une image préexistante, `captvty3-wine-patcher:dedup-python`, sert de **source** pour copier sélectivement Wine et le préfixe déjà configuré avec .NET Framework 4.8.

```dockerfile
# syntax=docker/dockerfile:1
ARG USER_NAME=captvty
ARG USER_UID=1000
ARG USER_GID=1000

FROM captvty3-wine-patcher:dedup-python AS source
FROM debian:13-slim AS final
```

La variante WoW64 ne recopie que les répertoires Wine nécessaires à l'exécution retenue :

```dockerfile
COPY --from=source /opt/wine-staging/bin /opt/wine-staging/bin
COPY --from=source /opt/wine-staging/lib/wine/x86_64-unix /opt/wine-staging/lib/wine/x86_64-unix
COPY --from=source /opt/wine-staging/lib/wine/x86_64-windows /opt/wine-staging/lib/wine/x86_64-windows
COPY --from=source /opt/wine-staging/lib/wine/i386-windows /opt/wine-staging/lib/wine/i386-windows
COPY --from=source /opt/wine-staging/share /opt/wine-staging/share
```

**Point essentiel :** `i386-windows` reste présent, mais `i386-unix` et la pile de bibliothèques Linux 32 bits ne sont pas copiés. Ce choix a été testé avec le nouveau WoW64 de Wine Staging 11.16 ; il ne doit pas être généralisé sans vérification à d'autres versions de Wine.

Le préfixe Wine `.wine` contient l'installation .NET 4.8. Les DLL Wine en doublon avaient déjà été remplacées par des liens symboliques vers l'installation Wine lors de la phase `dedup-python` (1 699 fichiers dédupliqués). Il ne faut donc pas déplacer ce préfixe indépendamment des chemins ciblés par ces liens.

## 3. Historique des tailles

Les valeurs ci-dessous proviennent des mesures réalisées pendant les essais. Les tailles initiales et certaines tailles intermédiaires étaient arrondies.

| Variante | Taille de l'image | Modification principale |
|---|---:|---|
| Initiale | ~6,06 Go | Image complète avant optimisation |
| `dedup-python` | 3 640 628 132 octets | Déduplication du préfixe Wine |
| `wow64` | 3 440 470 791 octets | Copie sélective de Wine, sans pile Linux i386 |
| `no-llvm` | 3 283 046 047 octets | Suppression des bibliothèques LLVM et Z3 inutilisées dans cette configuration |
| `no-gallium` | 3 240 480 143 octets | Suppression de `libgallium-*.so` |
| `no-samba` | 3 164 657 957 octets | Retrait de `winbind` et de dépendances transitives |
| **`no-netcache`** | **2 918 282 715 octets** | Exclusion du cache d'installation .NET 4.8 |

**Gain mesuré de `no-samba` à `no-netcache` : 246 375 242 octets, soit environ 235 Mio.**
**Réduction globale : environ 52 % par rapport aux ~6,06 Go initiaux.**

Mesurer une image :

```bash
docker image inspect \
  captvty3-wine-patcher:no-samba \
  captvty3-wine-patcher:no-netcache \
  --format '{{.RepoTags}} : {{.Size}} octets'
```

## 4. Détail des optimisations

### 4.1 Déduplication du préfixe (`dedup-python`)

La phase de déduplication a remplacé 1 699 copies de DLL Wine présentes dans le préfixe par des liens symboliques vers les fichiers Wine correspondants. Cela évite de conserver plusieurs copies des mêmes binaires.

**À documenter depuis les sources du dépôt :** script Python exact, critères d'égalité des fichiers, contrôles des liens et commande de construction initiale. Ces détails ne sont pas reconstitués ici sans les fichiers originaux.

### 4.2 Copie sélective WoW64 (`wow64`)

La construction finale repart de `debian:13-slim` et copie les composants Wine nécessaires depuis `dedup-python`. L'absence de `i386-unix` réduit la taille, tout en conservant `i386-windows` pour les applications Windows 32 bits.

**Validation :** Captvty démarre ; téléchargement et lecture multimédia contrôlés.

### 4.3 Retrait de LLVM et Z3 (`no-llvm`)

Les bibliothèques `libLLVM.so.19.1` (~129,7 Mo) et `libz3.so.4` (~27,75 Mo) ont été éliminées dans la **même instruction `RUN` que l'installation APT**, afin de ne pas conserver leur contenu dans une couche antérieure.

Des essais imposant `LIBGL_ALWAYS_SOFTWARE=1` et `GALLIUM_DRIVER=softpipe` ont rendu l'interface lente : ces variables n'ont **pas** été conservées comme solution.

**Validation :** interface réactive, téléchargement et lecture avec `mpv`.

### 4.4 Retrait de Gallium (`no-gallium`)

Les fichiers `/usr/lib/x86_64-linux-gnu/libgallium-*.so` ont été supprimés dans la couche d'installation APT. Gain mesuré par rapport à `no-llvm` : **42 565 904 octets**.

**Validation :** interface Captvty réactive, téléchargement, vidéo et audio vérifiés au début, au milieu et à la fin.

### 4.5 Retrait de `winbind` (`no-samba`)

Au lieu d'exécuter un `apt autoremove` large et risqué, la dépendance explicite `winbind` a été retirée de la liste `apt-get install` du Dockerfile. La reconstruction a aussi évité l'installation de plusieurs dépendances transitives Samba, notamment la bibliothèque ICU présente auparavant.

Gain mesuré par rapport à `no-gallium` : **75 822 186 octets**.

**Validation :** téléchargement et lecture d'une émission de 53 min 18 s (~390 Mio, MPEG-TS, H.264, AAC), avec image et son sur toute la durée.

### 4.6 Exclusion du cache d'installation .NET (`no-netcache`)

L'analyse du préfixe a révélé :

```text
~1007 Mio  /home/captvty/.wine
 ~998 Mio  /home/captvty/.wine/drive_c/windows
 ~685 Mio  .../windows/Microsoft.NET
 ~400 Mio  .../Microsoft.NET/Framework64
 ~236 Mio  .../Framework64/v4.0.30319/SetupCache
 ~218 Mio  .../SetupCache/v4.8.03761/NetFx_Full.mzz
```

`NetFx_Full.mzz` appartient au cache d'installation de .NET Framework 4.8, utile notamment à la maintenance/réparation, mais pas normalement requis pour exécuter Captvty.

Le fichier `docker/Dockerfile.no-netcache` a été dérivé de `docker/Dockerfile.no-samba` en modifiant la copie du préfixe :

```dockerfile
# Préfixe .NET 4.8 déjà installé et dédupliqué.
COPY --from=source --chown=${USER_UID}:${USER_GID} \
     --exclude=drive_c/windows/Microsoft.NET/Framework64/v4.0.30319/SetupCache/** \
     /home/${USER_NAME}/.wine \
     /home/${USER_NAME}/.wine
```

Le motif exclut les **fichiers contenus** dans `SetupCache`. Le répertoire lui-même subsiste, vide (4 Kio). C'est attendu et sans conséquence :

```bash
docker run --rm --entrypoint /bin/sh \
  captvty3-wine-patcher:no-netcache \
  -c 'du -sh /home/captvty/.wine/drive_c/windows/Microsoft.NET/Framework64/v4.0.30319/SetupCache; \
      ls -la /home/captvty/.wine/drive_c/windows/Microsoft.NET/Framework64/v4.0.30319/SetupCache'
```

Résultat constaté : répertoire vide, **4,0K**.

> **Attention à la vérification :** `test ! -e .../SetupCache` échoue puisque le répertoire existe encore. Il faut vérifier son **contenu**, pas son absence.

## 5. Construction et lancement

Les Dockerfiles se trouvent dans `docker/`. Le **contexte de construction doit être `docker/`**, car le Dockerfile contient :

```dockerfile
COPY --chmod=755 entrypoint.sh /usr/local/bin/captvty3-entrypoint
```

Depuis la racine du projet :

```bash
docker build \
  -f docker/Dockerfile.no-netcache \
  -t captvty3-wine-patcher:no-netcache \
  docker/
```

**Erreur rencontrée :** utiliser `.` comme contexte provoquait `"/entrypoint.sh": not found`. L'utilisation de `docker/` a corrigé cette erreur.

Créer un fichier Compose propre à la variante sans modifier la configuration validée :

```bash
sed 's/captvty3-wine-patcher:no-samba/captvty3-wine-patcher:no-netcache/' \
  compose.no-samba.yaml > compose.no-netcache.yaml
```

Lancer :

```bash
docker compose -f compose.no-netcache.yaml \
  -p captvty3-netcache-test run --rm --no-deps captvty3
```

**Remarque :** les fichiers Compose définissent les montages de répertoires, notamment les téléchargements. Conserver ces paramètres lors des essais pour ne pas confondre régression de l'image et différence de configuration.

## 6. Protocole de non-régression

Pour chaque nouvelle variante :

1. **Construction :** Docker build terminé sans erreur.
2. **Mesure :** taille exacte obtenue par `docker image inspect`.
3. **Présence des composants :** vérifier les bibliothèques conservées et les fichiers ciblés par la suppression.
4. **Interface :** lancement de Captvty, navigation entre chaînes, affichage des émissions, vignettes et descriptions, réactivité.
5. **Téléchargement :** télécharger une émission complète.
6. **Lecture :** ouvrir le fichier avec `mpv` sur l'hôte Debian, vérifier **image et son au début, au milieu et à la fin**.
7. **Contrôle du média :** utiliser `mediainfo` pour vérifier durée, codecs, résolution et pistes audio.

Dernier test de `no-netcache`, **9 octobre 2026** : téléchargement du sujet *Journal 20h00 – Une PME dans le viseur de Trump* (France 2), fichier MPEG-TS de **16,5 Mio**, durée **3 min 33 s** :

- Vidéo H.264/AVC, 768 × 432, 25 i/s, progressif.
- Audio AAC LC, 2 canaux, 48 kHz, français.
- Lecture `mpv` : image et son vérifiés sur toute la durée, y compris début, milieu et fin.

**Conclusion : `captvty3-wine-patcher:no-netcache` est la nouvelle référence fonctionnelle validée.**

## 7. Ce qui n'a pas été supprimé, et pourquoi

### Bibliothèques FFmpeg

`/opt/wine-staging/lib/wine/x86_64-unix/winedmo.so` dépend de bibliothèques FFmpeg, dont `libavutil.so.59`, `libavformat.so.61`, `libavcodec.so.61`, ainsi que de dépendances indirectes telles que `libcodec2.so.1.2` et `libx265.so.215`.

Même si la lecture finale est effectuée avec `mpv` sur Debian, supprimer ces bibliothèques pourrait casser des fonctions multimédias de Wine. Elles ont donc été **conservées**.

### Composants .NET

Le préfixe contient notamment :

| Chemin sous `drive_c/windows` | Taille observée |
|---|---:|
| `Microsoft.NET/Framework64` | ~400 Mio avant exclusion du cache |
| `Microsoft.NET/Framework` | ~153 Mio |
| `Microsoft.NET/assembly` | ~132 Mio |
| `assembly/NativeImages_v4.0.30319_32` | ~177 Mio |
| `assembly/NativeImages_v4.0.30319_64` | ~35 Mio |
| `winsxs` | ~87 Mio |

Ces répertoires contiennent des composants d'exécution, des assemblages ou des images natives. Leur taille ne suffit pas à démontrer qu'ils sont inutiles.

Le répertoire `assembly/NativeImages_v4.0.30319_64/Temp` ne fait que **8 Kio** : ce n'est pas une piste de gain significative.

## 8. Pistes pour la suite

- Examiner les images natives **NGen** : identifier les éléments potentiellement régénérables, puis tester dans une **nouvelle** image, jamais dans `no-netcache`.
- Examiner les caches et les composants .NET 64 bits sans supposer que tout `Framework64` est inutile au nouveau WoW64.
- Mesurer les autres répertoires volumineux avant toute suppression.
- Documenter précisément le script de déduplication `dedup-python` à partir de son code source.
- Automatiser les tests de démarrage, téléchargement et inspection des médias lorsque cela devient pertinent.

**Règle de sécurité :** ne pas supprimer `Microsoft.NET/assembly`, `Framework`, `Framework64` ou les images NGen en bloc sans tests ciblés. Les économies les plus faciles (bibliothèques manifestement superflues et cache d'installation) ont déjà été réalisées.

## 9. Commandes utiles

```bash
# Mesurer les deux dernières variantes
docker image inspect \
  captvty3-wine-patcher:no-samba \
  captvty3-wine-patcher:no-netcache \
  --format '{{.RepoTags}} : {{.Size}} octets'

# Trouver les gros répertoires du préfixe
docker run --rm --entrypoint /bin/sh \
  captvty3-wine-patcher:no-netcache \
  -c 'du -h -d 2 /home/captvty/.wine/drive_c/windows 2>/dev/null | sort -h | tail -25'

# Rechercher les fichiers les plus volumineux dans un sous-arbre
docker run --rm --entrypoint /bin/sh \
  captvty3-wine-patcher:no-netcache \
  -c 'du -ah /home/captvty/.wine/drive_c/windows/Microsoft.NET 2>/dev/null | sort -h | tail -25'

# Vérifier les codecs et la durée d'un fichier téléchargé
mediainfo 'downloads/nom-du-fichier.ts'

# Lire un fichier téléchargé
mpv 'downloads/nom-du-fichier.ts'
```

---

*Ce document décrit les étapes et résultats effectivement observés. Pour une reproduction entièrement automatisée depuis zéro, compléter les instructions de création de l'image initiale et de `dedup-python` à partir des scripts présents dans le dépôt.*
