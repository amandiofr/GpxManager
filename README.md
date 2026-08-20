# GpxManager

Éditeur de fichiers GPX pour Windows.

## Installation

1. Aller sur la page [Releases](https://github.com/amandiofr/GpxManager/releases/latest).
2. Dans la section **Assets** de la dernière release (cliquer sur "Assets" pour la déplier), télécharger le fichier `GpxManager-Setup-X.X.X.exe`.
3. Double-cliquer dessus et suivre l'assistant (Suivant → Suivant → Installer).
4. GpxManager se lance automatiquement à la fin. Il sera aussi disponible dans le menu Démarrer.

Tout est inclus dans l'installateur, aucune dépendance à installer séparément et aucun droit administrateur requis.

> Windows peut afficher un avertissement "Windows a protégé votre ordinateur" au premier lancement car l'application n'est pas signée numériquement. Cliquer sur **Informations complémentaires** puis **Exécuter quand même**.

Pour désinstaller, utiliser **Applications** dans les Paramètres Windows.

## Fonctionnalités

### Gestion des fichiers
- Ouverture de plusieurs fichiers GPX simultanément dans des **onglets**
- Glisser-déposer de fichiers GPX sur la fenêtre
- **Association de fichiers** : double-cliquer sur un `.gpx` l'ouvre dans l'instance déjà ouverte (instance unique)
- Restauration automatique de la session au redémarrage
- Création d'un nouveau fichier GPX vide
- Menu contextuel sur les onglets : fermer les autres, fermer tous

### Visualisation
- Carte interactive (OpenStreetMap / Topo / Satellite) via Mapsui
- Affichage des traces en rouge, trace sélectionnée en bleu
- Affichage des waypoints avec icône
- Flèches de direction sur les traces (densité adaptée au zoom)
- Panoramique avec clic droit

### Statistiques par trace
- Distance
- Durée
- Dénivelé positif / négatif (lissage par moyenne glissante pour neutraliser le bruit GPS)
- Altitude min / max
- Heure de départ et d'arrivée
- Nombre de points GPS

### Édition des traces
| Outil | Description |
|-------|-------------|
| ✂ Diviser | Clic sur la carte pour couper une trace au point le plus proche |
| ▱ Gommer | Sélection rectangle pour supprimer des points |
| 🧶 Pelotes | Détection et affichage des amas de points erratiques (pelotes GPS) |
| Simplifier | Simplification par algorithme **Ramer-Douglas-Peucker** avec curseur 4 niveaux (1 / 3 / 10 / 20 m) — s'applique à toutes les traces du fichier, réversible |

- Renommer une trace (double-clic ou menu contextuel)
- Copier / coller des traces entre fichiers
- Joindre deux traces sélectionnées en une
- Supprimer une trace
- Réordonner les traces par glisser-déposer
- **Joindre tout** : fusionner toutes les traces de tous les fichiers ouverts dans un nouveau fichier

### Nettoyage
- **Épurer** : suppression des balises `extensions`, `geotracker` et `metadata` superflues
- Compatibilité avec les fichiers GPX malformés (préfixes de namespace non déclarés)

## Raccourcis

| Raccourci | Action |
|-----------|--------|
| `Ctrl+O` | Ouvrir un fichier GPX |
| Glisser-déposer | Ouvrir un ou plusieurs fichiers |
| Double-clic sur `.gpx` | Ouvrir dans l'instance existante |

## Technologies

- **WPF** / .NET 8
- **CommunityToolkit.Mvvm** 8.4.2 (MVVM source generators)
- **Mapsui** 5.1 + **Mapsui.Nts** (carte interactive)
- Mutex + Named Pipe (instance unique et IPC)

## Compilation (développeurs)

Prérequis : .NET 8 SDK, Windows.

```
dotnet build GpxManager.csproj -c Release
```

### Construire l'installateur (mainteneurs)

Nécessite [Inno Setup 6](https://jrsoftware.org/isinfo.php) (`winget install JRSoftware.InnoSetup`) en plus du SDK .NET 8.

```
.\installer\build-installer.ps1 -Version 1.1.0
```

Ceci publie un build self-contained (le Runtime .NET n'a pas besoin d'être installé sur la machine cible) et l'assemble dans `publish-installer\GpxManager-Setup-<version>.exe`.
