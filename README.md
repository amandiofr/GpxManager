# GpxManager

Éditeur de fichiers GPX pour Windows, développé en WPF / .NET 8.

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

## Compilation

```
dotnet build GpxManager.csproj -c Release
```

Prérequis : .NET 8 SDK, Windows.
