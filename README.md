# Captvty 3 Wine Patcher

Correctifs de compatibilité Wine pour **Captvty 3.0.1.24, 3.0.1.25 et 3.0.1.27**.

Ce projet fournit un patcheur écrit avec **Mono.Cecil** qui modifie automatiquement un exemplaire original de Captvty afin de corriger plusieurs problèmes rencontrés sous Wine.

## Origine du projet

Ce projet est né d'un rapport de bug Wine 
https://bugs.winehq.org/show_bug.cgi?id=55955
et d'une analyse méthodique visant à améliorer la compatibilité de Captvty 3.0.1.24 sous Wine. Les correctifs proposés ne modifient pas les fonctionnalités de Captvty ; ils rendent simplement l'application plus robuste face à des situations qui peuvent se produire sous Wine.

Le but n'est pas de modifier Captvty, mais uniquement d'améliorer sa compatibilité avec Wine en appliquant le minimum de changements possibles.

L'objectif n'a jamais été de désobfusquer Captvty. L'analyse s'est limitée aux chemins d'exécution conduisant aux exceptions observées, en s'appuyant sur les appels au framework .NET (System.IO, System.Drawing, VisualStyleRenderer, etc.).



## Pourquoi Mono.Cecil ?

Captvty est une application .NET.

Les problèmes rencontrés ont été identifiés directement au niveau du code IL, sans disposer du code source.

Mono.Cecil permet de modifier proprement un assembly .NET en remplaçant uniquement les quelques instructions nécessaires, sans avoir recours à un éditeur hexadécimal ni réécrire entièrement l'exécutable.

Les correctifs restent ainsi :

- ciblés ;
- reproductibles ;
- facilement vérifiables ;
- documentés dans ce dépôt.

## Objectif

Captvty 3.0.1.24 fonctionne correctement sous Windows mais peut rencontrer plusieurs exceptions sous Wine :

- plantage au démarrage ;
- problèmes liés aux thèmes Windows (UxTheme / Visual Styles) ;
- plantages pendant ou à la fin d'un téléchargement.

Ce projet applique automatiquement plusieurs correctifs IL qui rendent l'application beaucoup plus robuste sous Wine.

## Fonctionnalités

Le patcheur applique les corrections suivantes :

- neutralisation d'une initialisation Windows spécifique (`_UXB()`) ;
- remplacement de l'utilisation de `VisualStyleRenderer.GetColor()` par des couleurs système (`SystemColors`) ;
- protection des accès à `FileInfo.Length` lorsque le fichier n'existe pas encore ;
- protection des accès à `Stream.Position` lorsque le flux est nul ;
- protection de la lecture de la taille du fichier pendant la finalisation d'un téléchargement.

## Captvty 3.0.1.27

Pour Captvty 3.0.1.27, cinq corrections spécifiques sont appliquées :

- neutralisation du hook UxTheme qui échoue sous Wine ;
- remplacement de l'initialisation VisualStyleRenderer par SystemColors.Window ;
- neutralisation de l'extraction d'icônes depuis imageres.dll, tout en conservant l'initialisation du gestionnaire yt-dlp ;
- neutralisation de LinearGradientBrush.RotateTransform(20f), non implémenté dans ce chemin sous Wine ;
- hauteur initiale du panneau de téléchargements réduite de 300 à 150.

Le patcheur vérifie les signatures IL attendues avant d'appliquer les modifications.

SHA-256 de l'exécutable 3.0.1.27 original testé :

    a4fd6bc53c41804225e1760b4fb4bfb339ed06895953339ee1318d7d8276b69c

SHA-256 après application du patch :

    72a05e34ee9b5358db011fd94721d2191211168671f64ec8546269bbc5866cfe

La version patchée a été validée sous Wine avec lancement de l'interface,
consultation des programmes et téléchargements réels.

## Ce que ce projet ne fait pas

Ce dépôt :

- ne contient **aucun** code source de Captvty ;
- ne redistribue **aucun** exécutable Captvty ;
- ne redistribue **aucun** exécutable patché.

L'utilisateur applique le patch à son propre exemplaire de Captvty.

## Compilation

Sous Debian / Ubuntu :

```bash
sudo apt install mono-devel libmono-cecil-private-cil
```

Puis :

```bash
scripts/build.sh
```

## Application du patch

```bash
scripts/patch.sh \
    Captvty.exe \
    Captvty-patched.exe
```

Le script `patch.sh` accepte actuellement de façon stricte l'exécutable original
de Captvty 3.0.1.27. Son SHA-256 est vérifié avant toute modification ; un
exécutable différent est refusé et aucun fichier de sortie n'est créé.


## Utilisation

Le fichier patché peut être exécuté :

- sous Wine ;
- dans un conteneur Docker ;
- via Docker Compose.

## Sécurité

Ce projet ne distribue volontairement aucun exécutable Captvty modifié.

L'objectif est que chacun puisse :

- examiner le code source du patcheur ;
- le compiler lui-même ;
- appliquer le patch à son propre exemplaire de Captvty.

Ainsi, il n'est jamais nécessaire de faire confiance à un exécutable modifié fourni par un tiers.

## Licence

Le code du patcheur est distribué sous licence MIT.

Captvty reste la propriété de son auteur.
