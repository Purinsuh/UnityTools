# Mathis Unity Toolkit

Boite a outils personnelle de shaders stylises et d'outils d'editeur pour URP, pensee pour etre partagee entre plusieurs projets via un package Git et mise a jour au fil du temps.

## Contenu

### Shaders (`ShaderTool/Shaders/`)

- **ToonAdvanced.shader** — Le shader principal, façon Genshin / ZZZ, pour décors, objets et personnages : ramp ou atlas de ramps (avec version nuit), liseré Fresnel ou Depth, reflet net, reflet métal (MatCap), émissif HDR, lumières secondaires, contour coloré par la texture pour les personnages (coupé par défaut, sans coût), éclairage « en sphère » des visages (une seule limite d'ombre nette, façon anime). Optimisé (effets désactivés = coût nul, SRP Batcher, Forward+, fondu LOD, GPU Resident Drawer, ombres précalculées shadowmask). Le liseré Depth demande la texture de profondeur de la caméra.
- **ToonSky.shader** — Ciel qui suit le jour/nuit (étoiles et dégradé la nuit).
- **CelShading.shader** — Cell shading avec bandes clair/ombre nettes, recoit et projette les vraies ombres de la lumiere directionnelle (shadow map). Conserve la couleur/texture d'origine de l'objet (`_BaseMap` / `_BaseColor`).
- **ToonOutline.shader** — Toon shading 2 tons avec un contour noir integre (technique de la coque inversee).
- **MangaDotsOutline.shader** — Degrade en pois façon trame manga (halftone), couleur de base plate, avec outline.
- **MangaDotsOutlineTextured.shader** — Variante qui conserve la texture/couleur d'origine de l'objet plutot qu'une couleur plate.

### Composant de jeu (`ShaderTool/Runtime/`)

**ToonAtmosphere.cs + ToonAtmosphereFeature.cs** — Atmosphère stylisée réglée par des Volumes (voir l'onglet Atmosphère de l'outil), dessinée par un Renderer Feature URP.

**ToonDayNight.cs** — A poser sur un objet de la scène : un curseur `night` (0 = jour, 1 = nuit) pilote les ramps de tous les materials ToonAdvanced, le ciel ToonSky, la lumière principale, l'ambiance et les reflets. Fonctionne aussi en mode édition.

### Outil d'editeur (`ShaderTool/Editor/`)

**ShaderToolsWindow.cs** — Fenêtre accessible via `Tools > Outils Shaders > Editeur`. Les onglets suivent l'ordre de travail, et chacun commence par un mode d'emploi (bouton « Aide » pour le masquer) :

1. **Convertir** — Liste tous les materials de la scène active et les convertit en masse vers un shader cible (ToonAdvanced par défaut) en conservant couleur/texture d'origine. Crée les nouveaux materials dans `Assets/ShaderTools/Materials` (dossier créé automatiquement si absent, modifiable dans l'outil).
2. **Régler** — Ajuste en direct les materials des objets sélectionnés : un style de base en un clic (Doux / Contraste / Simple, version décor), puis des sections dépliables (ombres, liseré, reflets, lumières et émissif, contour des personnages avec lissage des normales du modèle, une partie par material : survoler sa ligne l'allume dans la vue Scene ; la partie « Visage » donne un éclairage propre au visage). Gère aussi `MangaDotsOutline*`.
3. **Atmosphère** — Ambiances prêtes à l'emploi en boutons colorés (extérieur, intérieur, sombre / flippant) pour toute la scène ou pour une pièce créée autour de la sélection, fondues automatiquement entre elles. Installe l'effet sur les renderers URP en un clic. Les ambiances sont enregistrées dans `Assets/ShaderTools/Atmospheres`.
4. **Jour / Nuit** — Ajoute le jour/nuit à la scène en un clic (en reprenant l'éclairage actuel), puis bascule entre Jour, Crépuscule et Nuit avec des boutons ou un curseur.
5. **Ramps** — Génère les textures de ramp (simple, ou atlas avec une ligne par matière et une version nuit) dans `Assets/ShaderTools/Ramps`. Une ramp créée par l'outil peut être rechargée pour être retouchée.
6. **Historique** — Garde la trace persistante (survit à la fermeture d'Unity, contrairement à Ctrl+Z) du material d'origine de chaque objet converti, avec possibilité de revenir en arrière. Fonctionne aussi sur les prefabs.
7. **Vérifier la scène** — Analyse en un clic ce qui s'affiche mal ou coûte cher (objets roses, scripts manquants, liseré ou contour inutiles, petites ombres, textures trop lourdes, lampes avec ombres, émissif qui ne brille pas, caméra sans anti-aliasing...), avec sélection des objets concernés et correction en un clic.

La selection d'un objet dans la Hierarchy est surlignee en orange dans les deux onglets Conversion et Historique.

## Installation dans un projet

`Window > Package Manager > + > Add package from git URL`, puis coller l'URL de ce depot (eventuellement suivie de `#nom-du-tag` pour figer une version precise, ex: `...git#v1.0.0`).

Prerequis : le projet doit utiliser l'**Universal Render Pipeline (URP)**, ces shaders ne fonctionnent pas en Built-in ou HDRP.

## Mettre a jour un outil existant / en ajouter un nouveau

1. Ajouter/modifier les fichiers dans `ShaderTool/Editor/`, `ShaderTool/Runtime/` ou `ShaderTool/Shaders/` (chaque nouveau fichier ou dossier doit avoir son `.meta`)
2. Incrementer le numero de version dans `package.json`
3. `git add`, `git commit`, `git push` (+ `git tag vX.Y.Z && git push --tags` pour figer une version)
4. Dans chaque projet utilisant le package : `Window > Package Manager` puis **Update**
