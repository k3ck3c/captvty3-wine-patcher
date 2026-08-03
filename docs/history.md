Historique
Contexte

Captvty 3.0.1.24 ne fonctionne pas correctement sous les versions récentes de Wine.

Les principaux symptômes observés étaient :

plantage immédiat au démarrage ;
erreurs liées aux thèmes Windows (UxTheme / Visual Styles) ;
plantage pendant un téléchargement ;
plantage à la fin d'un téléchargement.

L'objectif de ce projet n'est pas de modifier le fonctionnement de Captvty, mais simplement de lui permettre de fonctionner correctement sous Wine.

Analyse

L'analyse a commencé par une comparaison du comportement de Captvty sous plusieurs versions de Wine.

Le programme étant développé en .NET, l'investigation a ensuite été menée directement au niveau du code IL à l'aide de Mono.Cecil et de plusieurs petits outils développés pour ce projet.

L'objectif n'était pas de désobfusquer complètement Captvty, mais d'identifier les appels au framework .NET susceptibles de provoquer les exceptions observées.

Cette approche a rapidement permis d'isoler plusieurs hypothèses faites par l'application qui sont vraies sous Windows mais pas toujours sous Wine.

Premier correctif : démarrage

Le premier plantage se produisait pendant l'initialisation de l'application.

La méthode concernée est :

_zcA::_UXB()

Cette routine effectue plusieurs initialisations spécifiques à Windows qui ne sont pas indispensables au fonctionnement de Captvty sous Wine.

Le correctif consiste à neutraliser cette méthode.

Deuxième correctif : couleurs des thèmes Windows

Captvty interroge plusieurs couleurs au moyen de :

VisualStyleRenderer.GetColor(...)

Wine ne reproduit pas complètement ce comportement.

Le correctif remplace cette initialisation par les couleurs génériques :

SystemColors.Window
SystemColors.WindowText

L'apparence reste très proche tout en devenant indépendante des thèmes Windows.

Troisième correctif : longueur du fichier temporaire

Pendant un téléchargement, Captvty peut appeler :

new FileInfo(path).Length

alors que le fichier temporaire n'existe pas encore.

Le correctif vérifie d'abord :

File.Exists(path)

et retourne une longueur nulle si le fichier n'est pas encore présent.

Quatrième correctif : position du flux

Une autre exception provenait de :

Stream.Position

alors que le flux était momentanément nul.

Le correctif retourne simplement 0 tant que le flux n'est pas initialisé.

Cinquième correctif : fin du téléchargement

À la fin d'un téléchargement, Captvty crée un objet représentant le fichier téléchargé.

Selon le moment où cette opération intervient, le fichier temporaire peut déjà avoir été renommé.

Une nouvelle vérification de File.Exists() évite alors une exception sur FileInfo.Length.

Validation

Le fonctionnement du correctif a été validé par plusieurs essais :

démarrage complet de Captvty ;
navigation dans les listes d'émissions ;
téléchargement complet d'une émission ;
lecture du fichier obtenu avec mpv ;
vérification du flux avec MediaInfo.

Le téléchargement d'une émission de plus de 500 Mo a notamment permis de valider le dernier correctif.

Philosophie du projet

Ce dépôt ne distribue volontairement aucun exécutable Captvty modifié.

L'utilisateur conserve son exemplaire original de Captvty et applique lui-même le correctif.

Cette approche présente plusieurs avantages :

respect du travail de l'auteur de Captvty ;
possibilité de vérifier le code source du patcheur ;
reproductibilité des modifications ;
pas de diffusion d'un exécutable modifié dont le contenu serait impossible à vérifier.
Perspectives

L'objectif idéal serait que ce patcheur devienne un jour inutile.

Deux scénarios sont possibles :

Wine évolue et reproduit entièrement les comportements attendus par Captvty ;
ou l'auteur de Captvty intègre directement des tests de robustesse équivalents.

Dans les deux cas, ce projet aura rempli son rôle.