# Vehicle credits

The vehicle is adapted from **Lamborghini Murcielago LP-640 – Low Poly** by **Ajay Gawde**.

- Original model: https://sketchfab.com/3d-models/lamborghini-murcielago-lp-640-low-poly-1822aaadc6f34060a38ddf46f0c84ec3
- Author: https://sketchfab.com/aagawde
- License: Creative Commons Attribution 4.0 International (CC BY 4.0), https://creativecommons.org/licenses/by/4.0/

Changes for Flying Lambo: normalized to RC scale; converted modifier results to static meshes; replaced the yellow paint and baked seam texture with clean pearl-white materials, retaining dark regions as geometric material boundaries; made the windows opaque; adjusted runtime materials; refined body curvature and wheel arches; removed overlapping old nozzles; added modelled panel gaps, honeycomb intakes and cooling louvers; rebuilt detailed tyres, forged split-spoke wheels, drilled brake rotors, calipers, projector lamps and recessed nozzles; added articulated steering/spin pivots, neutral studio reflections, and up to 2048 px runtime textures with Android ASTC 4×4 compression; added lift and reaction-control ports, exhaust attachment points, and the thruster effects and flight rig.

The unmodified extracted donor source and textures are included in `Art/Donor/original/source/Lamborgini/`. Duplicate download archives are excluded from Git. Editable high-detail source is `Art/AstraLP640_HD.blend`; its reproducible recipe is `Art/refine_lamborghini.py`, using `Art/adapt_lamborghini.py` for the base import. The earlier low-detail conversion remains in `Art/AstraLP640.blend`. The Blender and Unity exports retain the derived body geometry; material grouping, paint, wheels and added details have been reworked. The game displays author/license credit when paused with text visible and offers D-pad left/right links to the original model and license.

The donor source, derived Lamborghini assets, textures and vehicle images in this repository are distributed under CC BY 4.0. This includes the README vehicle render, `Art/AstraLP640*.blend` and `.png`, `Unity/Assets/AstroQuest/Art/DonorVehicle.fbx`, and `DonorTextures/`. The project's MIT license does not replace that attribution requirement. Unity and Meta dependencies retain their respective licenses.

The earlier wedge placeholder (`Art/AstraWedge.blend`) and the added thruster geometry are original project assets. The vehicle conversion is a game asset and is not affiliated with or endorsed by Lamborghini or Tesla.
