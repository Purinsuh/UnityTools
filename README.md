# Mathis Unity Toolkit

Boite a outils personnelle de shaders stylises et d'outils d'editeur pour URP, pensee pour etre partagee entre plusieurs projets via un package Git et mise a jour au fil du temps.

## Contenu

### Shaders (`Shaders/`)

- **CelShading.shader** — Cell shading avec bandes clair/ombre nettes, recoit et projette les vraies ombres de la lumiere directionnelle (shadow map). Conserve la couleur/texture d'origine de l'objet (`_BaseMap` / `_BaseColor`).
- **ToonOutline.shader** — Toon shading 2 tons avec un contour noir integre (technique de la coque inversee).
- **MangaDotsOutline.shader** — Degrade en pois façon trame manga (halftone), couleur de base plate, avec outline.
- **MangaDotsOutlineTextured.shader** — Variante qui conserve la texture/couleur d'origine de l'objet plutot qu'une couleur plate.

### Outil d'editeur (`Editor/`)

**ShaderToolsWindow.cs** — Fenetre accessible via `Tools > Outils Shaders > Editeur`, avec 3 onglets :

1. **Reglages** — Ajuste en direct les proprietes (pois, degrade, outline) des materials `MangaDotsOutline*` sur les objets selectionnes dans la Hierarchy.
2. **Conversion** — Liste tous les materials de la scene active, permet de les convertir en masse vers un shader cible (Cell Shading par defaut) tout en conservant couleur/texture d'origine. Cree les nouveaux materials dans `Assets/ShaderTools/Materials` (dossier cree automatiquement si absent, modifiable dans l'outil).
3. **Historique** — Garde la trace persistante (survit a la fermeture d'Unity, contrairement a Ctrl+Z) du material d'origine de chaque objet converti, avec possibilite de revenir en arriere. Fonctionne aussi sur les prefabs.

La selection d'un objet dans la Hierarchy est surlignee en orange dans les deux onglets Conversion et Historique.

## Installation dans un projet

`Window > Package Manager > + > Add package from git URL`, puis coller l'URL de ce depot (eventuellement suivie de `#nom-du-tag` pour figer une version precise, ex: `...git#v1.0.0`).

Prerequis : le projet doit utiliser l'**Universal Render Pipeline (URP)**, ces shaders ne fonctionnent pas en Built-in ou HDRP.

## Mettre a jour un outil existant / en ajouter un nouveau

1. Ajouter/modifier les fichiers dans `Editor/` ou `Shaders/`
2. Incrementer le numero de version dans `package.json`
3. `git add`, `git commit`, `git push` (+ `git tag vX.Y.Z && git push --tags` pour figer une version)
4. Dans chaque projet utilisant le package : `Window > Package Manager` puis **Update**
