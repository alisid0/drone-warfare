# Drone Warfare: Sandbox (Unity)

A real-time drone strategy prototype. You command a squad of drones from a top-down RTS camera:
select them, send them across the map, and hit enemy tanks, trucks, AA guns and radars.

This first version is a **bare sandbox**: there's no win or lose yet. Destroyed enemies respawn after
30 seconds, and you can launch more drones whenever you want (up to 30).

The world is built from code when you press Play, so there are no scenes or prefabs to set up:
rolling terrain with a valley for the battlefield, villages, woods, a hazy sky and filmic colour grading.
Drones, vehicles and scenery are low-poly models made by the Blender scripts in `Tools/Blender`
(see below); if a model is missing, the game falls back to simple shapes. Ground textures and the
sky come from [Poly Haven](https://polyhaven.com) (CC0, see Credits).

## Getting it running

This is a Unity 6 project (6000.6, Universal Render Pipeline).

1. Install **Unity Hub** and a **Unity 6** editor (6000.6.x) with Windows Build Support.
2. In Unity Hub: **Add → Add project from disk** and pick this folder.
3. Open it, open `Assets/Scenes/SampleScene`, and press **Play**. The sandbox builds itself in whatever scene is open.

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

## How the code is organised

All scripts are in `Assets/DroneSandbox/Scripts`:

| File | What it does |
| --- | --- |
| `Bootstrap.cs` | Starts the sandbox when you press Play |
| `SandboxGame.cs` | Builds the map, scenery, base and enemy forces; tracks units, score and respawns |
| `Ground.cs` | Terrain: hills, levelled sites for bases and defences, texture splatting, height queries |
| `Atmosphere.cs` | Sky, sun, haze and post-processing |
| `RtsController.cs` | Camera, selection and move/attack orders |
| `SandboxHud.cs` | On-screen HUD: health bars, selection, threat rings, launch buttons, help |
| `Drone.cs` | Drone types, flight and weapons |
| `EnemyUnit.cs` | Enemy types, patrolling and AA fire |
| `Unit.cs` | Health and team shared by all units |
| `Projectile.cs` | Bullets and homing missiles |
| `Combat.cs` | Target finding and blast damage |
| `Effects.cs` | Explosions, smoke, sparks, order markers, wrecks |
| `Art.cs` | Colours, materials, model loading and primitive stand-ins |
| `GameInput.cs` | Keyboard and mouse for either input system |
| `AutoTest.cs` | The `-autotest` smoke test |

`Assets/DroneSandbox/Editor` adds the **Drone Sandbox** menu: *Build for Windows*, and
*Configure Project*, which sets player and shadow settings and creates the material assets the game
loads at runtime (re-run it after adding new terrain textures or a new sky). Models live in
`Assets/DroneSandbox/Resources/Models` and are loaded by name (`Drone_Scout`, `Enemy_Tank`, ...).
Terrain textures are `Resources/Textures/Terrain/<grass|meadow|dirt|rock>_diff` and `_nor`, and the sky is
`Resources/Sky/sky.hdr`; drop in replacements with the same names and run *Configure Project*.

Unit stats (HP, speed, range, damage) are set in `Drone.Setup` and `EnemyUnit.Setup`, so balance
tweaks are one-line changes.

## Models (Blender)

The models are generated by Python scripts, so they can be tweaked and rebuilt instead of hand-edited.
With Blender 4.2 or newer:

```
blender --background --factory-startup --python Tools/Blender/make_drones.py -- Assets/DroneSandbox/Resources/Models
blender --background --factory-startup --python Tools/Blender/make_world.py -- Assets/DroneSandbox/Resources/Models
```

Add a preview folder as a second argument to also render a PNG of each model. `make_world.py` takes an
optional third argument, a comma-separated list of names, to rebuild only some models
(for example `Enemy_Tank,Scenery_Pine`). Rotating parts are found by name: drone propellers are
`Prop_*`, and turrets and radar heads hang under an empty called `Turret`.

## Next features to pick from

1. **Battery life.** Drones drain power while flying and must return to base to recharge.
2. **Fog of war.** You only see what your drones see, which makes scouts matter.
3. **Enemy fights back harder.** Jammers that cut drone control, enemy drones, attacks on your base.
4. **Missions with win/lose.** Destroy the HQ, defend the base, limited drone budget.
5. **Take control (FPV).** Jump into one drone and fly it yourself from its camera.
6. **Better vehicles and effects.** Swap in Asset Store vehicle packs, particle explosions and smoke, sound.
7. **Control groups and waypoints.** Ctrl+1-9 groups, shift-click to queue a patrol route.

## Credits

Terrain textures and sky from [Poly Haven](https://polyhaven.com), released under CC0:
[Aerial Grass Rock](https://polyhaven.com/a/aerial_grass_rock),
[Forrest Ground 01](https://polyhaven.com/a/forrest_ground_01),
[Forest Ground 04](https://polyhaven.com/a/forest_ground_04),
[Aerial Rocks 02](https://polyhaven.com/a/aerial_rocks_02) and
[Kloofendal 48d Partly Cloudy (Pure Sky)](https://polyhaven.com/a/kloofendal_48d_partly_cloudy_puresky).
