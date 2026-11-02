# Drone Warfare: Sandbox (Unity)

A real-time drone strategy prototype. You command a squad of drones from a top-down RTS camera:
select them, send them across the map, and hit enemy tanks, trucks, AA guns and radars.

It starts at a menu with two ways to play:

- **Asset Hunt** (missions): destroy a mission's targets with a fixed stock of drones. Each mission adds one
  idea, and you earn up to three stars for using few drones; finishing one unlocks the next.
- **Free play sandbox:** no win or lose. Destroyed enemies respawn after 30 seconds, and you can launch drones
  whenever you want (up to 30).

The world is built from code: green summer farmland in a valley ringed by wooded hills, with a river
between your base and enemy land, a bridge, dirt roads, villages with a church, patchwork fields (wheat,
young crops, ploughed soil, sunflowers, mown hay with bales), hedgerows and poplar rows. Your base is a
HESCO-walled drone yard with a Quonset hangar and launch pads; the enemy holds a fortified command post.
The layout follows the concept art in `Docs/Concept` (made with ChatGPT image generation).
Models are made by the Blender scripts in `Tools/Blender` (see below) and dressed in photo textures;
if a model is missing, the game falls back to simple shapes. Textures and the sky come from
[Poly Haven](https://polyhaven.com) (CC0, see Credits).

## Getting it running

This is a Unity 6 project (6000.6, Universal Render Pipeline).

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

Missions are data in `Missions.cs` (targets, defences, drone stock, the drone count for three stars), so new
ones need no new code. Best stars are kept per mission in the player's saved preferences.

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
| 1 / 2 / 3 | Launch a Scout / Strike / Kamikaze drone |
| Space | Select all drones |
| X | Stop |
| Esc | Deselect |
| H | Show or hide the help panel |

## Units

**Your drones**

| Drone | HP | Speed | Weapon | Notes |
| --- | --- | --- | --- | --- |
| Scout | 40 | fast | Machine gun, range 11 | Good against trucks; too short-ranged to hit AA safely |
| Strike | 70 | slow | Homing missiles, range 20, splash | Outranges a lone AA gun, but not one boosted by radar |
| Kamikaze | 25 | fastest | Blows itself up (big splash) | Only attacks when you order it to |

Gun and missile drones fire on enemies in range by themselves when they're idle.

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
| `MapLayout.cs` | The hand-made map: bases, defences, river, roads, villages, and where the fields go |
| `World.cs` | Builds the map from the layout: terrain, river, fields, villages, woods, bridge, bases |
| `Ground.cs` | Terrain: hills, river channel, roads, levelled sites, texture splatting, height queries |
| `Farmland.cs` | Fields with generated crop textures, hay bales, hedgerows, poplar rows |
| `Water.cs` | The river's surface (drawn by `Assets/DroneSandbox/Shaders/River.shader`: flowing ripples, see-through shallows, sky reflections, sun glints) |
| `Bases.cs` | Layout of your drone base and the enemy command post |
| `PathField.cs` | Fast "distance to the river / nearest road" lookups |
| `MapPreview.cs` | Shows the map in the editor without pressing Play |
| `Atmosphere.cs` | Sky, sun, haze and post-processing |
| `RtsController.cs` | Camera, selection and move/attack orders |
| `SandboxHud.cs` | On-screen HUD: health bars, selection, threat rings, launch buttons, help |
| `Drone.cs` | Drone types, flight and weapons |
| `EnemyUnit.cs` | Enemy types, patrolling and AA fire |
| `Unit.cs` | Health and team shared by all units |
| `Projectile.cs` | Bullets and homing missiles |
| `Combat.cs` | Target finding and blast damage |
| `Effects.cs` | Explosions, smoke, sparks, order markers, wrecks |
| `Art.cs` | Colours, materials, model loading (with photo materials) and primitive stand-ins |
| `GameInput.cs` | Keyboard and mouse for either input system |
| `AutoTest.cs` | The `-autotest` smoke test (sandbox battle, then missions) |

`Assets/DroneSandbox/Editor` adds the **Drone Sandbox** menu: *Build for Windows*, and
*Configure Project*, which sets player and shadow settings, creates the material assets the game
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
```

`make_world.py` builds the enemy vehicles, `make_base.py` the two bases and their props, and
`make_scenery.py` the trees, buildings, bridge and rocks. Add a preview folder as a second argument to also
render a PNG of each model, and a comma-separated list of names as a third to rebuild only some
(for example `Enemy_Tank` or `Scenery_Pine,Scenery_Spruce`). Models get UVs in real-world metres so photo
textures sit at the right scale, and static models are merged into one mesh each so the map stays fast.
Rotating parts are found by name: drone propellers are `Prop_*`, and turrets and radar heads hang under an
empty called `Turret`.

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
[Leafy Grass](https://polyhaven.com/a/leafy_grass) and
[Kloofendal 48d Partly Cloudy (Pure Sky)](https://polyhaven.com/a/kloofendal_48d_partly_cloudy_puresky).

Concept art in `Docs/Concept` was generated with ChatGPT for reference.
