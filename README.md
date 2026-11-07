# Drone Warfare: Sandbox (Unity)

A real-time drone strategy prototype. You command a squad of drones from a top-down RTS camera:
select them, send them across the map, and hit enemy tanks, trucks, AA guns and radars.

**Setting:** the fictional Varrow Valley. You fly for the Norvane Defence Force (teal, a white chevron in a
hexagon) against the Dravin Coalition (black and orange, a triangle in a diamond). Every side, place, flag and
emblem is invented (`Factions.cs`, where they can be renamed); the gameplay borrows how drones are used in
modern wars without standing in for any real country or army.

It starts at a menu with two ways to play, plus the tech shop:

- **Asset Hunt** (missions): destroy a mission's targets with a fixed stock of drones. Each mission adds one
  idea, and you earn up to three stars for using few drones; finishing one unlocks the next.
- **Free play sandbox:** no win or lose. Destroyed enemies respawn after 30 seconds, and you can launch drones
  whenever you want (up to 30). All the tech from the shop is switched on.
- **Tech shop:** every enemy you destroy in a mission earns points, and so does each new star. Spend them on a
  thermal camera, the Nightjar heavy bomber and upgrades. Purchases are kept and apply to every mission.

The world is built from code: green summer farmland in a valley ringed by wooded hills, with a river
between your base and enemy land, a bridge, dirt roads, villages with a church, patchwork fields (wheat,
young crops, ploughed soil, sunflowers, mown hay with bales), hedgerows and poplar rows. Your base is a
HESCO-walled drone yard with a Quonset hangar and launch pads; the enemy holds a fortified command post.
Up the valley to the west, the river comes out of a reservoir: a two-armed lake with rocky islands and
forested banks, held back by a curved concrete dam with an arcade of arches, stone gatehouses, lamps and a
stepped spillway pouring white water into a plunge pool and a rocky gorge. Meadows have grass and
wildflowers, the shallows have reeds, and the sky fades between real day, sunset and night skies.
Rocks, the gorge's rock faces, stumps and the undergrowth (ferns, nettles, moss, bushes, and pebbles and
weeds along the water) are photo scans, spread in patches rather than evenly.
The layout follows the concept art in `Docs/Concept` (made with ChatGPT image generation; the lake and dam
were tuned against ChatGPT's own critique of the renders).
Models are made by the Blender scripts in `Tools/Blender` (see below) and dressed in photo textures;
if a model is missing, the game falls back to simple shapes. Textures, the sky and the scanned models come
from [Poly Haven](https://polyhaven.com) (CC0, see Credits).

## Getting it running

This is a Unity 6 project (6000.6) using the High Definition Render Pipeline (HDRP 17.6): a physically based
sky with volumetric clouds, volumetric fog and light shafts, HDRP water, screen-space reflections and ambient
occlusion, and automatic exposure. It wants a DirectX 12/11 GPU of roughly RTX 3050 class or better.

1. Install **Unity Hub** and a **Unity 6** editor (6000.6.x) with Windows Build Support.
2. In Unity Hub: **Add → Add project from disk** and pick this folder.
3. Open it and open `Assets/Scenes/SampleScene`. The map shows in the Scene view straight away (a preview
   that is rebuilt, never saved; untick *Show Map* on the *Map Preview* object to hide it). Press **Play**
   and the sandbox builds itself in whatever scene is open.

To make a standalone Windows build, use the menu **Drone Sandbox → Build for Windows**
(it writes `Builds/Windows/DroneWarfare.exe`), or from the command line:

```
Unity.exe -batchmode -quit -projectPath . -executeMethod DroneSandbox.EditorTools.BuildTools.BuildWindows
```

The scripts work with both input systems (the new Input System package and the old Input Manager),
so you don't need to change any input settings.

### Smoke test

Run the built game with `-autotest <folder>` and it plays a short scripted battle by itself, saves
screenshots and a log of errors and stats into that folder, then quits:

```
Builds\Windows\DroneWarfare.exe -autotest AutoTestOutput
```

## Asset Hunt missions

| # | Mission | Targets | What it teaches |
| --- | --- | --- | --- |
| 1 | First Light | 3 parked supply trucks | Launching drones and ordering a Kamikaze strike |
| 2 | Open the Sky | A radar and the AA gun it boosts | Kill order: the radar first, then outrange the AA gun |
| 3 | Hidden Guns | 3 artillery guns under camouflage nets | Searching the yellow circles with Scouts; targets stay hidden until a drone is within 26 m |
| 4 | Convoy | 4 fuel trucks driving to the enemy HQ | Catching moving targets before they arrive |
| 5 | Command Post | The enemy HQ building | Breaking into a compound guarded by a radar and three AA guns |

Each mission has its own time of day (First Light at dawn, Hidden Guns at dusk, Convoy at night, Command Post
before dawn), and the clock keeps running, so light changes as you play. Free play runs a full day every
24 minutes.

Missions are data in `Missions.cs` (targets, defences, drone stock, the drone count for three stars), so new
ones need no new code. Best stars, points and purchases are kept in the player's saved preferences.

## Tech shop

| Item | Points | What it does |
| --- | --- | --- |
| Wren-T thermal camera | 50 | Scouts get a thermal camera, so T (thermal view) works with a Scout in the air |
| Nightjar heavy bomber | 150 | One per mission (key 4), counts as two drones for stars: three bombs, flies home to rearm, has its own thermal camera |
| Armoured frames | 80 | Every drone has 40% more HP |
| High-output motors | 70 | Every drone flies 20% faster |
| Heavier warheads | 100 | Guns, missiles, kamikaze charges and bombs do 30% more damage |
| Long-range optics | 60 | Hidden targets are spotted from 36 m instead of 26 m |
| Supply contract | 120 | One extra Scout, Strike and Kamikaze drone in every mission |

Kills are worth: truck 10, radar 25, AA gun 30, artillery 35, tank 40, command post 100. Each star you hadn't
earned before on a mission adds 20. Clearing all five missions with three stars and every enemy destroyed earns
about 850 points; everything in the shop costs 630. The items and prices are in `Tech.cs`.

## Controls

| Input | Action |
| --- | --- |
| Left-click / drag a box | Select drones |
| Shift + click | Add to or remove from the selection |
| Right-click the ground | Move (the group spreads out) |
| Right-click an enemy | Attack it |
| WASD / arrow keys | Pan the camera |
| Q / E | Rotate the camera |
| Mouse wheel | Zoom |
| 1 / 2 / 3 / 4 | Launch a Scout / Strike / Kamikaze / Nightjar drone (the Nightjar once bought) |
| Space | Select all drones |
| X | Stop |
| Esc | Deselect |
| H | Show or hide the help panel |
| T | Thermal view (white-hot camera; needs a Scout with the Wren-T camera, or the Nightjar, in the air) |
| N | Skip ahead 2 hours (free play) |
| P | Photo mode (below) |

**Photo mode** turns the camera into a free-flying camera drone anywhere on the map, down to a metre over
the ground or water, with the HUD hidden, depth of field focused on the middle of the frame, and bounce
light (screen-space global illumination, too slow for play but fine for pictures):

| Input | Action |
| --- | --- |
| WASD | Fly (Shift for faster) |
| R / F | Up / down |
| Q / E, right-drag | Turn and look around |
| Mouse wheel | Zoom |
| [ / ] (or , / .) | Move the time of day |
| F12 (or K) | Save a photo to Pictures/Drone Warfare |
| P | Back to the game |

## Units

**Your drones**

| Drone | HP | Speed | Weapon | Notes |
| --- | --- | --- | --- | --- |
| Scout | 40 | fast | Machine gun, range 11 | Good against trucks; too short-ranged to hit AA safely |
| Strike | 70 | slow | Homing missiles, range 20, splash | Outranges a lone AA gun, but not one boosted by radar |
| Kamikaze | 25 | fastest | Blows itself up (big splash) | Only attacks when you order it to |
| Nightjar | 130 | slowest | Three guided bombs, splash | Bought in the tech shop. Flies high and bombs from overhead (moving targets are missed 30% of the time), then returns to base and rearms in 15 s. Counts as two drones for stars |

Gun and missile drones fire on enemies in range by themselves when they're idle; the Nightjar bombs anything
it's hovering right over. Upgrades from the tech shop raise these numbers.

**Enemies**

| Enemy | Threat | Notes |
| --- | --- | --- |
| Tank | none | Patrols, tracks drones with its turret, lots of HP |
| Truck | none | Patrols, fragile |
| AA gun | shoots drones | Red dotted ring shows how far it reaches a drone cruising 8 m up (it bends with the hills) |
| Radar | none | AA guns within 35 m of a working radar get +6 range. Kill it first. |
| Artillery | none (to drones) | Towed gun under a camouflage net; its muzzle flash shows even while hidden (missions only) |
| Command post | none | The enemy HQ building itself, 320 HP (missions only) |

## How the code is organised

All scripts are in `Assets/DroneSandbox/Scripts`:

| File | What it does |
| --- | --- |
| `Bootstrap.cs` | Starts the sandbox when you press Play |
| `SandboxGame.cs` | Builds the world, runs the menu, sandbox and missions; tracks units, targets, stars and respawns |
| `Missions.cs` | The Asset Hunt missions as data |
| `Tech.cs` | The tech shop: items, prices, points per kill, and saving points and purchases |
| `DayNight.cs` | Time of day: sun and moon, sky, fog, night lights (headlights, floodlights, windows) and the thermal view |
| `Factions.cs` | The fictional sides, their emblems and flags (drawn in code), and the place names |
| `Flag.cs` | Waving flags at both bases |
| `MapLayout.cs` | The hand-made map: bases, defences, river, roads, villages, and where the fields go |
| `World.cs` | Builds the map from the layout: terrain, river, fields, villages, woods, rocks, bridge, bases |
| `Ground.cs` | Terrain: hills, river channel, roads, levelled sites, texture splatting, height queries |
| `Farmland.cs` | Fields with generated crop textures, hay bales, hedgerows, poplar rows |
| `Water.cs` | The river (with the plunge pool, foaming below the dam) and the lake, as HDRP water surfaces over meshes built from the map |
| `Reservoir.cs` | The dam, its lamps, spray and lake mist, boulders and rock faces in the gorge, shore rocks, the lake woods and a reflection probe over the lake |
| `Grass.cs` | Grass, wildflowers and reeds, drawn by the terrain as instanced detail meshes |
| `DamWeathering.cs` | Decals that weather the dam: moss at its foot, spray-wet spillway walls, slime on the steps, the waterline stain |
| `Scans.cs` | The photo-scanned models by family (boulders, rocks, cliffs, ferns, bushes...) and placing one on the ground |
| `Undergrowth.cs` | Pebbles and weeds along the lake and river, ferns, nettles and moss among the trees, bushes at wood edges, stumps and fallen trunks, each in noise-masked patches |
| `PhotoMode.cs` | The free camera drone, depth of field and saving photos |
| `Bases.cs` | Layout of your drone base and the enemy command post |
| `PathField.cs` | Fast "distance to the river / nearest road" lookups |
| `MapPreview.cs` | Shows the map in the editor without pressing Play |
| `Atmosphere.cs` | The HDRP look: physically based sky with stars, volumetric clouds, volumetric fog, exposure, shadows, reflections, ambient occlusion and the colour grade |
| `RtsController.cs` | Camera, selection and move/attack orders |
| `SandboxHud.cs` | On-screen HUD: menu, tech shop, briefing and results, health bars, selection, threat rings, launch buttons, help |
| `Drone.cs` | Drone types, flight and weapons, upgrades, and the Nightjar's bombing and rearming |
| `EnemyUnit.cs` | Enemy types, patrolling and AA fire |
| `Unit.cs` | Health and team shared by all units |
| `Projectile.cs` | Bullets, homing missiles and guided bombs |
| `Combat.cs` | Target finding and blast damage |
| `Effects.cs` | Explosions, smoke, sparks, order markers, wrecks |
| `Art.cs` | Colours, materials, model loading (with photo materials) and primitive stand-ins |
| `GameInput.cs` | Keyboard and mouse for either input system |
| `AutoTest.cs` | The `-autotest` smoke test (sandbox battle, then missions) |

`Assets/DroneSandbox/Editor` adds the **Drone Sandbox** menu: *Build for Windows*, and
*Configure Project*, which makes the HDRP pipeline asset (`Editor/HdrpSetup.cs`), sets player settings, creates the material assets the game
loads at runtime (re-run it after adding new textures or a new sky) and saves the sky, sun and map preview
into the main scene. Models live in `Assets/DroneSandbox/Resources/Models` and are loaded by name
(`Drone_Scout`, `Enemy_Tank`, `Base_Hangar`, ...). Each model's flat-coloured materials are swapped at load
for photo-textured ones of the same name from `Resources/Materials/PBR` (the table is
`ProjectSetup.PbrMaterials`; the textures are in `Assets/DroneSandbox/Textures/PBR`, fetched by
`python Tools/fetch_textures.py`). Terrain textures are `Resources/Textures/Terrain/<meadow|lush|dirt|rock>_diff`
and `_nor`, and the sky is `Resources/Sky/sky.hdr`; drop in replacements with the same names and run
*Configure Project*.

Unit stats (HP, speed, range, damage) are set in `Drone.Setup` and `EnemyUnit.Setup`, so balance
tweaks are one-line changes.

## Models (Blender)

The models are generated by Python scripts, so they can be tweaked and rebuilt instead of hand-edited.
With Blender 4.2 or newer:

```
blender --background --factory-startup --python Tools/Blender/make_drones.py -- Assets/DroneSandbox/Resources/Models
blender --background --factory-startup --python Tools/Blender/make_world.py -- Assets/DroneSandbox/Resources/Models
blender --background --factory-startup --python Tools/Blender/make_base.py -- Assets/DroneSandbox/Resources/Models
blender --background --factory-startup --python Tools/Blender/make_scenery.py -- Assets/DroneSandbox/Resources/Models
blender --background --factory-startup --python Tools/Blender/make_dam.py -- Assets/DroneSandbox/Resources/Models
```

`make_world.py` builds the enemy vehicles, `make_base.py` the two bases and their props,
`make_scenery.py` the bushes, hay bales, buildings, fences, bridge and fallback rocks, and `make_dam.py` the reservoir dam (with the
white-water sheet down its spillway, whose vertex colour says how much foam it carries). Add a preview folder as a second argument to also
render a PNG of each model, and a comma-separated list of names as a third to rebuild only some
(for example `Enemy_Tank` or `Scenery_Pine,Scenery_Spruce`). Models get UVs in real-world metres so photo
textures sit at the right scale, and static models are merged into one mesh each so the map stays fast.
Parts the game moves are found by name: drone propellers are `Prop_*`, the Nightjar's bombs are `Bomb_*`
(hidden as they drop), and turrets and radar heads hang under an empty called `Turret`.

The trees (oaks, birches, poplars, spruces and pines, several of each) have their own script, which grows
them by space colonisation, renders their leaf atlases and bark, and makes three levels of detail ending in a
billboard:
`blender --background --factory-startup --python Tools/Blender/make_trees.py -- Assets/DroneSandbox/Resources/Models Assets/DroneSandbox/Textures/PBR [preview_dir] [names]`.
A model with versions named `<name>_V2`, `_V3`... gets one of them picked by position wherever it's placed.

The rocks, rock faces, deadwood and plants are photo scans instead: `python Tools/fetch_models.py` downloads them
from Poly Haven into `Tools/scans` (kept out of git), and
`blender --background --factory-startup --python Tools/Blender/import_scans.py -- [preview_dir] [names]` splits
the sets, decimates them into two levels of detail, gathers tiny plants into patches and writes
`Resources/Models/Scan_*.fbx` plus their `scan_<id>_diff`/`_nor` textures (then run *Configure Project* for
the materials).

## Next features to pick from

1. **Battery life.** Drones drain power while flying and must return to base to recharge.
2. **Fog of war.** You only see what your drones see, which makes scouts matter.
3. **Enemy fights back harder.** Jammers that cut drone control, enemy drones, attacks on your base.
4. **Missions with win/lose.** Destroy the HQ, defend the base, limited drone budget.
5. **Take control (FPV).** Jump into one drone and fly it yourself from its camera.
6. **Better vehicles and effects.** Swap in Asset Store vehicle packs, particle explosions and smoke, sound.
7. **Control groups and waypoints.** Ctrl+1-9 groups, shift-click to queue a patrol route.

## Credits

Textures and sky from [Poly Haven](https://polyhaven.com), released under CC0:
[Forrest Ground 01](https://polyhaven.com/a/forrest_ground_01),
[Forest Ground 04](https://polyhaven.com/a/forest_ground_04),
[Aerial Rocks 02](https://polyhaven.com/a/aerial_rocks_02),
[Concrete Floor Worn 001](https://polyhaven.com/a/concrete_floor_worn_001),
[Concrete Wall 004](https://polyhaven.com/a/concrete_wall_004),
[Corrugated Iron 02](https://polyhaven.com/a/corrugated_iron_02),
[Worn Corrugated Iron](https://polyhaven.com/a/worn_corrugated_iron),
[Metal Plate](https://polyhaven.com/a/metal_plate),
[Green Metal Rust](https://polyhaven.com/a/green_metal_rust),
[Rusty Painted Metal](https://polyhaven.com/a/rusty_painted_metal),
[White Plaster Rough 01](https://polyhaven.com/a/white_plaster_rough_01),
[Grey Plaster 02](https://polyhaven.com/a/grey_plaster_02),
[Plastered Stone Wall](https://polyhaven.com/a/plastered_stone_wall),
[Brick Wall 02](https://polyhaven.com/a/brick_wall_02),
[Clay Roof Tiles 02](https://polyhaven.com/a/clay_roof_tiles_02),
[Weathered Plank Siding](https://polyhaven.com/a/weathered_plank_siding),
[Wood Planks Grey](https://polyhaven.com/a/wood_planks_grey),
[Rough Wood](https://polyhaven.com/a/rough_wood),
[Bark Brown 02](https://polyhaven.com/a/bark_brown_02),
[Pine Bark](https://polyhaven.com/a/pine_bark),
[Hessian 230](https://polyhaven.com/a/hessian_230),
[Mossy Rock](https://polyhaven.com/a/mossy_rock),
[Gravel Floor](https://polyhaven.com/a/gravel_floor),
[Asphalt 02](https://polyhaven.com/a/asphalt_02),
[Leafy Grass](https://polyhaven.com/a/leafy_grass),
[Kloofendal 48d Partly Cloudy (Pure Sky)](https://polyhaven.com/a/kloofendal_48d_partly_cloudy_puresky),
[Qwantani Sunset (Pure Sky)](https://polyhaven.com/a/qwantani_sunset_puresky) and
[Qwantani Night (Pure Sky)](https://polyhaven.com/a/qwantani_night_puresky).

Scanned models from [Poly Haven](https://polyhaven.com), released under CC0 (scanned and made by Kless Gyzen,
Rico Cilliers, Jenelle van Heerden, Dario Barresi and Rob Tuytel):
[Rock Moss Set 01](https://polyhaven.com/a/rock_moss_set_01),
[Rock Moss Set 02](https://polyhaven.com/a/rock_moss_set_02),
[Boulder 01](https://polyhaven.com/a/boulder_01),
[Rock 07](https://polyhaven.com/a/rock_07),
[Rock 09](https://polyhaven.com/a/rock_09),
[Rock Face 01](https://polyhaven.com/a/rock_face_01),
[Rock Face 02](https://polyhaven.com/a/rock_face_02),
[Tree Stump 01](https://polyhaven.com/a/tree_stump_01),
[Tree Stump 02](https://polyhaven.com/a/tree_stump_02),
[Dead Tree Trunk](https://polyhaven.com/a/dead_tree_trunk),
[Fern 02](https://polyhaven.com/a/fern_02),
[Nettle Plant](https://polyhaven.com/a/nettle_plant),
[Shrub 01](https://polyhaven.com/a/shrub_01),
[Shrub 02](https://polyhaven.com/a/shrub_02),
[Shrub 03](https://polyhaven.com/a/shrub_03),
[Shrub 04](https://polyhaven.com/a/shrub_04),
[Moss 01](https://polyhaven.com/a/moss_01),
[Celandine 01](https://polyhaven.com/a/celandine_01) and
[Dandelion 01](https://polyhaven.com/a/dandelion_01).

Concept art in `Docs/Concept` was generated with ChatGPT for reference.
