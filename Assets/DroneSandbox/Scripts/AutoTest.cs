using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace DroneSandbox
{
    // Scripted smoke test. Start the game with  -autotest <folder>  and it plays a short battle by itself,
    // saves screenshots plus a log of errors and stats into that folder, then quits.
    // With  -demo <folder>  it instead plays one mission like a player and records it frame by frame (Demo).
    // Without either flag this does nothing.
    public class AutoTest : MonoBehaviour
    {
        string outDir;
        StreamWriter log;
        int errors;
        bool demo;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Launch()
        {
            string[] args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-autotest"), d = Array.IndexOf(args, "-demo");
            if (d >= 0 && d + 1 < args.Length) new GameObject("Auto Test").AddComponent<AutoTest>().Begin(args[d + 1], true);
            else if (i >= 0 && i + 1 < args.Length) new GameObject("Auto Test").AddComponent<AutoTest>().Begin(args[i + 1], false);
        }

        void Begin(string folder, bool recordDemo)
        {
            SandboxGame.SaveProgress = false; // test runs don't touch the player's stars, points or purchases
            Tech.UseTestWallet();
            demo = recordDemo;
            outDir = folder;
            Directory.CreateDirectory(outDir);
            log = new StreamWriter(Path.Combine(outDir, "autotest.log")) { AutoFlush = true };
            Application.logMessageReceived += OnLog;
        }

        void OnDestroy()
        {
            Application.logMessageReceived -= OnLog;
            log?.Dispose();
        }

        void OnLog(string message, string stackTrace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            {
                errors++;
                log.WriteLine($"[{Time.time:F1}] {type}: {message}\n{stackTrace}");
            }
        }

        IEnumerator Start()
        {
            yield return new WaitForSeconds(1.5f);
            var game = SandboxGame.Instance;
            var rts = game.GetComponent<RtsController>();
            if (demo)
            {
                yield return Demo(game, rts);
                log.WriteLine($"Demo done: {recorded} frames, {game.Result} ({game.ResultReason}), drones used {game.DronesUsed}. Errors: {errors}");
                yield return new WaitForSecondsRealtime(0.5f);
                Application.Quit();
                yield break;
            }
            yield return Shot("00_menu");
            Sound.ExportReel(Path.Combine(outDir, "sound_reel.wav"));
            log.WriteLine("Sound reel written");
            game.StartSandbox();
            Recon.ForceShowAll = true; // the model close-ups first, everything drawn
            yield return new WaitForSeconds(0.5f);
            log.WriteLine($"Started. Drones: {game.drones.Count}, enemies: {game.enemies.Count}, fields: {MapLayout.Fields.Count}, " +
                          $"screen {Screen.width}x{Screen.height}");

            yield return Shot("01_start");

            // Close-up of the drones at the base, to check the models.
            rts.LookAt(game.basePosition, 18f, 30f);
            yield return new WaitForSeconds(2.5f);
            yield return Shot("02_drones_closeup");
            rts.LookAt(game.basePosition, 42f, 20f);
            yield return new WaitForSeconds(0.5f);
            yield return Shot("02a_base");

            // Close-up of the enemy HQ, to check the vehicle models, terrain and sky.
            rts.LookAt(game.enemyHqPosition, 38f, 210f);
            yield return new WaitForSeconds(1f);
            yield return Shot("02b_enemy_closeup");

            // The river crossing, a village and the farmland.
            var bridge = MapLayout.BridgePoint;
            rts.LookAt(new Vector3(bridge.x, 0f, bridge.y), 40f, 135f);
            yield return new WaitForSeconds(0.5f);
            yield return Shot("02c_bridge");
            rts.LookAt(new Vector3(-100f, 0f, 0f), 26f, 150f);
            yield return new WaitForSeconds(0.5f);
            yield return Shot("02f_river");
            var village = MapLayout.Villages[0];
            rts.LookAt(new Vector3(village.x, 0f, village.y), 45f, 200f);
            yield return new WaitForSeconds(0.5f);
            yield return Shot("02d_village");
            rts.LookAt(new Vector3(-20f, 0f, -30f), 100f, 40f);
            yield return new WaitForSeconds(0.5f);
            yield return Shot("02e_farmland");

            // The fictional sides: a flag at a base gate, and the emblem on an enemy tank.
            var flags = UnityEngine.Object.FindObjectsByType<Flag>();
            if (flags.Length > 0)
            {
                rts.LookAt(flags[0].transform.position, 22f, 30f);
                yield return new WaitForSeconds(0.5f);
                yield return Shot("02g_flag");
            }
            EnemyUnit emblemTank = Nearest(EnemyType.Tank, game.enemyHqPosition);
            if (emblemTank != null)
            {
                rts.LookAt(emblemTank.transform.position, 12f, emblemTank.transform.eulerAngles.y + 90f);
                yield return new WaitForSeconds(0.3f);
                yield return Shot("02h_tank_emblem");
            }
            rts.LookAt(game.basePosition, 30f, 300f);
            yield return new WaitForSeconds(0.3f);
            yield return Shot("02i_base_emblem");

            // The Nightjar heavy bomber lifting off (free play has all the tech).
            var liftOff = game.SpawnDrone(DroneType.Bomber);
            if (liftOff != null)
            {
                rts.LookAt(liftOff.transform.position, 14f, 30f);
                yield return new WaitForSeconds(0.3f);
                yield return Shot("02j_nightjar");
            }

            // Send everyone toward the forward AA post and follow them, with the fog of war on.
            Recon.ForceShowAll = false;
            Vector3 staging = new Vector3(-32f, 0f, -26f);
            rts.selected.Clear();
            rts.selected.AddRange(game.drones);
            foreach (var d in game.drones) d.MoveTo(staging);
            for (float t = 0f; t < 6f; t += 0.5f)
            {
                rts.LookAt(Vector3.Lerp(game.basePosition, staging, t / 6f), 45f, 45f);
                yield return new WaitForSeconds(0.5f);
            }
            yield return Shot("03_moving");

            // Strike drones hit the AA gun, scouts the nearest vehicle, kamikazes dive on a tank.
            EnemyUnit aa = Nearest(EnemyType.AAGun, staging);
            EnemyUnit vehicle = Nearest(EnemyType.Truck, staging) ?? Nearest(EnemyType.Tank, staging);
            EnemyUnit tank = Nearest(EnemyType.Tank, staging);
            foreach (var d in game.drones)
            {
                if (d.type == DroneType.Strike && aa != null) d.Attack(aa);
                else if (d.type == DroneType.Scout && vehicle != null) d.Attack(vehicle);
                else if (d.type == DroneType.Kamikaze && tank != null) d.Attack(tank);
                else if (d.type == DroneType.Bomber && vehicle != null) d.Attack(vehicle);
            }
            rts.LookAt(aa != null ? aa.transform.position : staging, 40f, 45f);
            yield return new WaitForSeconds(3f);
            yield return Shot("04_engaging");
            yield return new WaitForSeconds(4f);
            yield return Shot("05_battle");
            if (tank != null) rts.LookAt(tank.transform.position, 35f, 45f);
            yield return new WaitForSeconds(4f);
            yield return Shot("06_battle_later");

            // Orders: an attack-move with two waypoints queued after it, then the tactical pause.
            rts.selected.Clear();
            rts.selected.AddRange(game.drones);
            Vector3 sweep = staging + new Vector3(-20f, 0f, 18f);
            foreach (var d in game.drones)
            {
                d.MoveTo(sweep, false, true);
                d.MoveTo(sweep + new Vector3(15f, 0f, 15f), true);
                d.MoveTo(game.basePosition + new Vector3(8f, 0f, 8f), true);
            }
            rts.LookAt(sweep, 60f, 45f);
            yield return new WaitForSeconds(1.5f);
            yield return Shot("06a_orders_route");
            game.TogglePause();
            yield return new WaitForSecondsRealtime(0.4f);
            yield return Shot("06b_tactical_pause");
            log.WriteLine($"Orders: paused {game.Paused}, timeScale {Time.timeScale}, first drone '{(game.drones.Count > 0 ? game.drones[0].Status : "-")}'");
            game.TogglePause();

            // Wide view of the whole map.
            rts.LookAt(new Vector3(10f, 0f, 10f), 110f, 45f);
            yield return new WaitForSeconds(1f);
            yield return Shot("07_overview");
            var overlay = game.GetComponent<SandboxHud>();
            overlay.ToggleTelemetry();
            yield return new WaitForSecondsRealtime(2.2f);
            yield return Shot("07c_telemetry");
            overlay.ToggleTelemetry();

            log.WriteLine($"Sandbox done at {Time.time:F1}s. Destroyed: {game.enemiesDestroyed}, lost: {game.dronesLost}, " +
                          $"expended: {game.dronesExpended}, drones left: {game.drones.Count}, " +
                          $"fps: {1f / Mathf.Max(Time.smoothDeltaTime, 0.0001f):F0}");

            // Dusk, night and thermal view over the base and the valley.
            var dayNight = DayNight.Instance;
            dayNight.SetTime(20.2f, 0f);
            rts.LookAt(game.basePosition + new Vector3(25f, 0f, 25f), 75f, 45f);
            yield return new WaitForSeconds(1f);
            yield return Shot("08a_dusk");
            dayNight.SetTime(23.5f, 0f);
            yield return new WaitForSeconds(1f);
            yield return Shot("08b_night");
            rts.LookAt(game.enemyHqPosition, 45f, 210f);
            yield return new WaitForSeconds(0.5f);
            yield return Shot("08c_night_hq");
            if (game.SpawnDrone(DroneType.Scout) != null) game.ToggleThermal();
            rts.LookAt(new Vector3(10f, 0f, 10f), 90f, 45f);
            yield return new WaitForSeconds(1f);
            log.WriteLine($"Thermal on: {dayNight.Thermal} at {dayNight.Clock}");
            yield return Shot("08d_thermal");
            dayNight.SetThermal(false);
            dayNight.SetTime(10f, 1f);

            yield return BeautyShots();
            Recon.ForceShowAll = true;
            yield return ExplosionShots(game, rts);
            yield return UnitShots(game, rts);
            Recon.ForceShowAll = false;
            yield return EmptyAndRearm(game, rts);
            yield return PlayMissions(game, rts);
            yield return SectorTour(game, rts);

            log.WriteLine($"Done at {Time.realtimeSinceStartup:F1}s real time. Errors: {errors}");
            yield return new WaitForSecondsRealtime(0.5f);
            Application.Quit();
        }

        // Photo mode at the best light: the spillway from down in the gorge, the lake from above the dam at sunset,
        // the river at the bridge, the whole valley, and the dam's lamps at night.
        IEnumerator BeautyShots()
        {
            var photo = PhotoMode.Instance;
            var dayNight = DayNight.Instance;
            Vector2 dam = MapLayout.DamPoint, down = MapLayout.DamDown, across = MapLayout.DamAcross;
            float river = MapLayout.RiverField.values[0], lake = MapLayout.LakeLevel;
            Vector3 At(Vector2 p, float y) => new Vector3(p.x, y, p.y);
            // A camera position moved sideways, to the right of the way it looks.
            Vector3 Right(Vector3 from, Vector3 to, float metres)
            {
                var f = new Vector3(to.x - from.x, 0f, to.z - from.z).normalized;
                return from + new Vector3(f.z, 0f, -f.x) * metres;
            }
            Vector3 shoreFrom = At(dam - down * 50f + across * 4f, lake + 3f), shoreTo = At(dam - down * 75f + across * 55f, lake + 6f);

            var shots = new (string name, float hour, Vector3 from, Vector3 to, float zoom)[]
            {
                // A three-quarter view of the spillway, from high enough to see down onto its steps.
                ("20_photo_dam_morning", 6.0f, At(dam + down * 40f + across * 20f, river + 13f), At(dam + down * 4f, river + 4f), 45f),
                ("21_photo_lake_evening", 20.0f, At(dam + down * 165f - across * 30f, lake + 90f), At(dam - down * 55f + across * 30f, lake), 47f),
                ("22_photo_bridge", 8.5f, At(MapLayout.BridgePoint + new Vector2(20f, 24f), 5f), At(MapLayout.BridgePoint, 1f), 45f),
                ("23_photo_valley", 19.3f, new Vector3(75f, 70f, -150f), new Vector3(-35f, 0f, 0f), 60f),
                ("24_photo_dam_night", 22.8f, At(dam + down * 32f + across * 26f, lake + 5f), At(dam, lake), 55f),
                ("25_photo_lake_shore", 8f, Right(shoreFrom, shoreTo, 4f), shoreTo, 50f),
            };
            foreach (var shot in shots)
            {
                dayNight.SetTime(shot.hour, 0f);
                photo.PlaceLooking(shot.from, shot.to, shot.zoom);
                var hidden = ClearView(shot.from, shot.to);
                yield return new WaitForSeconds(1.2f);
                yield return Shot(shot.name);
                foreach (var r in hidden) if (r != null) r.enabled = true;
            }

            // Two short clips, frame by frame (made into GIFs outside the game), to see the grass sway in the wind
            // and the river run: the meadow by the bridge, and the river a little downstream from a low drone.
            var line = MapLayout.RiverField.points;
            int near = 0;
            for (int i = 1; i < line.Count; i++)
                if ((line[i] - MapLayout.BridgePoint).sqrMagnitude < (line[near] - MapLayout.BridgePoint).sqrMagnitude) near = i;
            int at = Mathf.Clamp(near + 14, 1, line.Count - 2);
            Vector2 flow = (line[at + 1] - line[at - 1]).normalized, bank = new Vector2(-flow.y, flow.x);
            float water = MapLayout.RiverField.values[at];
            var clips = new (string name, Vector3 from, Vector3 to)[]
            {
                ("40_clip_grass", At(MapLayout.BridgePoint + new Vector2(20f, 24f), 5f), At(MapLayout.BridgePoint, 1f)),
                ("41_clip_river", At(line[at] + bank * 12f - flow * 8f, water + 12f), At(line[at] + flow * 6f, water)),
            };
            dayNight.SetTime(8.5f, 0f);
            foreach (var clip in clips)
            {
                photo.PlaceLooking(clip.from, clip.to, 45f);
                var hidden = ClearView(clip.from, clip.to);
                yield return new WaitForSeconds(1.2f);
                for (int frame = 0; frame < 16; frame++)
                {
                    yield return Shot($"{clip.name}_{frame:00}");
                    yield return new WaitForSecondsRealtime(0.1f);
                }
                foreach (var r in hidden) if (r != null) r.enabled = true;
            }
            photo.Exit();
            dayNight.SetTime(10f, 1f);
            log.WriteLine($"Photo mode: {shots.Length} shots, dam at {dam}, lake level {lake}, river at the dam {river:F1}");
        }

        // Explosions from the game camera zoomed right in, on open ground by the base: a blast frame by frame (made
        // into a GIF outside the game), a tank blown up and left burning, and a blast at night for its flash.
        IEnumerator ExplosionShots(SandboxGame game, RtsController rts)
        {
            var dayNight = DayNight.Instance;
            Vector3 spot = Ground.OnGround(OpenGroundNear(game.basePosition));
            dayNight.SetTime(10f, 0f);
            rts.LookAt(spot, 24f, 30f);
            yield return new WaitForSeconds(0.5f);
            Effects.Explosion(spot + Vector3.up, 5f);
            for (int frame = 0; frame < 30; frame++)
            {
                yield return Shot($"50_clip_blast_{frame:00}");
                yield return new WaitForSeconds(0.1f);
            }

            var tank = game.SpawnEnemy(EnemyType.Tank, spot + new Vector3(6f, 0f, -4f), 30f);
            yield return new WaitForSeconds(0.3f);
            tank.TakeDamage(10000f);
            yield return new WaitForSeconds(1.2f);
            yield return Shot("51_wreck_burning");
            yield return new WaitForSeconds(4f);
            yield return Shot("52_wreck_smoke");

            dayNight.SetTime(23f, 0f);
            yield return new WaitForSeconds(1f);
            Effects.Explosion(spot + new Vector3(-6f, 1f, 5f), 5f);
            foreach (float wait in new[] { 0.05f, 0.25f, 0.8f })
            {
                yield return new WaitForSeconds(wait);
                yield return Shot($"53_blast_night_{wait:0.00}");
            }
            dayNight.SetTime(10f, 1f);
            log.WriteLine($"Explosions at {spot}: shots done, {FindObjectsByType<Effects>().Length} effects alive");
        }

        // The wreck of every kind of vehicle in a line-up, once their fires are out, then each close up; and each drone
        // close up in flight (lights, propeller blur) and all four from the RTS camera. The vehicles are wrecked
        // straight away rather than killed, so nothing respawns here later.
        IEnumerator UnitShots(SandboxGame game, RtsController rts)
        {
            Vector3 spot = Ground.OnGround(OpenGroundNear(game.basePosition, 70f)); // well clear of trees and their shadows
            var kinds = new[] { EnemyType.Tank, EnemyType.APC, EnemyType.Truck, EnemyType.Tanker, EnemyType.Rocket,
                                EnemyType.Jammer, EnemyType.AAGun, EnemyType.Radar, EnemyType.Artillery, EnemyType.Depot };
            var spots = new Vector3[kinds.Length];
            var parked = new EnemyUnit[kinds.Length];
            for (int i = 0; i < kinds.Length; i++)
            {
                spots[i] = Ground.OnGround(spot + new Vector3((i % 5 - 2) * 8f, 0f, (i / 5 - 0.5f) * 9f));
                parked[i] = game.SpawnEnemy(kinds[i], spots[i], 30f + i * 25f);
                parked[i].enabled = false; // parked for the camera: no driving off, no shooting
            }
            rts.LookAt(spot, 45f, 20f);
            yield return new WaitForSeconds(1f);
            yield return Shot("53_vehicles_all");
            rts.enabled = false; // hold the camera by hand for the close-ups
            var cam = Camera.main.transform;
            Light sun = null; // the brightest directional light (the moon's is off by day)
            foreach (var light in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (light.type == LightType.Directional && light.isActiveAndEnabled && (sun == null || light.intensity > sun.intensity)) sun = light;
            Vector3 sunward = sun != null ? Vector3.ProjectOnPlane(-sun.transform.forward, Vector3.up).normalized : Vector3.back;
            sunward = Quaternion.Euler(0f, 30f, 0f) * sunward; // a little off the sun's line, so the shading shows the shapes
            for (int i = 0; i < kinds.Length; i++)
            {
                var bounds = new Bounds(spots[i], Vector3.zero);
                foreach (var r in parked[i].GetComponentsInChildren<Renderer>()) bounds.Encapsulate(r.bounds);
                float reach = Mathf.Max(4f, bounds.extents.magnitude * 2.2f);
                cam.position = bounds.center + sunward * reach + Vector3.up * reach * 0.6f; // low, from the sunny side, to see the mud
                cam.LookAt(bounds.center);
                yield return new WaitForEndOfFrame();
                yield return Shot($"53_vehicle_{kinds[i]}");
            }
            rts.enabled = true;

            foreach (var enemy in parked) // then the same vehicles as wrecks
            {
                Effects.Wreck(enemy);
                game.enemies.Remove(enemy);
                Destroy(enemy.gameObject);
            }
            rts.LookAt(spot, 45f, 20f);
            yield return new WaitForSeconds(13f); // flames out, smoke gone
            yield return Shot("54_wrecks_all");
            rts.enabled = false;
            for (int i = 0; i < kinds.Length; i++)
            {
                cam.position = spots[i] + Quaternion.Euler(0f, 35f, 0f) * new Vector3(0f, 10f, -4.5f); // steeply down, under the canopies' reach
                cam.LookAt(spots[i] + Vector3.up * 0.5f);
                yield return new WaitForEndOfFrame();
                yield return Shot($"55_wreck_{kinds[i]}");
            }
            rts.enabled = true;

            var types = new[] { DroneType.Scout, DroneType.Strike, DroneType.Kamikaze, DroneType.Bomber };
            var flying = new List<Drone>();
            foreach (var type in types)
            {
                var drone = game.SpawnDrone(type);
                if (drone == null) continue;
                drone.MoveTo(spot + new Vector3((flying.Count - 1.5f) * 6f, 0f, 0f));
                flying.Add(drone);
            }
            yield return new WaitForSeconds(6f);
            rts.LookAt(spot, 25f, 20f);
            yield return new WaitForSeconds(0.3f);
            yield return Shot("57_drones_rts");
            rts.enabled = false;
            foreach (var drone in flying)
            {
                float reach = drone.radius * 3.2f;
                Vector3 at = drone.transform.position;
                cam.position = at + Quaternion.Euler(0f, 150f, 0f) * new Vector3(0f, reach * 0.8f, reach * 0.8f); // from above, the field behind
                cam.LookAt(at);
                yield return new WaitForEndOfFrame();
                yield return Shot($"56_drone_{drone.type}");
            }
            rts.enabled = true;
            log.WriteLine($"Units: {kinds.Length} vehicles and wrecks, {flying.Count} drones shot");
            foreach (var drone in flying)
            {
                game.drones.Remove(drone);
                Destroy(drone.gameObject);
            }
        }

        // The nearest dry spot (going out in rings) with no tree within clearance metres, so the camera sees the ground.
        static Vector3 OpenGroundNear(Vector3 from, float clearance = 25f)
        {
            for (float reach = 30f; reach < 300f; reach += 15f)
                for (float angle = 0f; angle < 360f; angle += 20f)
                {
                    var p = new Vector2(from.x, from.z) + new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad)) * reach;
                    bool dry = MapLayout.LakeDistance(p.x, p.y) > 10f && MapLayout.RiverField.Distance(p.x, p.y) > MapLayout.RiverHalfWidth + 8f;
                    if (dry && !Undergrowth.UnderTrees(p, clearance)) return new Vector3(p.x, 0f, p.y);
                }
            return from;
        }

        // A tour of generated sectors: the theatre map, then a dozen kinds of sector, each built, photographed from
        // above and from a drone's height, with one of its missions briefed and started through the theatre path,
        // so a broken generator shows up as errors, odd numbers in the log or odd pictures.
        IEnumerator SectorTour(SandboxGame game, RtsController rts)
        {
            game.OpenMenu();
            var hud = game.GetComponent<SandboxHud>();
            hud.TheatreOpen = true;
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Shot("30_theatre");
            hud.TheatreOpen = false;

            var kinds = new[]
            {
                SectorKind.RiverValley, SectorKind.DeepForest, SectorKind.Marshland, SectorKind.TownOutskirts,
                SectorKind.IndustrialZone, SectorKind.RailJunction, SectorKind.Airfield, SectorKind.QuarryHills,
                SectorKind.LakeDistrict, SectorKind.Reservoir, SectorKind.BorderLine, SectorKind.Lakeshore,
            };
            var photo = PhotoMode.Instance;
            for (int n = 0; n < kinds.Length; n++)
            {
                var kind = kinds[n];
                int seed = 1000 + n * 37;
                var situation = (Situation)((n * 7 + 3) % Situations.Names.Length);
                while (!Situations.Fits(situation, SectorTypes.Get(kind))) situation = (Situation)(((int)situation + 1) % Situations.Names.Length);
                var hotspot = new Hotspot { kind = kind, seed = seed, situation = situation, daylight = Daylight.Day, weather = n == 6 ? Weather.Rain : n == 10 ? Weather.Fog : n % 4 == 3 ? Weather.Overcast : Weather.Clear };
                float started = Time.realtimeSinceStartup;
                game.PlayHotspot(hotspot);
                yield return null;
                while (game.Loading) yield return null;
                float took = Time.realtimeSinceStartup - started;
                var plan = MapLayout.Plan;
                log.WriteLine($"Sector {n}: {plan.name} ({SectorTypes.Get(kind).name}, seed {seed}) built in {took:F1}s: " +
                              $"{plan.roads.Count} roads, {plan.villages.Count} villages, {plan.bridges.Count} bridges, {plan.ponds.Count} pools, " +
                              $"{plan.railways.Count} railways, {plan.sites.Count} sites, {MapLayout.Fields.Count} fields; " +
                              $"{Situations.Name(situation)}: {game.Mission.units.Count} units, {game.TargetsTotal} targets, " +
                              $"convoy route {(game.Mission.convoyRoute != null ? game.Mission.convoyRoute.Count : 0)} points, " +
                              $"{game.Mission.units.FindAll(u => Undergrowth.UnderTrees(new Vector2(u.position.x, u.position.z), 4f)).Count} under trees, " +
                              $"{Stacked(game.Mission.units)} stacked");
                game.CloseBriefing();
                yield return null;
                var stack = UnityEngine.Rendering.VolumeManager.instance.stack;
                UnityEngine.Rendering.VolumeManager.instance.Update(stack, Camera.main.transform, ~0);
                var fog = stack.GetComponent<UnityEngine.Rendering.HighDefinition.Fog>();
                var clouds = stack.GetComponent<UnityEngine.Rendering.HighDefinition.VolumetricClouds>();
                log.WriteLine($"  weather {hotspot.weather}: fog {fog.enabled.value} {fog.meanFreePath.value:F0} m up to {fog.maximumHeight.value:F0} m, " +
                              $"clouds {clouds.enable.value} {clouds.cloudPreset}, sun x{Atmosphere.SunScale}");

                // From high over your base, looking across the sector to the enemy.
                Vector3 baseAt = game.basePosition, hq = game.enemyHqPosition;
                Vector3 back = (baseAt - hq).normalized;
                DayNight.Instance.SetTime(10.5f, 0f);
                photo.PlaceLooking(baseAt + back * 60f + Vector3.up * 140f, (baseAt + hq) / 2f, 55f);
                yield return new WaitForSeconds(1.5f);
                yield return Shot($"31_sector_{n:00}_{kind}");
                photo.Exit();
                // The mission's targets from the game camera.
                DayNight.Instance.SetTime(game.Mission.startHour, 0f);
                rts.LookAt(game.Mission.cameraFocus, 70f, game.Mission.cameraYaw);
                yield return new WaitForSeconds(2f);
                yield return Shot($"32_mission_{n:00}_{situation}");
            }
            game.OpenMenu();
        }

        // How many of a mission's units stand within 3 m of another (placed on top of each other).
        static int Stacked(List<MissionUnit> units)
        {
            int count = 0;
            for (int i = 0; i < units.Count; i++)
                for (int j = 0; j < units.Count; j++)
                    if (i != j && Vector3.Distance(units[i].position, units[j].position) < 3f) { count++; break; }
            return count;
        }

        // Hides the trees, plants and rocks right in front of the lens (around the camera or across the first stretch
        // of its view), as a photographer would step round a branch. Returns them to show again.
        static List<Renderer> ClearView(Vector3 from, Vector3 to)
        {
            const float Reach = 14f, Margin = 1.5f;
            var hidden = new List<Renderer>();
            Vector3 dir = (to - from).normalized;
            foreach (var r in FindObjectsByType<Renderer>())
            {
                if (!r.enabled || !(r is MeshRenderer)) continue;
                var b = r.bounds;
                if (b.size.magnitude > 60f) continue; // the dam, water, buildings' bigger parts
                Vector3 nearest = from + dir * Mathf.Clamp(Vector3.Dot(b.center - from, dir), 0f, Reach);
                if (b.SqrDistance(nearest) > Margin * Margin) continue;
                if (r.GetComponentInParent<LODGroup>() == null && !r.name.StartsWith("Scan_")) continue; // trees and scans only
                r.enabled = false;
                hidden.Add(r);
            }
            return hidden;
        }

        // The Nightjar bombs the nearest enemies until its bay is empty, then has to fly home and rearm.
        IEnumerator EmptyAndRearm(SandboxGame game, RtsController rts)
        {
            var nightjar = game.drones.Find(d => d.IsBomber) ?? game.SpawnDrone(DroneType.Bomber);
            if (nightjar == null)
            {
                log.WriteLine("Nightjar: could not launch");
                yield break;
            }
            bool shot = false;
            float t = 0f;
            for (; t < 70f && nightjar != null && nightjar.BombsLeft > 0; t += 0.5f)
            {
                if (nightjar.AttackTarget == null)
                {
                    var next = NearestVisible(nightjar.transform.position);
                    if (next != null) nightjar.Attack(next);
                }
                rts.LookAt(nightjar.transform.position, 40f, 45f);
                if (!shot && nightjar.BombsLeft < Drone.BomberLoad)
                {
                    shot = true;
                    yield return new WaitForSeconds(0.4f);
                    yield return Shot("07a_nightjar_bombing");
                }
                yield return new WaitForSeconds(0.5f);
            }
            if (nightjar == null)
            {
                log.WriteLine($"Nightjar: shot down after {t:F0}s");
                yield break;
            }
            log.WriteLine($"Nightjar: bombs {nightjar.BombsLeft}/{Drone.BomberLoad} after {t:F0}s, {nightjar.Status}, " +
                          $"hp {nightjar.hp:F0}/{nightjar.maxHp:F0}");
            for (t = 0f; t < 60f && nightjar != null && nightjar.BombsLeft == 0; t += 0.5f)
            {
                rts.LookAt(nightjar.transform.position, 30f, 45f);
                yield return new WaitForSeconds(0.5f);
            }
            if (nightjar != null)
            {
                log.WriteLine($"Nightjar: rearmed {nightjar.BombsLeft}/{Drone.BomberLoad} after {t:F0}s more, {nightjar.Status}");
                yield return Shot("07b_nightjar_rearmed");
            }
            else
            {
                log.WriteLine("Nightjar: shot down on the way home");
            }
        }

        // Plays mission 1 to the end with Kamikaze drones (with no tech bought), opens the tech shop and buys
        // everything, flies the night Convoy with the Nightjar and thermal view, checks spotting in Hidden Guns,
        // and looks at the Command Post target. Missions pause the game behind their panels, so waits here use
        // real time.
        IEnumerator PlayMissions(SandboxGame game, RtsController rts)
        {
            game.StartMission(0);
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Shot("08_briefing");
            game.CloseBriefing();
            game.ToggleThermal();
            log.WriteLine($"Mission 1 thermal with no tech: {DayNight.Instance.Thermal} (\"{game.Message}\")");
            // Recon: a Scout flies out over the rough contacts until the trucks are found.
            var targets = game.enemies.FindAll(e => e.isTarget);
            log.WriteLine($"Mission 1 phase at start: {game.Phase}, target intel {(targets.Count > 0 ? targets[0].Intel.ToString() : "-")}");
            var recon = game.SpawnDrone(DroneType.Scout);
            if (recon != null)
            {
                recon.MoveTo(new Vector3(-30f, 0f, -85f)); // south of the AA gun's ring
                foreach (var site in game.enemies.FindAll(e => e.isTarget || e.isDecoy)) recon.MoveTo(site.ContactPosition, true);
            }
            rts.LookAt(targets.Count > 0 ? targets[0].transform.position : game.basePosition, 75f, 70f);
            for (float t = 0f; t < 60f && game.Phase == MissionPhase.Recon && game.Result == MissionResult.None; t += 0.5f)
                yield return new WaitForSecondsRealtime(0.5f);
            log.WriteLine($"Mission 1 recon: phase {game.Phase} after {game.MissionTime:F1}s");
            yield return Shot("08e_first_light_strike");

            // Strike: a Kamikaze at each truck, the first one's camera feed on screen as it dives.
            rts.selected.Clear();
            foreach (var target in targets)
            {
                var drone = game.SpawnDrone(DroneType.Kamikaze);
                if (drone != null && target != null) drone.Attack(target);
                if (drone != null && rts.selected.Count == 0) rts.selected.Add(drone);
            }
            yield return new WaitForSecondsRealtime(2.5f);
            yield return Shot("08g_kamikaze_feed");
            for (float t = 0f; t < 30f && game.Phase != MissionPhase.Extract && game.Result == MissionResult.None; t += 0.5f)
                yield return new WaitForSecondsRealtime(0.5f);
            yield return Shot("08f_first_light_extract");

            // Extract: bring the Scout home.
            game.Recall(new List<Drone>());
            rts.LookAt(game.basePosition, 70f, 70f);
            for (float t = 0f; t < 40f && game.Result == MissionResult.None; t += 0.5f)
                yield return new WaitForSecondsRealtime(0.5f);
            log.WriteLine($"Mission 1: {game.Result}, {game.ResultReason} Stars {game.Stars}, drones used {game.DronesUsed}, " +
                          $"lost {game.dronesLost}, home {game.DronesHome}, targets {game.TargetsDestroyed}/{game.TargetsTotal}, time {game.MissionTime:F1}s, " +
                          $"points +{game.PointsEarned} (stars {game.StarBonus}), total {Tech.Points}");
            yield return new WaitForSecondsRealtime(2f);
            yield return Shot("09_mission_complete");

            // The tech shop with the points from mission 1, then with everything bought.
            game.OpenMenu();
            var hud = game.GetComponent<SandboxHud>();
            hud.ShopOpen = true;
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Shot("09a_tech_shop");
            Tech.Earn(1000);
            int bought = 0;
            foreach (var item in Tech.Items)
                if (Tech.Buy(item)) bought++;
            yield return new WaitForSecondsRealtime(0.3f);
            yield return Shot("09b_tech_shop_bought");
            hud.ShopOpen = false;
            log.WriteLine($"Tech shop: bought {bought}/{Tech.Items.Length}, {Tech.Points} points left");

            // Convoy at night: the Nightjar gives thermal view and bombs the fuel trucks one by one.
            game.StartMission(3);
            log.WriteLine($"Convoy stock: Scout {game.StockLeft(DroneType.Scout)}, Strike {game.StockLeft(DroneType.Strike)}, " +
                          $"Kamikaze {game.StockLeft(DroneType.Kamikaze)}, Nightjar {game.StockLeft(DroneType.Bomber)}");
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Shot("11a_convoy_briefing");
            game.CloseBriefing();
            var bomber = game.SpawnDrone(DroneType.Bomber);
            game.ToggleThermal();
            log.WriteLine($"Convoy: Nightjar launched {bomber != null}, thermal {DayNight.Instance.Thermal} at {DayNight.Instance.Clock}");
            var trucks = game.enemies.FindAll(e => e.isTarget);
            bool thermalShot = false, bombShot = false;
            float flown = 0f;
            for (; flown < 90f && game.Result == MissionResult.None && bomber != null; flown += 0.5f)
            {
                if (bomber.AttackTarget == null && bomber.BombsLeft > 0)
                {
                    var next = trucks.Find(e => e != null && e.IsAlive);
                    if (next != null) bomber.Attack(next);
                }
                rts.LookAt(bomber.transform.position, 50f, 60f);
                if (!thermalShot && flown >= 8f)
                {
                    thermalShot = true;
                    yield return Shot("11b_nightjar_thermal");
                }
                if (!bombShot && bomber.BombsLeft < Drone.BomberLoad)
                {
                    bombShot = true;
                    yield return new WaitForSecondsRealtime(0.5f);
                    yield return Shot("11c_nightjar_bombing");
                }
                yield return new WaitForSecondsRealtime(0.5f);
            }
            log.WriteLine($"Convoy: {game.Result}, {game.ResultReason} after {flown:F0}s, targets {game.TargetsDestroyed}/{game.TargetsTotal}, " +
                          (bomber != null ? $"Nightjar bombs {bomber.BombsLeft}, {bomber.Status}" : "Nightjar lost") +
                          $", points +{game.PointsEarned}");
            yield return new WaitForSecondsRealtime(2f);
            yield return Shot("11d_convoy_result");

            game.StartMission(2);
            game.CloseBriefing();
            var hidden = game.enemies.FindAll(e => e.Hidden);
            rts.LookAt(new Vector3(-10f, 0f, 30f), 105f, 30f);
            yield return new WaitForSecondsRealtime(1f);
            yield return Shot("10_search_areas");
            if (hidden.Count > 0)
            {
                var scout = game.SpawnDrone(DroneType.Scout);
                if (scout != null) scout.MoveTo(hidden[0].searchCenter);
                for (float t = 0f; t < 25f && hidden[0] != null && hidden[0].Hidden; t += 0.5f)
                    yield return new WaitForSecondsRealtime(0.5f);
                bool spotted = hidden[0] != null && !hidden[0].Hidden;
                log.WriteLine($"Hidden Guns: {hidden.Count} hidden targets, first spotted by a Scout: {spotted}");
                if (hidden[0] != null) rts.LookAt(hidden[0].transform.position, 30f, 200f);
                yield return new WaitForSecondsRealtime(1f);
                yield return Shot("11_spotted");
            }

            game.StartMission(4);
            game.CloseBriefing();
            rts.LookAt(game.enemyHqPosition, 55f, 210f);
            yield return new WaitForSecondsRealtime(1f);
            yield return Shot("12_command_post");
            log.WriteLine($"Command Post: {game.TargetsTotal} target, {game.enemies.Count} enemies");

            game.OpenMenu();
            yield return new WaitForSecondsRealtime(1f);
        }

        // ---- Gameplay recording (-demo) ----

        // Plays one generated mission the way a player would, from the menu and the theatre map through the briefing
        // to the result: launches drones in waves, sends them at the column, and follows the fight with the camera.
        // Every frame is saved (Record) at a steady 30 frames per second of game time, however slowly the machine
        // draws them, to be made into a video outside the game.
        const int DemoFps = 30;
        bool recording;
        int recorded;
        Vector3 camFocus;
        float camDistance, camYaw;

        IEnumerator Demo(SandboxGame game, RtsController rts)
        {
            Tech.Earn(1000);
            foreach (var item in Tech.Items) Tech.Buy(item); // the whole arsenal, the Nightjar included
            var hud = game.GetComponent<SandboxHud>();
            Time.captureFramerate = DemoFps;
            recording = true;
            StartCoroutine(Record());

            // The main menu, then the theatre map.
            yield return Hold(3f);
            hud.TheatreOpen = true;
            yield return Hold(4f);
            hud.TheatreOpen = false;

            // Quenfield, a farmland river valley: an armoured column is driving for the bridge.
            var hotspot = new Hotspot { kind = SectorKind.RiverValley, seed = 1000, situation = Situation.BridgeCrossing, daylight = Daylight.Day, weather = Weather.Clear };
            hotspot.sectorName = hotspot.Plan().name;
            game.PlayHotspot(hotspot);
            while (game.Loading) yield return null;
            yield return Hold(5f); // reading the briefing
            game.CloseBriefing();
            hud.showHelp = false;

            var targets = game.enemies.FindAll(e => e.isTarget);
            var aa = game.enemies.Find(e => e.type == EnemyType.AAGun);
            camFocus = Ground.OnGround(game.Mission.cameraFocus);
            camDistance = game.Mission.cameraDistance;
            camYaw = game.Mission.cameraYaw;
            Vector3 Lead()
            {
                var t = targets.Find(e => e != null && e.IsAlive);
                return t != null ? t.transform.position : camFocus;
            }
            void Order(Drone drone, Unit target)
            {
                rts.selected.Clear();
                rts.selected.Add(drone);
                drone.Attack(target);
                Effects.Marker(target.transform.position, Art.EnemyRed);
            }

            // The first wave, as a player pressing 1-4 and right-clicking: a Scout over the column, Strikes on the
            // AA gun, Kamikazes and the Nightjar on the vehicles.
            var firstWave = new (float at, DroneType type, int target)[]
            {
                (2f, DroneType.Scout, -1), (2.6f, DroneType.Strike, -2), (3.2f, DroneType.Strike, -2),
                (4.5f, DroneType.Kamikaze, 0), (5.1f, DroneType.Kamikaze, 1), (8f, DroneType.Bomber, 2),
            };
            int launched = 0;
            float clock = 0f, nextCheck = 14f, linger = 0f;
            Drone subject = null;
            bool following = false;
            while (game.Result == MissionResult.None && clock < 150f)
            {
                while (launched < firstWave.Length && clock >= firstWave[launched].at)
                {
                    var step = firstWave[launched++];
                    var drone = game.SpawnDrone(step.type);
                    if (drone == null) continue;
                    if (step.target == -1)
                    {
                        rts.selected.Clear();
                        rts.selected.Add(drone);
                        drone.MoveTo(Lead());
                        Effects.Marker(Lead(), new Color(0.35f, 1f, 0.6f));
                    }
                    else
                    {
                        Unit target = step.target == -2 && aa != null && aa.IsAlive ? aa : AliveAt(targets, step.target);
                        if (target != null) Order(drone, target);
                    }
                }
                // Then every few seconds: idle drones join in, and a fresh drone goes for any vehicle nobody is after.
                if (clock >= nextCheck)
                {
                    nextCheck += 3f;
                    foreach (var d in game.drones)
                        if (d != null && d.IsAlive && d.AttackTarget == null && d.type != DroneType.Scout)
                        {
                            var t = AliveAt(targets, 0);
                            if (t != null) d.Attack(t);
                        }
                    var free = targets.Find(e => e != null && e.IsAlive && !game.drones.Exists(d => d != null && d.AttackTarget == e));
                    var type = game.StockLeft(DroneType.Kamikaze) > 0 ? DroneType.Kamikaze : DroneType.Strike;
                    if (free != null && game.StockLeft(type) > 0)
                    {
                        var fresh = game.SpawnDrone(type);
                        if (fresh != null) Order(fresh, free);
                    }
                }

                // The camera rides with the drone closest to its target, frames drone and target together as it
                // closes in, and stays on the blast a moment when it hits.
                if (linger > 0f) linger -= 1f / DemoFps;
                else if (following && !Busy(subject))
                {
                    following = false;
                    linger = 2.5f;
                }
                else
                {
                    subject = Subject(game, subject);
                    following = subject != null;
                    if (following)
                    {
                        Vector3 a = subject.transform.position, b = subject.AttackTarget.transform.position;
                        float gap = Vector3.Distance(a, b);
                        if (gap > 90f) Aim(a + (b - a).normalized * 25f, 50f);
                        else Aim(Vector3.Lerp(b, a, 0.35f), Mathf.Clamp(gap * 0.6f + 28f, 30f, 70f));
                    }
                    else Aim(Lead(), 55f);
                }
                Glide(rts, 2f);
                clock += 1f / DemoFps;
                yield return null;
            }

            // The result panel.
            for (int f = 0; f < 6 * DemoFps; f++)
            {
                Glide(rts, 2f);
                yield return null;
            }
            recording = false;
            Time.captureFramerate = 0;
        }

        Vector3 shotFocus;
        float shotDistance = 55f;

        void Aim(Vector3 focus, float distance)
        {
            shotFocus = focus;
            shotDistance = distance;
        }

        // Eases the camera toward the shot Aim chose, turning slowly round it. A shot far across the map is a cut
        // instead, rather than a long smeared whip pan.
        void Glide(RtsController rts, float turnPerSecond)
        {
            if (shotFocus == Vector3.zero) shotFocus = camFocus;
            float k = 1f - Mathf.Exp(-1.8f / DemoFps);
            camFocus = Vector3.Distance(camFocus, shotFocus) > 120f ? shotFocus : Vector3.Lerp(camFocus, shotFocus, k);
            camDistance = Mathf.Lerp(camDistance, shotDistance, k);
            camYaw += turnPerSecond / DemoFps;
            rts.LookAt(camFocus, camDistance, camYaw);
        }

        static bool Busy(Drone d) => d != null && d.IsAlive && d.AttackTarget != null;

        // The drone closest to its target, keeping the one already followed unless another is a good deal closer.
        static Drone Subject(SandboxGame game, Drone current)
        {
            float Gap(Drone d) => Vector3.Distance(d.transform.position, d.AttackTarget.transform.position);
            Drone best = null;
            float bestGap = float.MaxValue;
            foreach (var d in game.drones)
                if (Busy(d) && Gap(d) < bestGap)
                {
                    best = d;
                    bestGap = Gap(d);
                }
            return Busy(current) && Gap(current) < bestGap + 40f ? current : best;
        }

        // The n-th of the targets still alive (counting round).
        static EnemyUnit AliveAt(List<EnemyUnit> targets, int n)
        {
            var alive = targets.FindAll(e => e != null && e.IsAlive);
            return alive.Count == 0 ? null : alive[n % alive.Count];
        }

        // Waits this many seconds of video, counted in frames, so it also runs while a panel has the game paused.
        static IEnumerator Hold(float seconds)
        {
            for (int i = Mathf.RoundToInt(seconds * DemoFps); i > 0; i--) yield return null;
        }

        IEnumerator Record()
        {
            string dir = Path.Combine(outDir, "frames");
            Directory.CreateDirectory(dir);
            while (recording)
            {
                yield return new WaitForEndOfFrame();
                var texture = ScreenCapture.CaptureScreenshotAsTexture();
                File.WriteAllBytes(Path.Combine(dir, $"f{recorded:00000}.jpg"), texture.EncodeToJPG(90));
                Destroy(texture);
                recorded++;
            }
        }

        static EnemyUnit NearestVisible(Vector3 from)
        {
            EnemyUnit best = null;
            float bestDistance = float.MaxValue;
            foreach (var e in SandboxGame.Instance.enemies)
            {
                if (e == null || e.Hidden || !e.IsAlive) continue;
                float d = Vector3.Distance(e.transform.position, from);
                if (d < bestDistance)
                {
                    best = e;
                    bestDistance = d;
                }
            }
            return best;
        }

        EnemyUnit Nearest(EnemyType type, Vector3 from)
        {
            EnemyUnit best = null;
            float bestDistance = float.MaxValue;
            foreach (var e in SandboxGame.Instance.enemies)
            {
                if (e.type != type) continue;
                float d = Vector3.Distance(e.transform.position, from);
                if (d < bestDistance)
                {
                    best = e;
                    bestDistance = d;
                }
            }
            return best;
        }

        // Saves exactly the frame being shown now. (CaptureScreenshot writes a later frame, which came out showing
        // the next camera position.)
        IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            var texture = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(Path.Combine(outDir, name + ".png"), texture.EncodeToPNG());
            Destroy(texture);
            // Memory as we go, to catch a leak: texture memory, and how many textures, render textures and materials exist.
            log.WriteLine($"[{Time.time:F1}] screenshot {name}   tex {Texture.currentTextureMemory / 1048576} MB, " +
                          $"{Resources.FindObjectsOfTypeAll<Texture2D>().Length} tex2d, {Resources.FindObjectsOfTypeAll<RenderTexture>().Length} rt, " +
                          $"{Resources.FindObjectsOfTypeAll<Material>().Length} mat");
        }
    }
}
