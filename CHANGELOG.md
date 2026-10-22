# Changelog

## 1.3.0

- ToonAdvanced : **contour de personnage** façon Genshin (coque inversée). La couleur vient de la texture (cheveux sombres → trait noir, peau → trait brun rosé), largeur en pixels qui grossit un peu en gros plan et s'affine au loin, recul pour rester caché derrière les tissus. Désactivé par défaut : la passe est alors coupée sur le materiau, les décors ne paient rien.
- Outil : section **Contour (personnages)** avec un bouton « Lisser les normales du contour » (bouche les trous aux arêtes vives, calculé à l'import du modèle sans toucher au fichier) et une case « Deux faces » pour les tissus et mèches simple face.
- Contour : une **partie** par material (Cheveux, Peau, Vêtements, Peau + vêtements) avec une couleur par partie. Vêtements en gris bleu par défaut.
- Contour : **masque peau** créé automatiquement par l'outil (détection de la couleur de peau dans la texture, PNG retouchable) pour les materials qui mélangent peau et vêtements.
- Contour : **silhouette extérieure plus claire**, traits intérieurs (plis, couches) bien marqués, comme dans Genshin.
- Contour : bouton **« Couper sur les objets sélectionnés »** (yeux, reflets…) : l'objet reçoit une variante du material sans contour, qui suit tous les autres réglages.
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
