# Changelog

## 1.5.0

- Nouvel onglet **Vérifier la scène** : une analyse en un clic (quelques dizaines de ms) qui liste par gravité ce qui s'affiche mal ou coûte cher, avec « Voir » (sélectionne les objets) et, quand c'est possible, « Corriger » (annulable avec Ctrl+Z) :
  - à corriger : objets roses (material manquant ou shader de l'ancien rendu), scripts manquants, atmosphère invisible (effet non installé) ;
  - performances : liseré sur de grandes surfaces, contour des personnages sur du décor, petits objets qui projettent une ombre, textures de plus de 2048 px, trop de lampes avec ombres ;
  - conseils : émissif sous le seuil du bloom, objets pas encore en toon, caméra sans anti-aliasing.
- Les onglets sont maintenant sur deux rangées (les 4 étapes, puis Ramps, Historique et Vérifier la scène).

## 1.4.0

- Nouvel onglet **Atmosphère** dans l'outil : ambiances prêtes à l'emploi (Extérieur jour clair, matin brumeux, coucher de soleil, nuit ; Intérieur chaleureux, frais et lumineux, mystérieux ; Aucune) posées en un clic pour **toute la scène** ou pour une **zone** (pièce, cave…) créée autour des objets sélectionnés. Unity fond les ambiances quand la caméra passe d'une zone à l'autre. Réglages modifiables dans l'outil.
- Nouvel effet **ToonAtmosphere** (dossier `Runtime/` + shader `Hidden/Toon/Atmosphere`) : voile coloré selon la distance (couleur proche → lointaine), brume au sol en extérieur, voile sur l'horizon du ciel, halo vers le soleil, teinte de nuit qui suit ToonDayNight. Chaque ambiance règle aussi la balance des blancs et la vignette, sans toucher au reste du post-process. Aucun coût quand aucune ambiance n'est active.
- L'outil installe l'effet sur les renderers URP du projet en un bouton.
- Ambiances **Sombre / flippant** : Brouillard angoissant, Alarme rouge, Lieu abandonné, Nuit sans lune (assombrissent et désaturent aussi l'image). Les ambiances sont présentées en boutons colorés, par famille, avec une description au survol.
- Fenêtre plus simple à prendre en main : onglets dans l'ordre de travail (1 Convertir, 2 Régler, 3 Atmosphère, 4 Jour / Nuit, puis Ramps et Historique), mode d'emploi en haut de chaque onglet (bouton « Aide » pour le masquer), réglages ToonAdvanced rangés en sections dépliables, ToonAdvanced proposé par défaut pour la conversion.
- Le package dépend maintenant explicitement d'URP 17.3.

## 1.3.0

- ToonAdvanced : **contour de personnage** façon Genshin (coque inversée). La couleur vient de la texture (cheveux sombres → trait noir, peau → trait brun rosé), largeur en pixels qui grossit un peu en gros plan et s'affine au loin, recul pour rester caché derrière les tissus. Désactivé par défaut : la passe est alors coupée sur le materiau, les décors ne paient rien.
- Outil : section **Contour (personnages)** avec un bouton « Lisser les normales du contour » (bouche les trous aux arêtes vives, calculé à l'import du modèle sans toucher au fichier) et une case « Deux faces » pour les tissus et mèches simple face.
- Contour : une **partie** par material (Cheveux, Peau, Vêtements, Peau + vêtements) avec, pour chaque partie, sa couleur, sa largeur, son recul et sa silhouette extérieure. Vêtements en gris bleu par défaut. Dans un material peau + vêtements, la peau a sa propre largeur (lue dans le masque).
- Contour : **masque peau** créé automatiquement par l'outil (détection de la couleur de peau dans la texture, PNG retouchable) pour les materials qui mélangent peau et vêtements.
- Contour : **silhouette extérieure plus claire**, traits intérieurs (plis, couches) bien marqués, comme dans Genshin.
- Contour : bouton **« Couper sur les objets sélectionnés »** (yeux, reflets…) : l'objet reçoit une variante du material sans contour, qui suit tous les autres réglages.
- Contour : de loin, sous 1 pixel, le trait s'estompe au lieu de se casser en pointillés (alpha to coverage, profite du MSAA).
- ToonAdvanced : en « deux faces » (Cull Off), l'intérieur est maintenant éclairé de son côté.
- À la première ouverture du projet, la passe de contour est coupée sur les materiaux ToonAdvanced existants.

## 1.2.2

- Conversion : reprend aussi la texture / couleur des materials **glTF** (importés avec glTFast, ex. modèles Sketchfab) et **Built-in** (`_MainTex` / `_Color`). Avant, ces materials étaient convertis sans leur texture.
- Conversion : un material rangé dans un modèle (.glb, .fbx…) donne un fichier préfixé par le nom du modèle (`M_ToonAdvanced_citlali_material_0`), pour ne plus écraser les conversions d'un autre modèle aux noms génériques.

## 1.2.1

- Liseré : nouveau réglage **Mélange**. « Lumière » (comportement d'avant : le liseré éclaircit, donc un noir est invisible) ou « Encre » (le liseré remplace la couleur : un noir donne un vrai contour sombre, visible même à l'ombre). Les matériaux existants ne changent pas.
- Outil : passer en « Encre » règle le liseré en noir tout autour de l'objet ; les préréglages remettent le mode « Lumière ».

## 1.2.0

- Nouvel onglet **Jour / Nuit** dans l'outil : ajoute le composant ToonDayNight à la scène en reprenant l'éclairage actuel (l'image ne change pas), boutons Jour / Crépuscule / Nuit et curseur, annulables avec Ctrl+Z.

## 1.1.0

- Nouveau shader **ToonAdvanced** (style Genshin / ZZZ) : ramp et atlas de ramps jour/nuit, saturation des ombres, liseré Fresnel ou Depth, reflet net, reflet métal (MatCap), émissif HDR, lumières secondaires, Forward+, fondu LOD, GPU Resident Drawer (DOTS instancing), ombres précalculées (shadowmask + passe Meta).
- Nouveau shader **ToonSky** : ciel identique à Skybox/Cubemap de jour, dégradé + étoiles la nuit.
- Nouveau composant **ToonDayNight** (dossier `Runtime/`) : un curseur jour/nuit qui pilote ramps, ciel, lumière principale, ambiance et reflets.
- Outil : réglages ToonAdvanced avec 3 préréglages (+ version décor sans liseré), nouvel onglet **Ramp** (ramp simple ou atlas jour/nuit, retouche d'une ramp existante), MatCap métal générée.
- Outil allégé pour les grosses scènes : cache de la sélection, rescans différés, listes virtualisées, filtre dans Conversion.
- Conversion : copie du tiling, de l'émission, préfixe de material propre à chaque shader cible.

## 1.0.0

- Premiere version du package.
- Shaders : CelShading (avec ombres portees), ToonOutline, MangaDotsOutline, MangaDotsOutlineTextured.
- Outil d'editeur ShaderToolsWindow avec 3 onglets : Reglages, Conversion (en masse, vers n'importe quel shader cible), Historique (persistant, avec reversion, compatible prefabs).
- Surlignage de la selection Hierarchy dans les onglets Conversion et Historique.
- Creation automatique du dossier de sortie des materials si absent.
