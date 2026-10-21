# Changelog

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
