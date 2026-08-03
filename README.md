# captvty3-wine-patcher
Patch IL pour Captvty 3.0.1.24 permettant son fonctionnement sous Wine.


Le patch ne contient aucun code de Captvty.
Il modifie un exécutable fourni par l'utilisateur.

## Fonctionnalités

- démarre sous Wine
- correction des problèmes liés aux thèmes Windows
- téléchargement fonctionnel
- compatible avec Wine natif ou Docker

- État actuel
- 
Le problème de démarrage a été analysé.
Plusieurs causes indépendantes ont été identifiées.
Un patcheur IL (Mono.Cecil) permet désormais de faire fonctionner Captvty 3.0.1.24 sous Wine.
Correctifs identifiés
neutralisation de _zcA::_UXB() (initialisation spécifique à Windows) ;
remplacement de l'initialisation des couleurs via VisualStyleRenderer par SystemColors ;
protection de plusieurs accès non sécurisés :
FileInfo.Length
Stream.Position
constructeur utilisant FileInfo.Length après renommage du fichier.
Résultat
Captvty démarre correctement ;
les listes d'émissions s'affichent ;
les téléchargements aboutissent ;
le fichier .ts produit est valide (lecture avec mpv/MediaInfo).



Captvty est la propriété de son auteur.

Ce dépôt ne redistribue aucun binaire Captvty.

Le patcheur modifie un exécutable fourni par l'utilisateur.

Pourquoi ne pas fournir directement un Captvty.exe patché ?

Par sécurité et par respect du travail de l'auteur, ce projet ne redistribue aucun binaire modifié de Captvty.

Le dépôt fournit uniquement le code source du patcheur. L'utilisateur compile ce dernier et l'applique lui-même à son exemplaire original de Captvty. Cela permet à chacun de vérifier exactement quelles modifications sont apportées et évite d'avoir à faire confiance à un exécutable modifié par un tiers.
