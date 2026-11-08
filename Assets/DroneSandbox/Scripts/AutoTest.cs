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
        static string only; // -autotest-only seek (the hunts and Hide and Seek) or tour (the sector tour, for its memory growth)

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Launch()
        {
            string[] args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-autotest"), d = Array.IndexOf(args, "-demo");
            int o = Array.IndexOf(args, "-autotest-only");
            if (o >= 0 && o + 1 < args.Length) only = args[o + 1];
            if (d >= 0 && d + 1 < args.Length) new GameObject("Auto Test").AddComponent<AutoTest>().Begin(args[d + 1], true);
            else if (i >= 0 && i + 1 < args.Length) new GameObject("Auto Test").AddComponent<AutoTest>().Begin(args[i + 1], false);
        }

        void Begin(string folder, bool recordDemo)
        {
            SandboxGame.SaveProgress = false; // test runs don't touch the player's stars, points or purchases
            Progress.UseTestStore();
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
            Settings.UseDefaultsForTest();
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
            var hud = game.GetComponent<SandboxHud>();
            hud.SettingsOpen = true;
            Settings.UiScale = 1.4f;
            yield return new WaitForSecondsRealtime(0.3f);
            yield return Shot("00a_settings_140");
            Settings.UiScale = 0.8f;
            yield return new WaitForSecondsRealtime(0.3f);
            yield return Shot("00b_settings_80");
            Settings.UiScale = 1f;
            hud.SettingsOpen = false;
            Sound.ExportReel(Path.Combine(outDir, "sound_reel.wav"));
            log.WriteLine("Sound reel written");
            if (MapSnapshot.Picture != null)
            {
                // The battle map the debrief draws over, saved to check it.
                File.WriteAllBytes(Path.Combine(outDir, "map_snapshot.png"), MapSnapshot.Picture.EncodeToPNG());
            }
            game.StartSandbox();
            Recon.ForceShowAll = true; // the model close-ups first, everything drawn
            yield return new WaitForSeconds(0.5f);
            if (only == "tour")
            {
                yield return SectorTour(game, rts);
                log.WriteLine($"Done at {Time.realtimeSinceStartup:F1}s real time. Errors: {errors}");
                yield return new WaitForSecondsRealtime(0.5f);
                Application.Quit();
                yield break;
            }
            if (only == "seek")
            {
                yield return HuntMission(game, rts, false);
                yield return HuntMission(game, rts, true);
                yield return SeekIgnored(game, rts);
                log.WriteLine($"Done at {Time.realtimeSinceStartup:F1}s real time. Errors: {errors}");
                yield return new WaitForSecondsRealtime(0.5f);
                Application.Quit();
                yield break;
            }
            log.WriteLine($"Varrow build ms: {World.Timings}");
            log.WriteLine($"forest build ms: total {SectorForest.BuildMilliseconds:F1}, stems {SectorForest.StemsMilliseconds:F1}, indices {SectorForest.IndicesMilliseconds:F1}, canopy preparation {SectorForest.CanopyMilliseconds:F1}; stems {SectorForest.Stems.Count}, cells {SectorForest.LocalCells.Count}");
            log.WriteLine($"World build stages: {FarTerrain.BuildTimings}");
            log.WriteLine($"Field layout stages: {MapLayout.Timings}; {MapLayout.FieldTimings}");
            log.WriteLine($"Started. Drones: {game.drones.Count}, enemies: {game.enemies.Count}, fields: {MapLayout.Fields.Count} ({MapLayout.FieldCoverage():P0} of the land), " +
                          $"screen {Screen.width}x{Screen.height}");

            yield return Shot("01_start");

            // Close-up of the drones at the base, to check the models.
            rts.LookAt(game.basePosition, 18f * Scale.Lens, 30f);
            yield return new WaitForSeconds(2.5f);
            yield return Shot("02_drones_closeup");
            rts.LookAt(game.basePosition, 42f * Scale.Camera, 20f);
            yield return new WaitForSeconds(0.5f);
            yield return Shot("02a_base");
            yield return LightingCheck(game, rts);

            // Close-up of the enemy HQ, to check the vehicle models, terrain and sky.
            rts.LookAt(game.enemyHqPosition, 38f * Scale.Camera, 210f);
            yield return new WaitForSeconds(1f);
            yield return Shot("02b_enemy_closeup");

            // The river crossing, a village and the farmland.
            var bridge = MapLayout.BridgePoint;
            rts.LookAt(new Vector3(bridge.x, 0f, bridge.y), 40f * Scale.Camera, 135f);
            yield return new WaitForSeconds(0.5f);
            yield return Shot("02c_bridge");
            LookAtOpenRiver(rts, Old(-100f, 0f), 26f * Scale.Lens, 150f);
            yield return new WaitForSeconds(2f); // the camera settled, for the pair below
            yield return Shot("02f_river");
            // The same view a second later, to check that the water flows down the river (compare the two).
            yield return new WaitForSeconds(1f);
            yield return Shot("02f_river_later");
            var village = MapLayout.Villages[0];
            rts.LookAt(new Vector3(village.x, 0f, village.y), 45f * Scale.Camera, 200f);
            yield return new WaitForSeconds(0.5f);
            yield return Shot("02d_village");
            rts.LookAt(Old(-20f, -30f), 100f * Scale.Camera, 40f);
            yield return new WaitForSeconds(0.5f);
            yield return Shot("02e_farmland");

            // The fictional sides: a flag at a base gate, and the emblem on an enemy tank.
            var flags = UnityEngine.Object.FindObjectsByType<Flag>();
            if (flags.Length > 0)
            {
                rts.LookAt(flags[0].transform.position, 22f * Scale.Lens, 30f);
                yield return new WaitForSeconds(0.5f);
                yield return Shot("02g_flag");
            }
            EnemyUnit emblemTank = Nearest(EnemyType.Tank, game.enemyHqPosition);
            if (emblemTank != null)
            {
                rts.LookAt(emblemTank.transform.position, 12f * Scale.Lens, emblemTank.transform.eulerAngles.y + 90f);
                yield return new WaitForSeconds(0.3f);
                yield return Shot("02h_tank_emblem");
            }
            rts.LookAt(game.basePosition, 30f * Scale.Lens, 300f);
            yield return new WaitForSeconds(0.3f);
            yield return Shot("02i_base_emblem");

            // The Nightjar heavy bomber lifting off (free play has all the tech).
            var liftOff = game.SpawnDrone(DroneType.Bomber);
            if (liftOff != null)
            {
                rts.LookAt(liftOff.transform.position, 14f * Scale.Lens, 30f);
                yield return new WaitForSeconds(0.3f);
                yield return Shot("02j_nightjar");
            }

            // Send everyone toward the forward AA post and follow them, with the fog of war on.
            Recon.ForceShowAll = false;
            Vector3 staging = Old(-32f, -26f);
            rts.selected.Clear();
            rts.selected.AddRange(game.drones);
            foreach (var d in game.drones) d.MoveTo(staging);
            for (float t = 0f; t < 6f; t += 0.5f)
            {
                rts.LookAt(Vector3.Lerp(game.basePosition, staging, t / 6f), 45f * Scale.Camera, 45f);
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
            rts.LookAt(aa != null ? aa.transform.position : staging, 40f * Scale.Camera, 45f);
            yield return new WaitForSeconds(3f);
            yield return Shot("04_engaging");
            yield return new WaitForSeconds(4f);
            yield return Shot("05_battle");
            if (tank != null) rts.LookAt(tank.transform.position, 35f * Scale.Camera, 45f);
            yield return new WaitForSeconds(4f);
            yield return Shot("06_battle_later");

            // Orders: an attack-move with two waypoints queued after it, then the tactical pause.
            rts.selected.Clear();
            rts.selected.AddRange(game.drones);
            Vector3 sweep = staging + new Vector3(-20f, 0f, 18f) * Scale.Distance;
            foreach (var d in game.drones)
            {
                d.MoveTo(sweep, false, true);
                d.MoveTo(sweep + new Vector3(15f, 0f, 15f) * Scale.Distance, true);
                d.MoveTo(game.basePosition + new Vector3(8f, 0f, 8f), true);
            }
            rts.LookAt(sweep, 60f * Scale.Camera, 45f);
            yield return new WaitForSeconds(1.5f);
            yield return Shot("06a_orders_route");
            game.TogglePause();
            yield return new WaitForSecondsRealtime(0.4f);
            yield return Shot("06b_tactical_pause");
            log.WriteLine($"Orders: paused {game.Paused}, timeScale {Time.timeScale}, first drone '{(game.drones.Count > 0 ? game.drones[0].Status : "-")}'");
            game.TogglePause();

            // Fast-forward: ] twice is 4x, and a threat drops it straight back to 1x.
            string busy = game.Threat();
            game.ChangeSpeed(1);
            game.ChangeSpeed(1);
            float fast = Time.timeScale;
            yield return new WaitForSecondsRealtime(0.3f);
            yield return Shot("06c_fast_forward");
            game.SlowDown("test threat");
            log.WriteLine($"Fast-forward: {fast}x, then {Time.timeScale}x after a threat{(busy != null ? $" (refused: {busy})" : "")}");

            // A coordinated strike: three drones at three different enemies, timed to hit together.
            var strikers = game.drones.FindAll(d => d != null && !d.IsBomber);
            var marks = new List<EnemyUnit>(game.enemies.FindAll(e => e != null && e.IsAlive && !e.Hidden));
            marks.Sort((a, b) => Vector3.Distance(a.transform.position, sweep).CompareTo(Vector3.Distance(b.transform.position, sweep)));
            rts.selected.Clear();
            for (int i = 0; i < 3 && i < strikers.Count && i < marks.Count; i++)
            {
                strikers[i].Attack(marks[i]);
                rts.selected.Add(strikers[i]);
            }
            game.CoordinateStrike(rts.selected);
            rts.LookAt(sweep, 70f * Scale.Camera, 45f);
            yield return new WaitForSeconds(0.8f);
            yield return Shot("06c_sync_strike");
            string tags = "";
            foreach (var d in rts.selected) if (d != null) tags += d.StatusTag + "; ";
            log.WriteLine($"Sync strike: {rts.selected.Count} drones, tags {tags} (\"{game.Message}\")");

            // Wide view of the whole map.
            rts.LookAt(Old(10f, 10f), 110f * Scale.Camera, 45f);
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
            rts.LookAt(game.basePosition + new Vector3(25f, 0f, 25f) * Scale.Distance, 75f * Scale.Camera, 45f);
            yield return new WaitForSeconds(1f);
            yield return Shot("08a_dusk");
            dayNight.SetTime(23.5f, 0f);
            yield return new WaitForSeconds(1f);
            yield return Shot("08b_night");
            rts.LookAt(game.enemyHqPosition, 45f * Scale.Camera, 210f);
            yield return new WaitForSeconds(0.5f);
            yield return Shot("08c_night_hq");
            if (game.SpawnDrone(DroneType.Scout) != null) game.ToggleThermal();
            rts.LookAt(Old(10f, 10f), 90f * Scale.Camera, 45f);
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
            yield return CampaignToVictory(game);
            yield return QualityCheck(game, rts);
            yield return WorldCheck(game, rts);
            yield return WildlifeShots(game, rts);
            yield return HuntMission(game, rts, false);
            yield return HuntMission(game, rts, true);
            yield return SeekIgnored(game, rts);
            yield return IdentificationHuntCheck(game, rts);
            yield return OcclusionCheck(game);
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
                ("21_photo_lake_evening", 20.0f, At(dam + (down * 165f - across * 30f) * Scale.Distance, lake + 90f * Scale.Distance), At(dam + (-down * 55f + across * 30f) * Scale.Distance, lake), 47f),
                ("22_photo_bridge", 8.5f, At(MapLayout.BridgePoint + new Vector2(20f, 24f), 5f), At(MapLayout.BridgePoint, 1f), 45f),
                ("23_photo_valley", 19.3f, Old(75f, -150f) + Vector3.up * 70f * Scale.Distance, Old(-35f, 0f), 60f),
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
            rts.LookAt(spot, 24f * Scale.Lens, 30f);
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
            rts.LookAt(spot, 45f * Scale.Camera, 20f);
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
            rts.LookAt(spot, 45f * Scale.Camera, 20f);
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

            var types = new[] { DroneType.Scout, DroneType.Strike, DroneType.Kamikaze, DroneType.Bomber, DroneType.Recon };
            var flying = new List<Drone>();
            foreach (var type in types)
            {
                var drone = game.SpawnDrone(type);
                if (drone == null) continue;
                drone.MoveTo(spot + new Vector3((flying.Count - 1.5f) * 6f, 0f, 0f));
                flying.Add(drone);
            }
            yield return new WaitForSeconds(6f);
            rts.LookAt(spot, 25f * Scale.Lens, 20f);
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
        // An (x, z) spot on the old 480 m Varrow map, on today's (see SectorPlan.FromOld).
        static Vector3 Old(float x, float z) => MapLayout.Plan.FromOld(new Vector3(x, 0f, z));

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

        // Points the camera at the nearest stretch of river (going out in rings) that it sees with no crown in the way,
        // tested on lines to the middle and four points round it: hd160-162's river shot looked straight into a
        // bankside tree (one the old near-tree list didn't hold).
        static void LookAtOpenRiver(RtsController rts, Vector3 from, float distance, float yaw)
        {
            var offsets = new[] { Vector3.zero, new Vector3(8f, 0f, 0f), new Vector3(-8f, 0f, 0f), new Vector3(0f, 0f, 8f), new Vector3(0f, 0f, -8f) };
            for (float reach = 0f; reach < 400f; reach += 10f)
                for (float angle = 0f; angle < 360f; angle += reach > 0f ? 15f : 360f)
                {
                    var p = new Vector2(from.x, from.z) + new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad)) * reach;
                    if (MapLayout.RiverField.Distance(p.x, p.y) >= MapLayout.RiverHalfWidth * 0.5f) continue;
                    var target = Ground.OnGround(new Vector3(p.x, 0f, p.y));
                    rts.LookAt(target, distance, yaw);
                    Vector3 eye = Camera.main.transform.position;
                    bool clear = true;
                    foreach (var offset in offsets) clear &= !Canopy.Blocks(eye, Ground.OnGround(target + offset) + Vector3.up * 0.5f);
                    if (clear) return;
                }
            rts.LookAt(from, distance, yaw);
        }

        // A tour of generated sectors: the theatre map, then a dozen kinds of sector, each built, photographed from
        // above and from a drone's height, with one of its missions briefed and started through the theatre path,
        // so a broken generator shows up as errors, odd numbers in the log or odd pictures.
        // The campaign end to end (in the test store): a new war; sectors won until the final operation opens (the
        // fights themselves are played elsewhere in this run, so here they're credited) and one lost; the final
        // operation flown, its targets destroyed and the war won; the victory on the results and the theatre map;
        // then a new campaign.
        IEnumerator CampaignToVictory(SandboxGame game)
        {
            var hud = game.GetComponent<SandboxHud>();
            Theatre.NewCampaign();
            int credited = 0, lostOne = 0, answered = 0, ignored = 0, alertedSeen = 0, chargesAtStart = Theatre.Charges;
            Hotspot ignoring = null;
            var turnsLog = new List<string>();
            bool shotThreat = false;
            for (int guard = 0; guard < 60 && !Theatre.FinalOpen; guard++)
            {
                var offers = Theatre.Offers;
                if (offers.Count < 3 || offers.Exists(o => !Theatre.Offered(o.spot))) { log.WriteLine($"Campaign: turn {Theatre.Turn} has {offers.Count} offers"); errors++; }
                var rewards = new HashSet<Theatre.Reward>(offers.ConvertAll(o => o.reward));
                float coolest = 1f;
                foreach (var h in Theatre.Hotspots) if (!h.final && !Theatre.Done(h)) coolest = Mathf.Min(coolest, h.heat);
                if (rewards.Count != offers.Count || !offers.Exists(o => o.spot.heat < 0.6f || o.spot.heat <= coolest)) { log.WriteLine($"Campaign: turn {Theatre.Turn} offers break the rules"); errors++; }
                var threat = Theatre.ThreatSpot;
                if (threat != null && !offers.Exists(o => o.spot == threat)) { log.WriteLine("Campaign: the threat isn't on offer"); errors++; }
                if (Theatre.AlertedSpot != null) alertedSeen++;
                if (threat != null && !shotThreat)
                {
                    shotThreat = true;
                    game.OpenMenu();
                    hud.TheatreOpen = true;
                    yield return new WaitForSecondsRealtime(0.6f);
                    yield return Shot("33a_campaign_threat");
                    hud.TheatreOpen = false;
                }
                int before = Theatre.Standing;
                // The first threat is dealt with; the second is left alone (another offer taken, both its turns) until it
                // lapses; the rest are dealt with. Otherwise the easiest offer.
                Hotspot pick = offers[0].spot;
                foreach (var o in offers) if (o.spot.heat < pick.heat) pick = o.spot;
                if (threat != null && answered > 0 && ignored == 0 && (ignoring == null || ignoring == threat))
                {
                    ignoring = threat;
                    pick = offers.Find(o => o.spot != threat).spot;
                    if (Theatre.ThreatDue <= Theatre.Turn) ignored++;
                }
                else if (threat != null)
                {
                    pick = threat;
                    answered++;
                }
                if (credited == 4 && lostOne == 0)
                {
                    Theatre.Lost(pick, 2);
                    lostOne++;
                }
                else
                {
                    Theatre.Won(pick, 0);
                    credited++;
                }
                turnsLog.Add($"{Theatre.Standing - before:+0;-0}");
            }
            var final = Theatre.Hotspots.Find(h => h.final);
            log.WriteLine($"Campaign: {Theatre.Turn - 1} turns, {credited} won, {lostOne} lost, threats answered {answered}, ignored {ignored}, turns with an alerted sector {alertedSeen}, " +
                          $"progress {Theatre.Standing}/{Theatre.ProgressForFinal}, charges {chargesAtStart} at the start, {Theatre.Charges} now, intel +{Theatre.Stacks(Theatre.Bonus.Intel)}, " +
                          $"rearm +{Theatre.Stacks(Theatre.Bonus.Rearm)}, front advance {Theatre.Advance:F3}, final open {Theatre.FinalOpen}, " +
                          $"final {(final != null ? final.sectorName + " / " + final.situation : "missing")}; per turn {string.Join(" ", turnsLog)}");
            if (!Theatre.FinalOpen || chargesAtStart != 1 || ignored == 0 || answered == 0) errors++;
            game.OpenMenu();
            hud.TheatreOpen = true;
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Shot("33_campaign_final_open");
            hud.TheatreOpen = false;
            if (final == null) yield break;

            game.PlayHotspot(final);
            while (game.Loading) yield return null;
            // A support charge spent in the briefing: one more drone of a type the mission has.
            int strikes = game.StockLeft(DroneType.Strike), charges = Theatre.Charges;
            bool spent = game.SpendSupport(DroneType.Strike);
            log.WriteLine($"Campaign briefing: spent a charge {spent}, Strikes {strikes} -> {game.StockLeft(DroneType.Strike)}, charges {charges} -> {Theatre.Charges}");
            if (spent != (charges > 0 && strikes > 0) || (spent && game.StockLeft(DroneType.Strike) != strikes + 1)) errors++;
            yield return new WaitForSecondsRealtime(0.4f);
            yield return Shot("33b_campaign_briefing");
            game.CloseBriefing();
            yield return new WaitForSeconds(1f);
            foreach (var e in game.enemies.FindAll(e => e != null && e.isTarget)) e.TakeDamage(100000f);
            float until = Time.realtimeSinceStartup + 30f;
            while (game.Result == MissionResult.None && Time.realtimeSinceStartup < until) yield return null;
            while (Time.unscaledTime < game.ResultsAt + 0.5f && Time.realtimeSinceStartup < until + 10f) yield return null;
            log.WriteLine($"Campaign final: {game.Result} ({game.ResultReason}), victory {Theatre.Victory}, " +
                          $"fights {Theatre.Fights}, wins {Theatre.Wins}, losses {Theatre.Losses}, drones lost {Theatre.DronesLost}");
            yield return Shot("34_campaign_victory");

            game.OpenMenu();
            yield return new WaitForSecondsRealtime(0.4f);
            yield return Shot("35_menu_after_victory");
            hud.TheatreOpen = true;
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Shot("36_theatre_victory");
            hud.TheatreOpen = false;

            Theatre.NewCampaign();
            log.WriteLine($"Campaign after New campaign: wins {Theatre.Wins}, victory {Theatre.Victory}, final open {Theatre.FinalOpen}, " +
                          $"hotspots {Theatre.Hotspots.Count}");
        }

        // The graphics settings: frames per second over the wide view of the sandbox at High and at Low, and a picture
        // at Low.
        IEnumerator QualityCheck(SandboxGame game, RtsController rts)
        {
            game.StartSandbox();
            rts.LookAt(Old(10f, 10f), 110f * Scale.Camera, 45f);
            yield return new WaitForSecondsRealtime(2f);
            var fps = new float[3];
            foreach (int q in new[] { 2, 1, 0 })
            {
                Settings.Quality = q;
                yield return new WaitForSecondsRealtime(1f);
                int frames = Time.frameCount;
                float start = Time.realtimeSinceStartup;
                yield return new WaitForSecondsRealtime(3f);
                fps[q] = (Time.frameCount - frames) / (Time.realtimeSinceStartup - start);
                if (q == 0) yield return Shot("37_quality_low");
            }
            log.WriteLine($"Graphics quality: High {fps[2]:F0} fps, Medium {fps[1]:F0} fps, Low {fps[0]:F0} fps over the sandbox's wide view");
            Settings.Quality = 2;
        }

        // The big world round the sector: the wide view from 9 km back (and its fps) and the horizon from 2.5 km, then
        // a Scout flown 5 km out over the far terrain, away from the enemy, at 4x. Its height above the ground there
        // has to stay positive all the way.
        IEnumerator WorldCheck(SandboxGame game, RtsController rts)
        {
            game.StartSandbox();
            log.WriteLine($"World: {WorldPlan.Size / 1000f:F0} km square, seed {WorldPlan.Seed}, ready {WorldPlan.Ready}, far terrain {FarTerrain.Active}, " +
                          $"sector at ({FarTerrain.Origin.x:F0}, {FarTerrain.Origin.y:F0}), {WorldPlan.Rivers.Count} rivers, {WorldPlan.Roads.Count} roads, " +
                          $"{WorldPlan.Settlements.Count} settlements, {WorldPlan.Sites.Count} sites; build ms: {World.Timings}");
            if (!FarTerrain.Active)
            {
                errors++;
                yield break;
            }
            rts.LookAt(Vector3.zero, 9000f, 35f);
            yield return new WaitForSecondsRealtime(2.5f);
            int frames = Time.frameCount;
            float start = Time.realtimeSinceStartup;
            yield return new WaitForSecondsRealtime(2f);
            float wideFps = (Time.frameCount - frames) / (Time.realtimeSinceStartup - start);
            yield return Shot("80_world_wide");
            log.WriteLine($"World scatter: mesh trees {FarScatter.MeshTreesDrawn}, cards {FarScatter.CardTreesDrawn}, houses {FarScatter.HousesDrawn}; " +
                          $"forest blocks {ForestHlod.BlocksDrawn}, triangles {ForestHlod.TrianglesDrawn}, town blocks {SettlementHlod.BlocksDrawn}, " +
                          $"town triangles {SettlementHlod.TrianglesDrawn}, draws {FarScatter.InstanceDrawCalls + ForestHlod.BlocksDrawn + SettlementHlod.DrawCalls}, " +
                          $"forest build {ForestHlod.BuildMs:F2} ms");
            rts.LookAt(new Vector3(1200f, 0f, 400f), 2500f, 60f);
            yield return new WaitForSecondsRealtime(1.5f);
            yield return ForestMassesSettle("81_world_horizon");
            yield return Shot("81_world_horizon");
            log.WriteLine($"World scatter: mesh trees {FarScatter.MeshTreesDrawn}, cards {FarScatter.CardTreesDrawn}, houses {FarScatter.HousesDrawn}; " +
                          $"forest blocks {ForestHlod.BlocksDrawn}, triangles {ForestHlod.TrianglesDrawn}, town blocks {SettlementHlod.BlocksDrawn}, " +
                          $"town triangles {SettlementHlod.TrianglesDrawn}, draws {FarScatter.InstanceDrawCalls + ForestHlod.BlocksDrawn + SettlementHlod.DrawCalls}, " +
                          $"forest build {ForestHlod.BuildMs:F2} ms");

            var scout = game.SpawnDrone(DroneType.Scout);
            Vector3 away = game.basePosition - game.enemyHqPosition;
            away.y = 0f;
            Vector3 target = game.basePosition + away.normalized * 6000f;
            if (scout != null) scout.MoveTo(target);
            Time.timeScale = 4f;
            float lowest = float.MaxValue, flown = 0f, lowestAt = 0f;
            for (float t = 0f; t < 300f && scout != null && scout.IsAlive; t += 0.5f)
            {
                flown = Vector3.Distance(Flat(scout.transform.position), Flat(game.basePosition));
                if (flown > 5000f) break;
                // From 600 m out: the climb from the pad is behind it by then.
                float clearance = scout.transform.position.y - Ground.HeightAt(scout.transform.position);
                if (flown > 600f && clearance < lowest)
                {
                    lowest = clearance;
                    lowestAt = flown;
                }
                if (t % 10f < 0.5f) rts.LookAt(scout.transform.position, 900f, 30f);
                yield return new WaitForSeconds(0.5f);
            }
            Time.timeScale = 1f;
            if (scout != null) rts.LookAt(scout.transform.position, 700f, 30f);
            yield return new WaitForSecondsRealtime(1.5f);
            yield return Shot("82_world_drone_out");
            log.WriteLine($"World flight: wide view {wideFps:F0} fps; Scout alive {scout != null && scout.IsAlive}, flew {flown:F0} m from base, " +
                          $"lowest {lowest:F1} m above the ground ({lowestAt:F0} m out), ground there {(scout != null ? Ground.HeightAt(scout.transform.position) : 0f):F0} m");
            if (scout == null || !scout.IsAlive || flown < 5000f || lowest < 2f) errors++;

            // The Swift's long flight: 15 km out over the world at 4x (ChatGPT's acceptance test is 20 km), which
            // it must finish with battery to spare and without touching the ground.
            var swift = game.SpawnDrone(DroneType.Recon);
            if (swift != null) swift.MoveTo(game.basePosition + away.normalized * 16000f);
            var flightFrames = new List<float>(12000);
            FarDetail.ResetMetrics();
            FarScatter.ResetTimings();
            bool lowField = false, lowForest = false;
            rts.selected.Clear();
            if (swift != null) rts.selected.Add(swift); // the feed has its own streaming priority and predicted velocity
            Time.timeScale = 4f;
            float swiftLowest = float.MaxValue, swiftFlown = 0f, started = Time.time;
            while (Time.time - started < 600f && swift != null && swift.IsAlive)
            {
                flightFrames.Add(Time.unscaledDeltaTime * 1000f);
                swiftFlown = Vector3.Distance(Flat(swift.transform.position), Flat(game.basePosition));
                if (swiftFlown > 15000f) break;
                if (swiftFlown > 600f) swiftLowest = Mathf.Min(swiftLowest, swift.transform.position.y - Ground.HeightAt(swift.transform.position));
                rts.LookAt(swift.transform.position, 75f, 30f);
                if (!lowField && swiftFlown > 5500f)
                {
                    yield return LowWorldShot(Cover.Farmland, "86_world_low_detail", swift.transform.position);
                    lowField = true;
                }
                if (!lowForest && swiftFlown > 10000f)
                {
                    yield return LowWorldShot(Cover.Forest, "87_world_low_forest_edge", swift.transform.position);
                    lowForest = true;
                }
                yield return null;
            }
            Time.timeScale = 1f;
            if (swift != null) rts.LookAt(swift.transform.position, 900f, 30f);
            yield return new WaitForSecondsRealtime(1.5f);
            yield return Shot("83_world_swift_far");
            log.WriteLine($"World long flight: Swift alive {swift != null && swift.IsAlive}, flew {swiftFlown:F0} m in {Time.time - started:F0} s, " +
                          $"battery {(swift != null ? swift.Battery * 100f : 0f):F0}%, lowest {swiftLowest:F1} m above the ground");
            flightFrames.Sort();
            float p95 = flightFrames.Count > 0 ? flightFrames[Mathf.Min(flightFrames.Count - 1, Mathf.CeilToInt(flightFrames.Count * 0.95f) - 1)] : 0f;
            double meanCommit = FarDetail.TotalCommitMs / Mathf.Max(1, FarDetail.CommitFrames);
            log.WriteLine($"World detail stage maxima: {FarDetailTiming.Breakdown}; collider bake worker worst {FarDetail.WorstBakeMs:F3} ms");
            log.WriteLine($"World picture stages (pending {FarTerrain.PicturePending}): {FarTerrain.PictureTimings}");
            log.WriteLine($"World detail flight: live {FarDetail.Live}/{FarDetail.MaxTiles}, committed {FarDetail.Committed}, " +
                          $"generation last {FarDetail.LastGenerationMs:F2} ms, main-thread mean {meanCommit:F3} ms, worst {FarDetail.WorstCommitMs:F3} ms, " +
                          $"over 2 ms {FarDetail.BudgetOverruns}, pressure evictions {FarDetail.PressureEvictions}; frame p95 {p95:F2} ms ({flightFrames.Count} frames, screenshot I/O excluded), " +
                          $"{Screen.width}x{Screen.height}, {SystemInfo.graphicsDeviceName}");
            log.WriteLine($"World forest over the flight: {ForestLine()}");
            log.WriteLine($"forest flight screen: effective px {FarScatter.TierPixels.x:F1}/{FarScatter.TierPixels.y:F1}/{FarScatter.TierPixels.z:F1}, near coarse {ForestHlod.NearCoarseBlocks}, CPU mean {FarScatter.MeanMs:F3} ms, worst {FarScatter.WorstMs:F3} ms, samples {FarScatter.Samples}");
            log.WriteLine($"forest flight budget: fast frames {FarScatter.FastFrames}, samples {FarScatter.Samples}, peak per-camera full/reduced/card {FarScatter.PeakFullTrees}/{FarScatter.PeakReducedTrees}/{FarScatter.PeakCards}, peak tree triangles {FarScatter.PeakTreeTriangles}; selection start/complete/cancel {FarScatter.SelectionStarts}/{FarScatter.SelectionCompletes}/{FarScatter.SelectionCancels}");
            log.WriteLine($"forest flight phases: generation {FarScatter.GenerationMeanMs:F3}, selection {FarScatter.SelectionMeanMs:F3}, submission {FarScatter.SubmissionMeanMs:F3}, canopy {FarScatter.CanopyMeanMs:F3} ms mean/frame (all cameras)");
            log.WriteLine($"forest flight canopy: build mean {ForestHlod.BuildMeanMs:F3} ms, worst {ForestHlod.BuildWorstMs:F3} ms, refinements {ForestHlod.RefinementsStarted}, cancelled {ForestHlod.CancelledBuilds}, deferred frames {ForestHlod.DeferredFrames}");
            if (FarScatter.Samples > 0 && FarScatter.MeanMs > 1f) errors++;
            if (!lowField || !lowForest || FarDetail.Live > FarDetail.MaxTiles || FarDetail.Committed == 0 ||
                FarDetail.WorstCommitMs > 2.0 || meanCommit > 1.0 || p95 > 16.7f) errors++;
            if (swift == null || !swift.IsAlive || swiftFlown < 15000f || swiftLowest < 2f) errors++;
            yield return WorldMapCheck(game, rts, swift);
            Vector2 forest = ForestHlod.LargestForest() - FarTerrain.Origin;
            rts.LookAt(new Vector3(forest.x, 0f, forest.y), 2500f, 35f);
            yield return new WaitForSecondsRealtime(2f);
            yield return ForestMassesSettle("88_world_forest_hlod");
            yield return Shot("88_world_forest_hlod");
            log.WriteLine($"World scatter: mesh trees {FarScatter.MeshTreesDrawn}, cards {FarScatter.CardTreesDrawn}, houses {FarScatter.HousesDrawn}; " +
                          $"forest blocks {ForestHlod.BlocksDrawn}, triangles {ForestHlod.TrianglesDrawn}, town blocks {SettlementHlod.BlocksDrawn}, " +
                          $"town triangles {SettlementHlod.TrianglesDrawn}, draws {FarScatter.InstanceDrawCalls + ForestHlod.BlocksDrawn + SettlementHlod.DrawCalls}, " +
                          $"forest build {ForestHlod.BuildMs:F2} ms");
            yield return LandmarkViews(rts);
            // Twice, as ChatGPT's gate asks: the second run is warmed and must be just as clean.
            yield return ContinuousFlight(game, rts, 1);
            yield return ContinuousFlight(game, rts, 2);
            // Two experiment passes on the same route for the stall crossing a river at a tile row (0.7-6.8 s, hd180):
            // without screen-space reflections, then with the regional ring's page work held. Reported only.
            Atmosphere.Profile.TryGet(out UnityEngine.Rendering.HighDefinition.ScreenSpaceReflection reflections);
            bool reflecting = reflections != null && reflections.enabled.value;
            if (reflections != null) reflections.enabled.value = false;
            yield return ContinuousFlight(game, rts, 3);
            if (reflections != null) reflections.enabled.value = reflecting;
            FarDetail.PauseRegional = true;
            yield return ContinuousFlight(game, rts, 4);
            FarDetail.PauseRegional = false;

            // Select the nearest requested cover outside Backdrop, then record a real 40-60 m view with a live tile.
            IEnumerator LowWorldShot(Cover cover, string name, Vector3 near)
            {
                Vector3 chosen = near;
                float best = float.MaxValue;
                for (int z = -48; z <= 48; z++) for (int x = -48; x <= 48; x++) // 3 km round the flight: forest isn't always nearer
                {
                    Vector3 local = near + new Vector3(x * 64f, 0f, z * 64f);
                    Vector2 world = new Vector2(local.x, local.z) + FarTerrain.Origin;
                    if (!FarDetail.Eligible(Mathf.FloorToInt(world.x / 512f), Mathf.FloorToInt(world.y / 512f)) ||
                        WorldPlan.CoverAt(world.x, world.y) != cover) continue;
                    if (cover == Cover.Forest && WorldPlan.CoverAt(world.x + 64f, world.y) == cover &&
                        WorldPlan.CoverAt(world.x - 64f, world.y) == cover && WorldPlan.CoverAt(world.x, world.y + 64f) == cover &&
                        WorldPlan.CoverAt(world.x, world.y - 64f) == cover) continue;
                    float distance = x * x + z * z;
                    if (distance < best) { best = distance; chosen = local; }
                }
                if (best == float.MaxValue) { log.WriteLine($"World detail: no {cover} probe near flight; {name} failed"); errors++; yield break; }
                float until = Time.realtimeSinceStartup + 12f, zoom = 75f;
                bool loaded = false;
                while (Time.realtimeSinceStartup < until)
                {
                    rts.LookAt(chosen, zoom, 30f);
                    var camera = rts.Cam.transform.position;
                    float clearance = camera.y - Ground.HeightAt(camera);
                    zoom = Mathf.Clamp(zoom + (50f - clearance) * 1.2f, 25f, 180f);
                    loaded = FarDetail.TryHeight(new Vector2(camera.x, camera.z), out _) &&
                             FarDetail.TryHeight(new Vector2(chosen.x, chosen.z), out _);
                    flightFrames.Add(Time.unscaledDeltaTime * 1000f);
                    if (loaded && clearance >= 40f && clearance <= 60f) break;
                    yield return null;
                }
                var eye = rts.Cam.transform.position;
                float above = eye.y - Ground.HeightAt(eye);
                log.WriteLine($"{name}: live {loaded}, clearance {above:F1} m, local ({eye.x:F0},{eye.z:F0}), tiles {FarDetail.Live}");
                if (!loaded || above < 40f || above > 60f) errors++;
                yield return Shot(name);
            }
        }

        // Exercise the atlas's actual pointer path, with a far-away Swift and screenshots at both map scales.
        IEnumerator WorldMapCheck(SandboxGame game, RtsController rts, Drone swift)
        {
            var hud = game.GetComponent<SandboxHud>();
            if (hud == null || swift == null || !swift.IsAlive) { errors++; yield break; }
            var selection = rts.selected.ToArray();
            bool paused = game.Paused;
            float timeScale = Time.timeScale;
            try
            {
                rts.selected.Clear();
                rts.selected.Add(swift);
                hud.OpenWorldMap();
                bool pausePreserved = Time.timeScale == timeScale && game.Paused == paused;
                if (!hud.WorldMapOpen || !hud.IsPointerOverHud(Vector2.zero) || !pausePreserved) errors++;
                yield return new WaitForSecondsRealtime(0.5f);
                yield return Shot("84_world_map");
                Vector2 world = new Vector2(swift.transform.position.x, swift.transform.position.z) + FarTerrain.Origin;
                for (int i = 0; i < 12; i++) hud.WorldMapPointer(hud.WorldMapPoint(world), false, false, false, false, 1f);
                if (!game.Paused) game.TogglePause();
                Vector2 target = world + new Vector2(500f, 500f);
                target.x = Mathf.Clamp(target.x, -WorldPlan.Half + 50f, WorldPlan.Half - 50f);
                target.y = Mathf.Clamp(target.y, -WorldPlan.Half + 50f, WorldPlan.Half - 50f);
                hud.WorldMapPointer(hud.WorldMapPoint(target), false, false, false, true, 0f);
                Vector3 expected = new Vector3(target.x - FarTerrain.Origin.x, 0f, target.y - FarTerrain.Origin.y);
                bool ordered = swift.MoveTarget.HasValue && Vector3.Distance(Flat(swift.MoveTarget.Value), expected) < 1f;
                if (!ordered) errors++;
                yield return Shot("85_world_map_zoomed");
                log.WriteLine($"World map: open {hud.WorldMapOpen}, pause preserved on open {pausePreserved}, Swift map order {ordered}; world ({target.x:F0}, {target.y:F0})");
            }
            finally
            {
                hud.CloseWorldMap();
                if (game.Paused != paused) game.TogglePause();
                rts.selected.Clear();
                foreach (var drone in selection) if (drone != null && drone.IsAlive) rts.selected.Add(drone);
            }
            if (hud.WorldMapOpen || Time.timeScale != timeScale) errors++;
        }

        static Vector3 Flat(Vector3 p) => new Vector3(p.x, 0f, p.z);

        // Mimic: crows and spy robots over the sandbox, from close by and from the game camera; then the hunt, played
        // by a bot that knows only what the drone's camera shows: it shadows a crow for 20 s (which must stay merely
        // observed), then shadows the robot zoomed in until the clues confirm it, and shoots it down.
        IEnumerator WildlifeShots(SandboxGame game, RtsController rts)
        {
            game.StartSandbox();
            const float S = Scale.Distance;
            Vector3 home = game.basePosition + new Vector3(30f, 0f, 30f) * S;
            Wildlife.AddFlock(home, 25f * S, 5, 11);
            Wildlife.AddFlock(home + new Vector3(-60f, 0f, 20f) * S, 25f * S, 3, 12);
            var route = new List<Vector3> { game.basePosition + new Vector3(-20f, 0f, 25f) * S, game.basePosition + new Vector3(15f, 0f, 30f) * S,
                                            game.basePosition + new Vector3(25f, 0f, -5f) * S, game.basePosition + new Vector3(-10f, 0f, -20f) * S };
            var robot = Wildlife.AddRobot(route, 14f * S, 21);
            yield return new WaitForSeconds(6f);
            var crow = Wildlife.Birds.Find(b => b != null && !b.Robot);

            // Close-ups from photo mode's free camera, with time stopped so the birds hold still.
            var photo = PhotoMode.Instance;
            if (photo != null)
            {
                photo.Enter();
                Time.timeScale = 0f;
                foreach (var (name, b) in new[] { ("50_crow_close", crow), ("51_robot_close", robot) })
                {
                    Vector3 c = b.transform.position;
                    photo.PlaceLooking(c - b.transform.forward * 2.2f + b.transform.right * 1.4f + Vector3.up * 0.9f, c, 40f);
                    yield return new WaitForSecondsRealtime(0.6f);
                    yield return Shot(name);
                }
                Time.timeScale = 1f;
                photo.Exit();
            }
            rts.LookAt(home, 70f * Scale.Camera, 30f);
            yield return new WaitForSeconds(1f);
            yield return Shot("52_birds_game_view");
            log.WriteLine($"Wildlife: {Wildlife.Birds.Count} birds ({Wildlife.Birds.FindAll(b => b.Robot).Count} robots), robot hovering {robot.Hovering}, " +
                          $"crow at {crow.transform.position.y - Ground.HeightAt(crow.transform.position):F0} m up");

            var scout = game.SpawnDrone(DroneType.Scout);
            rts.selected.Clear();
            rts.selected.Add(scout);
            DroneFeed.Instance.Zoom = 3f;
            Mimic.Act(crow, scout);
            yield return new WaitForSeconds(20f);
            var crowSeen = Mimic.Of(crow);
            log.WriteLine($"Hunt: a crow shadowed 20 s: {crowSeen.Status}, {crowSeen.Points} pts, watched {crowSeen.Watched:F0} s");

            Mimic.Act(robot, scout);
            float started = Time.time;
            bool shotTaken = false;
            while (Time.time - started < 120f && !Mimic.Of(robot).Confirmed) yield return null;
            var seen = Mimic.Of(robot);
            log.WriteLine($"Hunt: robot {seen.Status} after {Time.time - started:F0} s, {seen.Points} pts from {seen.KindCount} kinds: {string.Join("; ", seen.Found)}");
            rts.LookAt(scout.transform.position, 60f * Scale.Camera, 30f);
            yield return new WaitForSeconds(0.5f);
            yield return Shot("53_hunt_confirmed");
            while (!robot.Down && !robot.Escaped && Mimic.ShotsLeftFor(scout) > 0 && Time.time - started < 180f)
            {
                if (seen.Confirmed && Mimic.WouldHit(robot, scout))
                {
                    Mimic.Fire(robot, scout);
                    shotTaken = true;
                }
                yield return new WaitForSeconds(0.2f);
            }
            yield return new WaitForSeconds(1.5f);
            yield return Shot("54_hunt_down");
            log.WriteLine($"Hunt: shot {(shotTaken ? "taken" : "never taken")}, robot down {robot.Down}, escaped {robot.Escaped}, shots left {Mimic.ShotsLeftFor(scout)}/{Mimic.Shots}, " +
                          $"{Time.time - started:F0} s in all");
            Wildlife.Clear();
        }

        // Silent Wings, played: the bot samples the birds in the first Scout's feed one by one (10 s each, zoomed in)
        // until one shows a clue, keeps that one until it's confirmed, shoots it down, and moves on; a second Scout
        // takes over if the first runs dry. Then home. It must win, and no crow may ever be confirmed.
        IEnumerator HuntMission(SandboxGame game, RtsController rts, bool spotting)
        {
            int hunt = Missions.All.FindIndex(m => m.huntRobots > 0 && (m.spotterShelling > 0f) == spotting);
            string name = spotting ? "Hide and Seek" : "Silent Wings", prefix = spotting ? "6" : "5";
            if (hunt < 0)
            {
                log.WriteLine($"{name}: no such mission");
                errors++;
                yield break;
            }
            game.StartMission(hunt);
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Shot(prefix + "5_hunt_briefing");
            game.CloseBriefing();
            var scouts = new List<Drone> { game.SpawnDrone(DroneType.Scout), game.SpawnDrone(DroneType.Scout) };
            int scoutIndex = 0;
            Drone scout = scouts[0];
            rts.selected.Clear();
            rts.selected.Add(scout);
            DroneFeed.Instance.Zoom = 3f;
            float started = Time.time;
            var sampled = new HashSet<Bird>();
            int samples = 0, misses = 0, spooks = 0;
            bool feedShot = false, jamShots = false;
            while (game.Result == MissionResult.None && game.Phase != MissionPhase.Extract && Time.time - started < 420f)
            {
                if ((scout == null || !scout.IsAlive || Mimic.ShotsLeftFor(scout) <= 0) && scoutIndex + 1 < scouts.Count)
                {
                    scout = scouts[++scoutIndex];
                    rts.selected.Clear();
                    rts.selected.Add(scout);
                }
                // The nearest bird not yet looked at (or every bird again once all have been).
                Bird next = null;
                float best = float.MaxValue;
                foreach (var b in Wildlife.Birds)
                {
                    if (b == null || b.Down || !b.gameObject.activeInHierarchy || sampled.Contains(b)) continue;
                    float d = Vector3.Distance(scout.transform.position, b.transform.position);
                    if (d < best)
                    {
                        best = d;
                        next = b;
                    }
                }
                if (next == null)
                {
                    sampled.Clear();
                    yield return null;
                    continue;
                }
                sampled.Add(next);
                samples++;
                Mimic.Act(next, scout);
                float look = Time.time;
                var e = Mimic.Of(next);
                while (Time.time - look < 20f * Scale.TravelTime && Vector3.Distance(scout.transform.position, next.transform.position) > Scale.D(60f)) yield return null;
                look = Time.time;
                while (Time.time - look < 12f && e.Points == 0 && game.Result == MissionResult.None) yield return null;
                if (e.Points == 0 && next.Robot)
                    log.WriteLine($"  {name}: gave up on a robot at {Time.time - started:F0} s: {Vector3.Distance(scout.transform.position, next.transform.position):F0} m, " +
                                  $"watched {e.Watched:F1} s, close {e.Close:F1} s, hover {e.HoverWatched:F1} s, mingling {next.Mingling}, occluded {e.Occluded}, " +
                                  $"tracked {Mimic.Tracked == next}, feed on the scout {DroneFeed.Instance.Source == scout}, zoom {DroneFeed.Instance.Zoom:F1}");
                if (e.Points == 0) continue;
                // A clue: stay on it.
                while (!e.Confirmed && !next.Down && !next.Escaped && Time.time - look < 90f && game.Result == MissionResult.None) yield return null;
                if (!e.Confirmed) continue;
                if (!feedShot)
                {
                    rts.LookAt(scout.transform.position, 55f * Scale.Camera, 30f);
                    yield return new WaitForSeconds(0.4f);
                    yield return Shot(prefix + "6_hunt_confirmed_feed");
                    feedShot = true;
                }
                // Hide and Seek: the jam on the confirmed spotter, then (looking away, as if for 38 s) fading.
                if (spotting && !jamShots && Mimic.Jammed(next))
                {
                    jamShots = true;
                    rts.LookAt(next.transform.position, 45f * Scale.Camera, 30f);
                    yield return new WaitForSeconds(0.5f);
                    yield return Shot("68_seek_jammed");
                    Mimic.Track(null);
                    Mimic.Of(next).JammedUntil = Time.time + 7f;
                    yield return new WaitForSeconds(0.6f);
                    yield return Shot("69_seek_jam_fading");
                    log.WriteLine($"{name}: looking away, the jam is {(Mimic.Jammed(next) ? "holding" : "gone")} with {Mimic.JamLeft(next):F0} s left, spotters {game.Spotters}");
                    Mimic.Act(next, scout);
                }
                float aiming = Time.time;
                while (!next.Down && !next.Escaped && Mimic.ShotsLeftFor(scout) > 0 && game.Result == MissionResult.None && Time.time - aiming < 120f)
                {
                    if (Mimic.WouldHit(next, scout) && !Mimic.Fire(next, scout)) misses++;
                    yield return new WaitForSeconds(0.2f);
                }
            }
            var crows = Wildlife.Birds.FindAll(b => b != null && !b.Robot);
            spooks = Wildlife.Birds.FindAll(b => b != null && b.Robot && b.Spooked).Count;
            int falseConfirms = crows.FindAll(b => Mimic.Of(b).Confirmed).Count;
            float hunted = Time.time - started;
            game.Recall(new List<Drone>());
            for (float t = 0f; t < 90f && game.Result == MissionResult.None; t += 0.5f) yield return new WaitForSeconds(0.5f);
            float until = Time.realtimeSinceStartup + 10f;
            while (Time.unscaledTime < game.ResultsAt + 0.5f && Time.realtimeSinceStartup < until) yield return null;
            yield return Shot(prefix + "7_hunt_result");
            log.WriteLine($"{name}: {game.Result} ({game.ResultReason}), robots {game.TargetsDestroyed}/{game.TargetsTotal} in {hunted:F0} s, " +
                          $"{samples} birds sampled, {misses} misses, {spooks} spooked, scouts used {scoutIndex + 1}, crows confirmed {falseConfirms}, stars {game.Stars}" +
                          (spotting ? $", salvos {game.Salvo}, base {game.BaseIntegrity:0}%" : ""));
            if (game.Result != MissionResult.Won || falseConfirms > 0) errors++;
        }

        // Hide and Seek with nobody hunting, at 8x: the salvos must grow by half each minute and the base fall at the
        // sixth, about six minutes in. A shot of the base under fire at the fifth.
        IEnumerator SeekIgnored(SandboxGame game, RtsController rts)
        {
            int seek = Missions.All.FindIndex(m => m.spotterShelling > 0f);
            if (seek < 0) yield break;
            game.StartMission(seek);
            game.CloseBriefing();
            rts.LookAt(game.basePosition, 85f * Scale.Camera, 30f);
            Time.timeScale = 8f;
            var integrity = new List<string>();
            int seen = 0;
            bool shot = false;
            while (game.Result == MissionResult.None && game.MissionTime < 480f)
            {
                if (game.Salvo > seen)
                {
                    seen = game.Salvo;
                    integrity.Add($"{game.BaseIntegrity:0}");
                }
                if (!shot && game.Salvo == 4 && game.NextSalvoAt - game.MissionTime < 6f)
                {
                    Time.timeScale = 1f;
                    yield return new WaitForSeconds(4f);
                    yield return Shot("63_seek_ring_and_strip");
                    yield return new WaitForSeconds(2.6f);
                    yield return Shot("64_seek_salvo_lands");
                    shot = true;
                    Time.timeScale = 8f;
                }
                yield return null;
            }
            Time.timeScale = 1f;
            log.WriteLine($"Hide and Seek, nobody hunting: {game.Result} ({game.ResultReason}) at {game.MissionTime:F0} s, after salvo {game.Salvo}; " +
                          $"integrity after each: {string.Join(", ", integrity)}");
            if (game.Result != MissionResult.Failed || game.Salvo != 6) errors++;
            float until = Time.realtimeSinceStartup + 10f;
            while (Time.unscaledTime < game.ResultsAt + 0.5f && Time.realtimeSinceStartup < until) yield return null;
            yield return Shot("65_seek_lost");
        }

        // The identification hunt (Codex's campaign situation): generated in a village sector and played by a bot that
        // takes a Scout over each look-alike in turn and watches it, strikes only the ones shown to be real, and
        // brings the Scout home. It has to be winnable this way, with 3-5 look-alikes and 1-2 real ones.
        IEnumerator IdentificationHuntCheck(SandboxGame game, RtsController rts)
        {
            var spot = new Hotspot
            {
                kind = SectorKind.VillageCluster, seed = 4321, situation = Situation.IdentificationHunt,
                daylight = Daylight.Day, weather = Weather.Clear, heat = 0.5f, sectorName = "Test",
            };
            game.PlayHotspot(spot);
            while (game.Loading) yield return null;
            yield return new WaitForSecondsRealtime(0.4f);
            yield return Shot("70_idhunt_briefing");
            game.CloseBriefing();
            var group = game.enemies.FindAll(e => e != null && (e.isTarget || e.isDecoy));
            int real = group.FindAll(e => e.isTarget).Count, struck = 0;
            var scout = game.SpawnDrone(DroneType.Scout);
            int scouts = 1;
            float started = Time.time;
            bool shot = false;
            foreach (var e in group)
            {
                // Once the real ones are down the mission moves to extraction; checking more look-alikes would leave the Scout behind.
                if (game.Result != MissionResult.None || game.Phase == MissionPhase.Extract) break;
                if (e == null || !e.IsAlive) continue;
                if (scout == null || !scout.IsAlive)
                {
                    scout = game.SpawnDrone(DroneType.Scout); // the last one was shot down
                    scouts++;
                }
                if (scout == null) break;
                scout.MoveTo(e.ContactPosition);
                // Both waits stop if the mission ends: time freezes for the results panel and WaitForSeconds would never return.
                for (float t = 0f; t < 25f && scout != null && e != null && game.Result == MissionResult.None && Vector3.Distance(scout.transform.position, e.transform.position) > Scale.D(14f); t += 0.25f)
                    yield return new WaitForSeconds(0.25f);
                for (float t = 0f; t < 6f && e != null && game.Result == MissionResult.None && e.Intel < Intel.Identified && !e.ExposedDecoy; t += 0.25f) yield return new WaitForSeconds(0.25f);
                if (e == null) continue;
                if (!shot)
                {
                    shot = true;
                    rts.LookAt(e.transform.position, 50f * Scale.Camera, 40f);
                    yield return new WaitForSeconds(0.3f);
                    yield return Shot("71_idhunt_watching");
                }
                if (e.isTarget && e.IsAlive && e.Intel >= Intel.Identified)
                {
                    var k = game.SpawnDrone(DroneType.Kamikaze);
                    if (k != null) { k.Attack(e); struck++; }
                }
            }
            for (float t = 0f; t < 40f && game.Phase != MissionPhase.Extract && game.Result == MissionResult.None; t += 0.5f) yield return new WaitForSeconds(0.5f);
            int exposed = group.FindAll(e => e != null && e.ExposedDecoy).Count;
            game.Recall(new List<Drone>());
            for (float t = 0f; t < 90f && game.Result == MissionResult.None; t += 0.5f) yield return new WaitForSeconds(0.5f);
            float until = Time.realtimeSinceStartup + 10f;
            while (Time.unscaledTime < game.ResultsAt + 0.5f && Time.realtimeSinceStartup < until) yield return null;
            yield return Shot("72_idhunt_result");
            log.WriteLine($"Identification hunt: {group.Count} look-alikes ({group[0].type}), {real} real, {exposed} decoys exposed by watching, {struck} strikes, {scouts} scouts, " +
                          $"result {game.Result} ({game.ResultReason}) in {Time.time - started:F0} s, par {game.Mission.par}");
            if (game.Result != MissionResult.Won || group.Count < 3 || group.Count > 5 || real < 1 || real > 2) errors++;
        }

        // Does the treetop line-of-sight check (Canopy.Blocks) agree with the picture? 50 views of a 1 m sphere (a bird
        // with its wings out) placed near trees, through photo mode at 12 degrees, each captured with and without it:
        // the pixels that change show how much of it is in sight. Refusing a shot at a plainly visible target is the
        // failure that matters; allowing one at a hidden target is the lesser one.
        IEnumerator OcclusionCheck(SandboxGame game)
        {
            var photo = PhotoMode.Instance;
            if (photo == null) yield break;
            game.StartSandbox();
            var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere); // its collider gives photo mode its focus
            marker.GetComponent<Renderer>().sharedMaterial = Art.NewMaterial(new Color(0.9f, 0.1f, 0.8f));
            photo.Enter();
            Time.timeScale = 0f;
            // The pixel test compares two frames, so draw them as they are rather than reconstructed by the upscaler.
            var cameraData = Camera.main.GetComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData>();
            bool upscaled = cameraData != null && cameraData.allowDynamicResolution;
            if (cameraData != null) cameraData.allowDynamicResolution = false;
            var rng = new System.Random(5);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            int views = 0, tries = 0, agree = 0, falseBlocked = 0, falseClear = 0, partly = 0;
            bool shotBlocked = false, shotClear = false;
            Vector3 centre = game.basePosition;
            // The sandbox after the hunts can be a sector with no wood by its base (hd168-169 found none within 260 m):
            // look round the sector in rings for the nearest wood instead, and skip the check if there's none at all.
            if (Canopy.TopNear(centre, 260f) < Ground.HeightAt(centre) + 6f)
            {
                bool found = false;
                for (float reach = 300f; reach <= 1000f && !found; reach += 100f)
                    for (float angle = 0f; angle < 360f && !found; angle += 15f)
                    {
                        var p = game.basePosition + new Vector3(Mathf.Cos(angle * Mathf.Deg2Rad), 0f, Mathf.Sin(angle * Mathf.Deg2Rad)) * reach;
                        if (Canopy.TopNear(p, 60f) > Ground.HeightAt(p) + 8f) { centre = p; found = true; }
                    }
                if (!found)
                {
                    // A sector with trees but no canopy grid at all would mean line of sight ignores every tree: an error.
                    float tallest = Canopy.TopNear(game.basePosition, 1200f);
                    log.WriteLine($"Occlusion check: skipped, no wood within 1 km of the base (origin {FarTerrain.Origin.x:F0}, {FarTerrain.Origin.y:F0}); " +
                                  $"canopy grid {(tallest == float.MinValue ? "MISSING" : "present")}, Undergrowth trees {Undergrowth.Trees.Count}");
                    if (tallest == float.MinValue && Undergrowth.Trees.Count > 0) errors++;
                    photo.Exit();
                    Destroy(marker);
                    if (cameraData != null) cameraData.allowDynamicResolution = upscaled;
                    Time.timeScale = 1f;
                    yield break;
                }
            }
            while (views < 50 && tries < 3000)
            {
                tries++;
                var target = centre + new Vector3(R(-260f, 260f), 0f, R(-260f, 260f));
                if (Canopy.TopNear(target, 10f) < Ground.HeightAt(target) + 6f) continue; // near trees only
                target.y = Ground.HeightAt(target) + R(10f, 24f);
                float yaw = R(0f, 2f * Mathf.PI), d = R(15f, 45f);
                var eye = target + new Vector3(Mathf.Sin(yaw) * d, 0f, Mathf.Cos(yaw) * d);
                eye.y = Mathf.Max(Ground.HeightAt(eye) + 18f, target.y + R(-4f, 12f), Canopy.TopNear(eye, 3f) + 1.5f); // above the trees, as drones fly
                float range = Vector3.Distance(eye, target);
                if (Ground.Raycast(new Ray(eye, (target - eye) / range), out Vector3 hill) && Vector3.Distance(eye, hill) < range) continue;
                views++;
                bool blocked = Canopy.Blocks(eye, target);
                photo.PlaceLooking(eye, target, 12f);
                marker.transform.position = target;
                marker.SetActive(false);
                yield return new WaitForSecondsRealtime(0.3f);
                yield return new WaitForEndOfFrame();
                var without = ScreenCapture.CaptureScreenshotAsTexture();
                marker.SetActive(true);
                yield return new WaitForSecondsRealtime(0.3f);
                yield return new WaitForEndOfFrame();
                var with = ScreenCapture.CaptureScreenshotAsTexture();
                // How much of the sphere shows, against how much would in open sky.
                float pixelsPerMetre = with.height / (2f * range * Mathf.Tan(6f * Mathf.Deg2Rad));
                float radius = 0.5f * pixelsPerMetre, expected = Mathf.PI * radius * radius;
                int half = Mathf.Min(with.height / 2 - 1, Mathf.CeilToInt(radius) + 8);
                int cx = with.width / 2, cy = with.height / 2, changed = 0;
                var a = without.GetPixels(cx - half, cy - half, 2 * half, 2 * half);
                var b = with.GetPixels(cx - half, cy - half, 2 * half, 2 * half);
                for (int i = 0; i < a.Length; i++)
                    if (Mathf.Abs(a[i].r - b[i].r) + Mathf.Abs(a[i].g - b[i].g) + Mathf.Abs(a[i].b - b[i].b) > 0.12f) changed++;
                UnityEngine.Object.Destroy(without);
                UnityEngine.Object.Destroy(with);
                float shown = changed / expected;
                if (shown >= 0.5f && blocked)
                {
                    falseBlocked++;
                    log.WriteLine($"  refused but visible ({shown:P0} shown): eye {eye.y - Ground.HeightAt(eye):F0} m up, target {target.y - Ground.HeightAt(target):F0} m up, " +
                                  $"{range:F0} m; {Canopy.Explain(eye, target)}");
                    if (!shotBlocked) { shotBlocked = true; yield return Shot("58_los_refused_but_visible"); }
                }
                else if (shown <= 0.15f && !blocked)
                {
                    falseClear++;
                    if (!shotClear) { shotClear = true; yield return Shot("59_los_allowed_but_hidden"); }
                }
                else if (shown > 0.15f && shown < 0.5f) partly++;
                else agree++;
            }
            Time.timeScale = 1f;
            photo.Exit();
            if (cameraData != null) cameraData.allowDynamicResolution = upscaled;
            UnityEngine.Object.Destroy(marker);
            log.WriteLine($"Occlusion check: {views} views ({tries} tries), {agree} agree, {partly} partly hidden (either verdict fair), " +
                          $"{falseBlocked} refused though plainly visible, {falseClear} allowed though hidden");
            if (views < 50 || falseBlocked > views / 20 || falseClear > views / 10) errors++;
        }

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
                log.WriteLine($"  build ms: {World.Timings}");
                log.WriteLine($"  world build stages: {FarTerrain.BuildTimings}");
                log.WriteLine($"Sector {n}: {plan.name} ({SectorTypes.Get(kind).name}, seed {seed}) built in {took:F1}s: " +
                              $"{plan.roads.Count} roads, {plan.villages.Count} villages, {plan.bridges.Count} bridges, {plan.ponds.Count} pools, " +
                              $"{plan.railways.Count} railways, {plan.sites.Count} sites, {MapLayout.Fields.Count} fields; " +
                              $"{Situations.Name(situation)}: {game.Mission.units.Count} units, {game.TargetsTotal} targets, " +
                              $"convoy route {(game.Mission.convoyRoute != null ? game.Mission.convoyRoute.Count : 0)} points, " +
                              $"{game.Mission.units.FindAll(u => Undergrowth.UnderTrees(new Vector2(u.position.x, u.position.z), 4f)).Count} under trees, " +
                              $"{Stacked(game.Mission.units)} stacked");
                // The permanent landmarks (WorldPlan.Features) and this sector's copy of any it holds.
                string landmarks = LandmarkCheck.Run(plan);
                log.WriteLine($"  {landmarks}");
                if (landmarks.StartsWith("LANDMARK FAIL")) errors++;
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
                // Heights from the ground: a sector cut from the world can stand 200 m up.
                Vector3 eye = baseAt + back * 60f;
                eye.y = Ground.HeightAt(eye) + 140f;
                photo.PlaceLooking(eye, Ground.OnGround((baseAt + hq) / 2f), 55f);
                yield return new WaitForSeconds(1.5f);
                yield return ForestMassesSettle($"31_sector_{n:00}_{kind}");
                yield return Shot($"31_sector_{n:00}_{kind}");
                if (n == 0 || n == 11 || n == 5) log.WriteLine($"  textures after sector {n}: {TextureTally()}");
                // What of the woods this view drew: hd145's dense forest drew nothing in a Deep Forest sector.
                log.WriteLine($"  {ForestLine()}");
                log.WriteLine($"  forest screen: full/reduced/card {FarScatter.FullTreesDrawn}/{FarScatter.ReducedTreesDrawn}/{FarScatter.CardTreesDrawn}, effective px {FarScatter.TierPixels.x:F1}/{FarScatter.TierPixels.y:F1}/{FarScatter.TierPixels.z:F1}, crown reach {FarScatter.CrownReach:F0} m, ready radius {FarScatter.ReadyRadius:F0} m; missing {ForestHlod.MissingBlocks}, coarse {ForestHlod.CoarseBlocks}, near coarse {ForestHlod.NearCoarseBlocks}, build {ForestHlod.BuildMs:F3} ms");
                log.WriteLine($"  forest materials: fallbacks {ForestMaterials.FallbackCount}, remapped variants {ForestMaterials.RemappedCount}; last fallback {ForestMaterials.LastFallback}");
                log.WriteLine($"  forest motion: camera {FarScatter.CameraSpeed:F1} m/s, canopy slice {ForestHlod.SliceBudgetMs:F2} ms; recipe cells {SectorForest.LocalCells.Count}, logical stems {SectorForest.Stems.Count}, woods build {SectorForest.BuildMilliseconds:F1} ms");
                photo.Exit();
                // The mission's targets from the game camera.
                DayNight.Instance.SetTime(game.Mission.startHour, 0f);
                rts.LookAt(game.Mission.cameraFocus, 70f * Scale.Camera, game.Mission.cameraYaw);
                // Real seconds: the canopy wait above lets the mission run long enough to pause itself (a tip, an
                // alert), and a game-time wait under a pause never ended (hd149 hung here).
                yield return new WaitForSecondsRealtime(2f);
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
                rts.LookAt(nightjar.transform.position, 40f * Scale.Camera, 45f);
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
                rts.LookAt(nightjar.transform.position, 30f * Scale.Lens, 45f);
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
            yield return new WaitForSecondsRealtime(0.4f);
            log.WriteLine($"Mission 1 tips: {Coach.Count}, showing \"{Coach.Current}\"");
            if (Settings.Tips && Coach.Count == 0) errors++;
            yield return Shot("08a_first_tip");
            game.ToggleThermal();
            log.WriteLine($"Mission 1 thermal with no tech: {DayNight.Instance.Thermal} (\"{game.Message}\")");
            // Recon: a Scout flies out over the rough contacts until the trucks are found.
            var targets = game.enemies.FindAll(e => e.isTarget);
            log.WriteLine($"Mission 1 phase at start: {game.Phase}, target intel {(targets.Count > 0 ? targets[0].Intel.ToString() : "-")}");
            var recon = game.SpawnDrone(DroneType.Scout);
            if (recon != null)
            {
                recon.MoveTo(Old(-30f, -85f)); // south of the AA gun's ring
                foreach (var site in game.enemies.FindAll(e => e.isTarget || e.isDecoy)) recon.MoveTo(site.ContactPosition, true);
            }
            rts.LookAt(targets.Count > 0 ? targets[0].transform.position : game.basePosition, 75f * Scale.Camera, 70f);
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
            rts.LookAt(game.basePosition, 70f * Scale.Camera, 70f);
            for (float t = 0f; t < 40f && game.Result == MissionResult.None; t += 0.5f)
                yield return new WaitForSecondsRealtime(0.5f);
            log.WriteLine($"Mission 1: {game.Result}, {game.ResultReason} Stars {game.Stars}, drones used {game.DronesUsed}, " +
                          $"lost {game.dronesLost}, home {game.DronesHome}, targets {game.TargetsDestroyed}/{game.TargetsTotal}, time {game.MissionTime:F1}s, " +
                          $"points +{game.PointsEarned} (stars {game.StarBonus}), total {Tech.Points}; tips done {Coach.Step}/{Coach.Count}");
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
                rts.LookAt(bomber.transform.position, 50f * Scale.Camera, 60f);
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
            rts.LookAt(Old(-10f, 30f), 105f * Scale.Camera, 30f);
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
                if (hidden[0] != null) rts.LookAt(hidden[0].transform.position, 30f * Scale.Lens, 200f);
                yield return new WaitForSecondsRealtime(1f);
                yield return Shot("11_spotted");
            }

            game.StartMission(4);
            game.CloseBriefing();
            rts.LookAt(game.enemyHqPosition, 55f * Scale.Camera, 210f);
            yield return new WaitForSecondsRealtime(1f);
            yield return Shot("12_command_post");
            log.WriteLine($"Command Post: {game.TargetsTotal} target, {game.enemies.Count} enemies");

            // Long Watch (Shadow), played twice. First properly: a Scout shadows the column side-on, 38 m out from
            // its middle (outside every vehicle's 26 m, inside the Scout's 46 m), on the side away from the AA guns,
            // then flies home with the intel.
            // Then badly: a Scout sits right on top of the column until it's spotted three times.
            int watch = Missions.All.FindIndex(m => m.shadowSeconds > 0f);
            if (watch >= 0)
            {
                game.StartMission(watch);
                game.CloseBriefing();
                var shadow = game.SpawnDrone(DroneType.Scout);
                var column = game.enemies.FindAll(e => e.isTarget);
                var lead = column.Count > 0 ? column[0] : null;
                bool extractShot = false;
                int detectionsSeen = 0;
                // Which side of the road to keep to (right of travel, or left): the one that stays furthest from the
                // AA guns all along the route.
                float GunClearance(Vector3 at)
                {
                    float nearest = float.MaxValue;
                    foreach (var e in game.enemies)
                        if (e != null && e.IsAlive && e.type == EnemyType.AAGun)
                            nearest = Mathf.Min(nearest, Vector3.Distance(e.transform.position, at));
                    return nearest;
                }
                float shadowSide = 1f, safest = -1f;
                var watchRoute = game.Mission.convoyRoute;
                foreach (float sign in new[] { 1f, -1f })
                {
                    float closest = float.MaxValue;
                    for (int i = 1; watchRoute != null && i < watchRoute.Count; i++)
                    {
                        Vector3 dir = watchRoute[i] - watchRoute[i - 1];
                        dir.y = 0f;
                        if (dir.sqrMagnitude < 0.01f) continue;
                        closest = Mathf.Min(closest, GunClearance(watchRoute[i] + Vector3.Cross(Vector3.up, dir.normalized) * sign * Scale.D(38f)));
                    }
                    if (closest > safest)
                    {
                        safest = closest;
                        shadowSide = sign;
                    }
                }
                for (float t = 0f; t < 120f * Scale.TravelTime && shadow != null && lead != null && game.Result == MissionResult.None; t += 0.5f)
                {
                    if (game.Phase == MissionPhase.Extract)
                    {
                        shadow.MoveTo(game.basePosition);
                        rts.LookAt(shadow.transform.position, 70f * Scale.Camera, 60f);
                        if (!extractShot)
                        {
                            extractShot = true;
                            yield return Shot("12c_long_watch_extract");
                        }
                    }
                    else
                    {
                        Vector3 middle = Vector3.zero;
                        int alive = 0;
                        foreach (var e in column)
                            if (e != null && e.IsAlive) { middle += e.transform.position; alive++; }
                        if (alive == 0) break;
                        middle /= alive;
                        Vector3 side = Vector3.Cross(Vector3.up, lead.transform.forward).normalized * shadowSide;
                        // Keep halfway between the column's notice range and our sight, measured from its nearest
                        // vehicle rather than its middle (on a bend the ends swing in), and back straight off if inside.
                        float notice = game.ShadowNoticeRange, keep = (notice + Recon.IdentifyRange * Recon.WeatherFactor) / 2f;
                        Vector3 station = middle + side * Scale.D(38f);
                        for (int pass = 0; pass < 3; pass++)
                            foreach (var e in column)
                            {
                                if (e == null || !e.IsAlive) continue;
                                Vector3 away = station - e.transform.position;
                                away.y = 0f;
                                if (away.magnitude < keep) station = e.transform.position + away.normalized * keep;
                            }
                        float closest = float.MaxValue;
                        Vector3 closestAt = middle;
                        foreach (var e in column)
                        {
                            if (e == null || !e.IsAlive) continue;
                            Vector3 off = shadow.transform.position - e.transform.position;
                            off.y = 0f;
                            if (off.magnitude < closest) { closest = off.magnitude; closestAt = e.transform.position; }
                        }
                        Vector3 back = shadow.transform.position - closestAt;
                        back.y = 0f;
                        shadow.MoveTo(closest < notice + Scale.D(6f) ? shadow.transform.position + back.normalized * (keep - closest + Scale.D(6f)) : station);
                        if (game.ShadowDetections > detectionsSeen)
                        {
                            detectionsSeen = game.ShadowDetections;
                            log.WriteLine($"  Long Watch detection {detectionsSeen} at {game.MissionTime:F0} s: nearest vehicle {closest:F0} m, notice {notice:F0} m, watched {game.ShadowProgress:F0} s");
                        }
                        rts.LookAt(middle, 70f * Scale.Camera, 60f);
                        if (Mathf.Approximately(t, 25f)) yield return Shot("12b_long_watch");
                    }
                    yield return new WaitForSecondsRealtime(0.5f);
                }
                log.WriteLine($"Long Watch (careful): watched {game.ShadowProgress:F0}/{Missions.All[watch].shadowSeconds:F0} s, intel {game.ShadowIntel}/3, " +
                              $"detected {game.ShadowDetections}, phase {game.Phase}, result {game.Result} ({game.ResultReason})");

                game.StartMission(watch);
                game.CloseBriefing();
                shadow = game.SpawnDrone(DroneType.Scout);
                column = game.enemies.FindAll(e => e.isTarget);
                lead = column.Count > 0 ? column[0] : null;
                bool detectedShot = false;
                int scoutsLost = 0;
                for (float t = 0f; t < 60f && lead != null && game.Result == MissionResult.None; t += 0.5f)
                {
                    // Shot down by the escort: launch another, as a player would.
                    if (shadow == null)
                    {
                        scoutsLost++;
                        shadow = game.SpawnDrone(DroneType.Scout);
                    }
                    shadow.MoveTo(lead.transform.position);
                    rts.LookAt(lead.transform.position, 70f * Scale.Camera, 60f);
                    if (!detectedShot && game.ShadowDetections > 0)
                    {
                        detectedShot = true;
                        yield return Shot("12d_long_watch_detected");
                    }
                    yield return new WaitForSecondsRealtime(0.5f);
                }
                log.WriteLine($"Long Watch (reckless): detected {game.ShadowDetections}, watched {game.ShadowProgress:F0} s, scouts shot down {scoutsLost}, " +
                              $"result {game.Result} ({game.ResultReason})");
            }

            // Last Delivery (Interdiction), played twice. First: a Scout looks at both tankers from close (which
            // spooks the column onto the north road), then both Strike drones go for the loaded one and everyone
            // flies home. Then nobody stops it, at four times speed, and the loaded tanker should get away.
            int delivery = Missions.All.FindIndex(m => m.convoyBranch != null);
            if (delivery >= 0)
            {
                game.StartMission(delivery);
                game.CloseBriefing();
                yield return new WaitForSecondsRealtime(1f);
                yield return Shot("13a_last_delivery_start");
                var eye = game.SpawnDrone(DroneType.Scout);
                var tankers = game.enemies.FindAll(e => e.type == EnemyType.Tanker);
                var loaded = game.enemies.Find(e => e.isTarget);
                int loadedSlot = loaded != null ? tankers.IndexOf(loaded) : -1;
                int looked = 0;
                bool struck = false, divertShot = false;
                float etaSeen = -1f;
                for (float t = 0f; t < 200f && game.Result == MissionResult.None; t += 0.5f)
                {
                    if (etaSeen < 0f && game.ConvoyEta >= 0f) etaSeen = game.ConvoyEta;
                    if (eye != null && looked < tankers.Count)
                    {
                        var next = tankers[looked];
                        if (next == null || next.Intel == Intel.Confirmed) looked++;
                        else eye.MoveTo(Ground.OnGround(next.MarkerPosition)); // where we last saw it, not where it is
                    }
                    else if (!struck)
                    {
                        // Only what the player could know: the tanker that hasn't shown itself to be the empty one.
                        var pick = tankers.Find(tk => tk != null && tk.IsAlive && tk.Intel == Intel.Confirmed && !tk.ExposedDecoy);
                        if (pick != null)
                        {
                            struck = true;
                            for (int i = 0; i < 2; i++)
                            {
                                var d = game.SpawnDrone(DroneType.Strike);
                                if (d != null) d.Attack(pick);
                            }
                            if (eye != null) eye.MoveTo(game.basePosition);
                        }
                    }
                    if (game.Phase == MissionPhase.Extract)
                        foreach (var d in game.drones)
                            if (d != null) d.MoveTo(game.basePosition);
                    Vector3 look = loaded != null ? loaded.transform.position : game.basePosition;
                    rts.LookAt(look, 90f * Scale.Camera, 30f);
                    if (!divertShot && game.Diverted)
                    {
                        divertShot = true;
                        yield return Shot("13b_last_delivery_diverted");
                    }
                    yield return new WaitForSecondsRealtime(0.5f);
                }
                log.WriteLine($"Last Delivery (look, then strike): loaded tanker was #{loadedSlot + 1} of {tankers.Count}, confirmed {looked}, diverted {game.Diverted}, first ETA shown {etaSeen:F0} s, " +
                              $"result {game.Result} ({game.ResultReason}), drones used {game.DronesUsed}, {game.MissionTime:F0} s");
                yield return Shot("13c_last_delivery_result");

                game.StartMission(delivery);
                game.CloseBriefing();
                Time.timeScale = 4f;
                for (float t = 0f; t < 60f && game.Result == MissionResult.None; t += 0.5f) yield return new WaitForSecondsRealtime(0.5f);
                Time.timeScale = 1f;
                log.WriteLine($"Last Delivery (nobody stops it): result {game.Result} ({game.ResultReason}) at {game.MissionTime:F0} s");
            }

            // Silent Corridor (Suppression), played twice. First: a Strike drone fires on each AA gun (a missile hit
            // pins one for 25 s), then the Nightjar goes in once both are pinned or dead, bombs the hardened depot and
            // everyone flies home. Then the mistake: the Nightjar alone, straight in.
            int corridor = Missions.All.FindIndex(m => m.nightjar);
            if (corridor >= 0)
            {
                game.StartMission(corridor);
                game.CloseBriefing();
                var guns = game.enemies.FindAll(e => e.type == EnemyType.AAGun);
                var depot = game.enemies.Find(e => e.isTarget);
                foreach (var gun in guns)
                {
                    var d = game.SpawnDrone(DroneType.Strike);
                    if (d != null) d.Attack(gun);
                }
                Drone nightjar = null;
                bool pinnedShot = false, bombingShot = false;
                int pinnedSeen = 0;
                for (float t = 0f; t < 120f && game.Result == MissionResult.None; t += 0.5f)
                {
                    bool open = guns.TrueForAll(g => g == null || !g.IsAlive || g.Suppressed);
                    int pinned = guns.FindAll(g => g != null && g.IsAlive && g.Suppressed).Count;
                    pinnedSeen = Mathf.Max(pinnedSeen, pinned);
                    if (!pinnedShot && pinned > 0)
                    {
                        pinnedShot = true;
                        rts.LookAt(guns.Find(g => g != null && g.Suppressed).transform.position, 70f * Scale.Camera, 30f);
                        yield return Shot("14a_corridor_pinned");
                    }
                    if (nightjar == null && (open || t > 30f) && depot != null)
                    {
                        nightjar = game.SpawnDrone(DroneType.Bomber);
                        if (nightjar != null) nightjar.Attack(depot);
                    }
                    if (game.Phase == MissionPhase.Extract)
                        foreach (var d in game.drones)
                            if (d != null) d.MoveTo(game.basePosition);
                    if (nightjar != null)
                    {
                        rts.LookAt(nightjar.transform.position, 80f * Scale.Camera, 30f);
                        if (!bombingShot && depot != null && Vector3.Distance(nightjar.transform.position, depot.transform.position) < Scale.D(20f))
                        {
                            bombingShot = true;
                            yield return Shot("14b_corridor_bombing");
                        }
                    }
                    yield return new WaitForSecondsRealtime(0.5f);
                }
                string depotState = depot == null ? "destroyed" : depot.hp.ToString("F0") + " hp";
                log.WriteLine($"Silent Corridor (pin, then bomb): most guns pinned at once {pinnedSeen}, guns left {guns.FindAll(g => g != null && g.IsAlive).Count}, " +
                              $"depot {depotState}, result {game.Result} ({game.ResultReason}), drones used {game.DronesUsed}, {game.MissionTime:F0} s");
                yield return Shot("14c_corridor_result");

                game.StartMission(corridor);
                game.CloseBriefing();
                depot = game.enemies.Find(e => e.isTarget);
                nightjar = game.SpawnDrone(DroneType.Bomber);
                if (nightjar != null && depot != null) nightjar.Attack(depot);
                for (float t = 0f; t < 60f && game.Result == MissionResult.None; t += 0.5f)
                {
                    if (game.Phase == MissionPhase.Extract && nightjar != null) nightjar.MoveTo(game.basePosition);
                    yield return new WaitForSecondsRealtime(0.5f);
                }
                string nightjarState = nightjar != null ? nightjar.hp.ToString("F0") + " hp" : "lost";
                depotState = depot == null ? "destroyed" : depot.hp.ToString("F0") + " hp";
                log.WriteLine($"Silent Corridor (Nightjar alone): nightjar {nightjarState}, depot {depotState}, result {game.Result} ({game.ResultReason})");
            }

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
                        if (gap > Scale.D(90f)) Aim(a + (b - a).normalized * Scale.D(25f), 50f * Scale.Camera);
                        else Aim(Vector3.Lerp(b, a, 0.35f), Mathf.Clamp(gap * 0.6f * Scale.Lens + 28f * Scale.Camera, 30f * Scale.Camera, 70f * Scale.Camera));
                    }
                    else Aim(Lead(), 55f * Scale.Camera);
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
        float shotDistance = 55f * Scale.Camera;

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
            camFocus = Vector3.Distance(camFocus, shotFocus) > Scale.D(120f) ? shotFocus : Vector3.Lerp(camFocus, shotFocus, k);
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
            return Busy(current) && Gap(current) < bestGap + Scale.D(40f) ? current : best;
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
        // The same two views (the grass by the base, the woods beside it) with each lighting effect switched in turn,
        // to see what each one adds or spoils: bounce light and micro-shadows (both off in play, so switched on),
        // contact shadows, the old 1.5 m ambient occlusion, and all of them with the old 512 shadow map ("before").
        // Then the frame time over the woods for 1024 or 2048 shadows, each with and without bounce light.
        IEnumerator LightingCheck(SandboxGame game, RtsController rts)
        {
            var profile = Atmosphere.Profile;
            if (profile == null) { log.WriteLine("Lighting check: no profile"); yield break; }
            profile.TryGet(out UnityEngine.Rendering.HighDefinition.GlobalIllumination bounce);
            profile.TryGet(out UnityEngine.Rendering.HighDefinition.ContactShadows contact);
            profile.TryGet(out UnityEngine.Rendering.HighDefinition.MicroShadowing micro);
            profile.TryGet(out UnityEngine.Rendering.HighDefinition.ScreenSpaceAmbientOcclusion ao);
            var sun = RenderSettings.sun != null ? RenderSettings.sun.GetComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalLightData>() : null;
            if (bounce == null || contact == null || micro == null || ao == null || sun == null) { log.WriteLine("Lighting check: missing effects"); yield break; }
            float radius = ao.radius.value, intensity = ao.intensity.value;
            bool microOn = micro.enable.value, bounceOn = bounce.enable.value; // both off in play since critique 16
            void Set(bool gi, bool cs, bool ms, bool aoNow, int shadow)
            {
                bounce.enable.value = gi; contact.enable.value = cs; micro.enable.value = ms;
                ao.radius.value = aoNow ? radius : 1.5f; ao.intensity.value = aoNow ? intensity : 0.8f;
                sun.SetShadowResolution(shadow);
            }
            var variants = new (string name, bool gi, bool cs, bool ms, bool ao, int shadow)[]
            {
                ("all", bounceOn, true, microOn, true, 2048), ("bounce_on", true, true, microOn, true, 2048), ("no_contact", bounceOn, false, microOn, true, 2048),
                ("micro_on", bounceOn, true, true, true, 2048), ("old_ao", bounceOn, true, microOn, false, 2048), ("before", false, false, false, false, 512),
            };
            var views = new (string name, float distance, float yaw)[] { ("grass", 18f * Scale.Lens, 30f), ("woods", 42f * Scale.Camera, 20f) };
            foreach (var view in views)
            {
                rts.LookAt(game.basePosition, view.distance, view.yaw);
                foreach (var v in variants)
                {
                    Set(v.gi, v.cs, v.ms, v.ao, v.shadow);
                    yield return new WaitForSecondsRealtime(1.5f); // temporal AA and the bounce light settle
                    yield return Shot($"90_light_{view.name}_{v.name}");
                }
            }
            // What the sharper shadows and the bounce light each cost, over the woods (3 s each, after 1 s to settle).
            var cost = new System.Text.StringBuilder("Lighting cost over the woods, mean frame ms:");
            foreach (int shadow in new[] { 1024, 2048 })
                foreach (bool gi in new[] { false, true })
                {
                    Set(gi, true, microOn, true, shadow);
                    yield return new WaitForSecondsRealtime(1f);
                    int frames = 0;
                    float from = Time.realtimeSinceStartup;
                    while (Time.realtimeSinceStartup - from < 3f) { frames++; yield return null; }
                    cost.Append($" {shadow}{(gi ? "+bounce" : "")} {(Time.realtimeSinceStartup - from) * 1000f / frames:F2}");
                }
            log.WriteLine(cost.ToString());
            Set(bounceOn, true, microOn, true, 2048);
            Atmosphere.ApplyQuality(Camera.main);
            yield return GreyCard(game, rts);
        }

        // A flat 18%-grey matte card in the open by the base, from 60 m and the strategy camera's 300 m: its colour on
        // screen is the cast the sun, sky, fog and grade put on everything (hd159's concrete came out tan and every
        // meadow short of blue). A neutral picture keeps its R, G and B within a few levels of each other; hd161's A/B of
        // white balances and highlight tones chose -20 (Atmosphere.ConfigureProfile).
        IEnumerator GreyCard(SandboxGame game, RtsController rts)
        {
            Vector3 spot = OpenGroundNear(game.basePosition, 30f);
            var card = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(card.GetComponent<Collider>());
            card.transform.SetPositionAndRotation(Ground.OnGround(spot) + Vector3.up * 0.3f, Quaternion.Euler(90f, 0f, 0f)); // facing up
            card.transform.localScale = Vector3.one * 40f;
            var material = Art.NewMaterial(new Color(0.46f, 0.46f, 0.46f)); // sRGB 0.46 is 18% reflectance
            material.SetFloat("_Smoothness", 0f);
            card.GetComponent<MeshRenderer>().sharedMaterial = material;
            foreach (var (name, distance) in new[] { ("60m", 60f), ("300m", 300f) })
            {
                rts.LookAt(spot, distance, 30f);
                yield return new WaitForSecondsRealtime(1.5f); // exposure and temporal AA settle
                yield return new WaitForEndOfFrame();
                var screen = ScreenCapture.CaptureScreenshotAsTexture();
                Color sum = Color.black;
                for (int y = -4; y <= 4; y++)
                    for (int x = -4; x <= 4; x++) sum += screen.GetPixel(screen.width / 2 + x, screen.height / 2 + y);
                Destroy(screen);
                log.WriteLine($"Grey card {name}: sRGB {sum.r / 81f * 255f:F0} {sum.g / 81f * 255f:F0} {sum.b / 81f * 255f:F0}");
                yield return Shot($"90_greycard_{name}");
            }
            Destroy(card);
            Destroy(material);
        }

        // ChatGPT's continuous-world acceptance flight (critique 19): a Swift flies one unbroken route of up to 20 km from
        // the base through a village, the largest forest, a works, a rail junction or airfield and a dam, never
        // teleporting. The first half is at 1x and 220 m up, the second at 4x and the lowest transit height. At each stop
        // the strategy camera looks from 50 m, 300 m and 2.5 km (the Swift's feed shows in the corner). Logged: frame p95
        // and p99 at each speed, frames over 25 ms with where the Swift was, the worst streaming commit, texture memory,
        // live tiles, missing forest blocks, the nearest streamed tile still missing, and whether the Swift took every leg,
        // kept its battery reckoning and met no enemy this far from the battle.
        // Frame phases for the flight's hitch report: when the first Update starts, when the last LateUpdate ends and when
        // the render pipeline finishes the frame. hd170's 0.9-1.4 s frames near a rail junction showed nothing in the
        // streaming counters, so each spike says which part of the frame it was spent in.
        [DefaultExecutionOrder(-32000)] sealed class FrameStart : MonoBehaviour { public static float At; void Update() => At = Time.realtimeSinceStartup; }
        [DefaultExecutionOrder(32000)] sealed class FrameScripts : MonoBehaviour { public static float At; void LateUpdate() => At = Time.realtimeSinceStartup; }
        static float rendered;
        static void Rendered(UnityEngine.Rendering.ScriptableRenderContext context, List<Camera> cameras) => rendered = Time.realtimeSinceStartup;

        IEnumerator ContinuousFlight(SandboxGame game, RtsController rts, int pass)
        {
            // Candidate stops in local coordinates, one kind per stop.
            var stops = new List<(string kind, string name, Vector3 at)>();
            void Add(string kind, string name, Vector2 world)
            {
                Vector2 local = world - FarTerrain.Origin;
                if (local.magnitude < Ground.Size * 0.5f + 500f) return; // outside the sector, where the world streams
                stops.Add((kind, name, new Vector3(local.x, 0f, local.y)));
            }
            foreach (var s in WorldPlan.Settlements) Add("village", s.name, s.center);
            Add("forest", "largest forest", ForestHlod.LargestForest());
            // Any wood within 10 km, at least 400 m across (hd165's route had no room for the largest forest).
            for (float dz = -10000f; dz <= 10000f; dz += 1000f)
                for (float dx = -10000f; dx <= 10000f; dx += 1000f)
                {
                    Vector2 w = FarTerrain.Origin + new Vector2(dx, dz);
                    bool wood = WorldPlan.Inside(w.x, w.y) && WorldPlan.CoverAt(w.x, w.y) == Cover.Forest;
                    foreach (var o in new[] { new Vector2(200f, 0f), new Vector2(-200f, 0f), new Vector2(0f, 200f), new Vector2(0f, -200f) })
                        wood &= WorldPlan.Inside(w.x + o.x, w.y + o.y) && WorldPlan.CoverAt(w.x + o.x, w.y + o.y) == Cover.Forest;
                    if (wood) Add("forest", "wood", w);
                }
            foreach (var f in WorldPlan.Features) if (f.valid) Add(f.kind == WorldFeatureKind.IndustrialZone ? "works" : "rail/airfield", f.kind.ToString(), f.center);
            foreach (var d in WorldPlan.Dams) Add("dam", d.name, (d.a + d.b) * 0.5f);
            // The route with the most kinds within 20 km, then the one nearest 17 km long (ChatGPT asked for 15-20), keeping
            // every leg 1.5 km clear of the enemy HQ, or 80% of the base's own distance from it when that's closer (hd164
            // asked for 1.5 km from a base nearer than that and found no route).
            Vector3 home = Flat(game.basePosition), hq = Flat(game.enemyHqPosition);
            float clearOfHq = Mathf.Min(1500f, 0.8f * Vector3.Distance(home, hq));
            // Only the six of each kind nearest the base go into the search, which keeps it to well under a million routes.
            stops.Sort((a, b) => Vector3.Distance(home, a.at).CompareTo(Vector3.Distance(home, b.at)));
            var perKind = new Dictionary<string, int>();
            var kept = new List<(string kind, string name, Vector3 at)>();
            foreach (var s in stops)
            {
                perKind.TryGetValue(s.kind, out int n);
                if (n >= 6) continue;
                perKind[s.kind] = n + 1;
                kept.Add(s);
            }
            stops.Clear();
            stops.AddRange(kept);
            var route = new List<int>();
            var best = new List<int>();
            float bestLength = 0f;
            var used = new HashSet<string>();
            void Search(Vector3 from, float length)
            {
                if (route.Count > best.Count || (route.Count == best.Count && Mathf.Abs(length - 17000f) < Mathf.Abs(bestLength - 17000f)))
                {
                    best.Clear(); best.AddRange(route); bestLength = length;
                }
                for (int i = 0; i < stops.Count; i++)
                {
                    if (used.Contains(stops[i].kind)) continue;
                    Vector3 to = stops[i].at;
                    float step = Vector3.Distance(from, to);
                    if (length + step > 20000f) continue;
                    Vector3 ab = to - from;
                    float t = Mathf.Clamp01(Vector3.Dot(hq - from, ab) / Mathf.Max(1f, ab.sqrMagnitude));
                    if (Vector3.Distance(hq, from + ab * t) < clearOfHq) continue;
                    used.Add(stops[i].kind); route.Add(i);
                    Search(to, length + step);
                    route.RemoveAt(route.Count - 1); used.Remove(stops[i].kind);
                }
            }
            Search(home, 0f);
            var names = new System.Text.StringBuilder();
            foreach (int i in best)
            {
                Vector2 w = new Vector2(stops[i].at.x, stops[i].at.z) + FarTerrain.Origin;
                names.Append($" {stops[i].kind} ({stops[i].name}, {w.x:F0}, {w.y:F0})");
            }
            Vector2 homeWorld = new Vector2(home.x, home.z) + FarTerrain.Origin;
            log.WriteLine($"Continuous flight {pass} route from the base at ({homeWorld.x:F0}, {homeWorld.y:F0}): {best.Count} stops, {bestLength / 1000f:F1} km:{names}");
            var swift = game.SpawnDrone(DroneType.Recon);
            if (swift == null || best.Count < 3)
            {
                log.WriteLine($"Continuous flight {pass}: Swift {swift != null}, {best.Count} stops chosen of {stops.Count} candidates, HQ clearance {clearOfHq:F0} m");
                errors++;
                yield break;
            }
            float cruise = swift.altitude;
            foreach (int i in best) swift.MoveTo(stops[i].at, true);
            rts.selected.Clear();
            rts.selected.Add(swift);
            int knownBefore = 0;
            var known = new HashSet<EnemyUnit>();
            foreach (var e in game.enemies) if (e != null && e.Known) { knownBefore++; known.Add(e); }
            var contacts = new System.Text.StringBuilder();
            var legs = new System.Text.StringBuilder();
            var leg = new List<float>(8000);

            FarDetail.ResetMetrics();
            var slow = new List<float>(20000);
            var fast = new List<float>(20000);
            var spikes = new System.Text.StringBuilder();
            int spikeCount = 0, reached = 0, ownTileMissing = 0, tileSamples = 0, worstMissingBlocks = 0, gcCount = System.GC.CollectionCount(0), farContacts = 0;
            int ownMissingFast = 0, samplesFast = 0, hitches = 0, hitchesFast = 0, unreadyFrames = 0, unreadyFast = 0, framesFast = 0, flightFrames = 0, throttledFrames = 0;
            // The first second is left out: its first frame carried the route search and the Swift's launch (hd165's
            // 196 ms "spike" at the base).
            float nearestMissing = float.MaxValue, settleUntil = Time.realtimeSinceStartup + 1f, nextSample = 0f, flownBefore = swift.Flown, gameTime = 0f;
            long peakTexture = 0;
            long committedBefore = System.Diagnostics.Process.GetCurrentProcess().PrivateMemorySize64;
            var startProbe = gameObject.AddComponent<FrameStart>();
            var scriptsProbe = gameObject.AddComponent<FrameScripts>();
            UnityEngine.Rendering.RenderPipelineManager.endContextRendering += Rendered;
            float frameBegan = 0f, lateRender = 0f, lateGpu = 0f;
            int lateFrames = 0;
            bool spikeShot = false;
            var timing = new FrameTiming[1];
            float started = Time.realtimeSinceStartup;
            while (reached < best.Count && swift != null && swift.IsAlive && Time.realtimeSinceStartup - started < 900f)
            {
                bool fastHalf = reached >= (best.Count + 1) / 2;
                // 4x as the player asks for it: the game holds the clock back while the ground ahead streams in.
                Time.timeScale = fastHalf ? game.AllowedSpeed(4f) : 1f;
                if (fastHalf && Time.timeScale < 4f) throttledFrames++;
                swift.altitude = fastHalf ? cruise : 220f;
                rts.LookAt(swift.transform.position, 120f, 30f);
                yield return null;
                if (swift == null) break;
                float ms = Time.unscaledDeltaTime * 1000f;
                // The frame just finished: its scripts, its rendering, and the gap before this one began (present, GPU
                // wait, physics); this frame's LateUpdate and rendering haven't run yet.
                float scriptMs = (FrameScripts.At - frameBegan) * 1000f, renderMs = (rendered - FrameScripts.At) * 1000f, gapMs = (FrameStart.At - rendered) * 1000f;
                frameBegan = FrameStart.At;
                // The render thread's and the GPU's own times arrive a few frames late: a spike's entry is finished with
                // the largest of them over the four frames after it.
                FrameTimingManager.CaptureFrameTimings();
                if (FrameTimingManager.GetLatestTimings(1, timing) > 0)
                {
                    lateRender = Mathf.Max(lateRender * (lateFrames > 0 ? 1f : 0f), (float)timing[0].cpuRenderThreadFrameTime);
                    lateGpu = Mathf.Max(lateGpu * (lateFrames > 0 ? 1f : 0f), (float)timing[0].gpuFrameTime);
                }
                if (lateFrames > 0 && --lateFrames == 0) { spikes.Append($" (render thread {lateRender:F0}, GPU {lateGpu:F0} ms);"); lateRender = lateGpu = 0f; }
                gameTime += Time.deltaTime;
                Vector2 world = new Vector2(swift.transform.position.x, swift.transform.position.z) + FarTerrain.Origin;
                if (Time.realtimeSinceStartup > settleUntil)
                {
                    (fastHalf ? fast : slow).Add(ms);
                    leg.Add(ms);
                    if (ms > 33.3f) { hitches++; if (fastHalf) hitchesFast++; }
                    // Every frame: is every streamed tile within 500 m of the followed Swift live? (ChatGPT asks for
                    // 99.9% ready-time, measured per frame.)
                    int tx = Mathf.FloorToInt(world.x / 512f), tz = Mathf.FloorToInt(world.y / 512f);
                    bool ready = true;
                    for (int z = tz - 1; z <= tz + 1 && ready; z++)
                        for (int x = tx - 1; x <= tx + 1 && ready; x++)
                            if (FarDetail.Eligible(x, z) && FarDetail.DistanceTo(x, z, world) < 500f &&
                                !FarDetail.TryHeight(new Vector2((x + 0.5f) * 512f, (z + 0.5f) * 512f) - FarTerrain.Origin, out _)) ready = false;
                    if (!ready) { unreadyFrames++; if (fastHalf) unreadyFast++; }
                    if (fastHalf) framesFast++;
                    flightFrames++;
                    // With what the far world spent on the main thread that frame and whether the garbage collector ran.
                    int gc = System.GC.CollectionCount(0);
                    if (ms > 25f && spikeCount < 16 && lateFrames == 0) { lateFrames = 4; lateRender = lateGpu = 0f; }
                    if (ms > 25f && spikeCount++ < 16)
                        spikes.Append($" {ms:F0} ms at ({world.x:F0}, {world.y:F0}) {(fastHalf ? "4x" : "1x")} [update {scriptMs:F0}, render {renderMs:F0}, gap {gapMs:F0}] scatter {FarScatter.MainThreadMs:F1}/places {FarScatter.PlacesMs:F1}, " +
                                      $"tiles {FarDetail.LastFrameMs:F1}, canopy {ForestHlod.LastFrameMs:F1} ms, uploads {(FarDetail.UploadBytes + ForestHlod.UploadBytes) / 1024} KB{(gc != gcCount ? " GC" : "")};");
                    gcCount = gc;
                    // The first frame over 200 ms in each pass: what the drone feed and the main view were looking at (hd175's
                    // 1.5 s frames were GPU time, only with the feed camera on). Those frames' I/O is left out of the timing.
                    if (ms > 200f && !spikeShot)
                    {
                        spikeShot = true;
                        var picture = DroneFeed.Instance != null ? DroneFeed.Instance.Picture : null;
                        if (picture != null)
                        {
                            var previous = RenderTexture.active;
                            RenderTexture.active = picture;
                            var feedShot = new Texture2D(picture.width, picture.height, TextureFormat.RGB24, false);
                            feedShot.ReadPixels(new Rect(0, 0, picture.width, picture.height), 0, 0);
                            RenderTexture.active = previous;
                            File.WriteAllBytes(Path.Combine(outDir, $"92_spike_{pass}_feed.png"), feedShot.EncodeToPNG());
                            Destroy(feedShot);
                        }
                        log.WriteLine($"Continuous flight {pass}: {ms:F0} ms frame at ({world.x:F0}, {world.y:F0}), Swift {swift.transform.position.y - Ground.HeightAt(swift.transform.position):F0} m up; " +
                                      $"feed at ({DroneFeed.Instance?.FeedCamera?.transform.position}), looking {DroneFeed.Instance?.FeedCamera?.transform.forward}");
                        yield return Shot($"92_spike_{pass}_main");
                        settleUntil = Time.realtimeSinceStartup + 1f;
                    }
                }
                peakTexture = System.Math.Max(peakTexture, (long)Texture.currentTextureMemory);
                worstMissingBlocks = Mathf.Max(worstMissingBlocks, ForestHlod.MissingBlocks);
                // Once a second: the nearest streamed tile round the Swift that should be live and isn't.
                if (Time.realtimeSinceStartup > nextSample)
                {
                    nextSample = Time.realtimeSinceStartup + 1f;
                    tileSamples++;
                    // Any enemy newly known, with where it stands and how far it was from the Swift.
                    // Only one outside the battle area counts against the flight: the Swift passes the sector's own units on
                    // its way out (hd167 spotted a tank 300 m off, inside the sector).
                    foreach (var e in game.enemies)
                        if (e != null && e.Known && known.Add(e))
                        {
                            Vector2 at = new Vector2(e.transform.position.x, e.transform.position.z) + FarTerrain.Origin;
                            bool far = Mathf.Max(Mathf.Abs(e.transform.position.x), Mathf.Abs(e.transform.position.z)) > Ground.Size * 0.5f;
                            if (far) farContacts++;
                            contacts.Append($" {e.type} at ({at.x:F0}, {at.y:F0}){(far ? " OUTSIDE the sector" : "")}, {Vector3.Distance(e.transform.position, swift.transform.position):F0} m from the Swift;");
                        }
                    // Tiles are judged in flight only: a stop's 2.5 km look moves the streaming away from the Swift.
                    int cx = Mathf.FloorToInt(world.x / 512f), cz = Mathf.FloorToInt(world.y / 512f);
                    if (Time.realtimeSinceStartup > settleUntil)
                    {
                        if (fastHalf) samplesFast++;
                        for (int z = cz - 2; z <= cz + 2; z++)
                            for (int x = cx - 2; x <= cx + 2; x++)
                            {
                                if (!FarDetail.Eligible(x, z)) continue;
                                Vector2 middle = new Vector2((x + 0.5f) * 512f, (z + 0.5f) * 512f) - FarTerrain.Origin;
                                if (FarDetail.TryHeight(middle, out _)) continue;
                                nearestMissing = Mathf.Min(nearestMissing, FarDetail.DistanceTo(x, z, world));
                                if (x == cx && z == cz) { ownTileMissing++; if (fastHalf) ownMissingFast++; }
                            }
                    }
                }
                // A stop: everything paused while the camera looks from 50 m, 300 m and 2.5 km.
                Vector3 stop = stops[best[reached]].at;
                if (Vector3.Distance(Flat(swift.transform.position), stop) < 90f)
                {
                    string label = $"91_flight_{reached}_{stops[best[reached]].kind.Replace('/', '_')}";
                    legs.Append($" to {stops[best[reached]].kind} {(fastHalf ? "4x" : "1x")} p95 {P(leg, 0.95f):F2} ms ({leg.Count});");
                    leg.Clear();
                    if (pass > 1) { reached++; continue; } // the warmed second run flies straight on
                    Time.timeScale = 0f;
                    foreach (var (name, distance) in new[] { ("50m", 50f), ("300m", 300f), ("2500m", 2500f) })
                    {
                        rts.LookAt(swift.transform.position, distance, 30f);
                        yield return new WaitForSecondsRealtime(distance > 1000f ? 2f : 1f);
                        yield return Shot($"{label}_{name}");
                    }
                    reached++;
                    settleUntil = Time.realtimeSinceStartup + 2f; // the camera's jumps reload the view; not flight frames
                }
            }
            Time.timeScale = 1f;
            UnityEngine.Rendering.RenderPipelineManager.endContextRendering -= Rendered;
            Destroy(startProbe);
            Destroy(scriptsProbe);
            float realSeconds = Time.realtimeSinceStartup - started;
            if (swift != null) swift.altitude = cruise;
            int knownAfter = 0;
            foreach (var e in game.enemies) if (e != null && e.Known) knownAfter++;
            float P(List<float> frames, float share)
            {
                if (frames.Count == 0) return 0f;
                frames.Sort();
                return frames[Mathf.Min(frames.Count - 1, Mathf.CeilToInt(frames.Count * share) - 1)];
            }
            float slow95 = P(slow, 0.95f), slow99 = P(slow, 0.99f), fast95 = P(fast, 0.95f), fast99 = P(fast, 0.99f);
            float battery = swift != null ? swift.Flown - flownBefore - gameTime : 0f; // flying time charged against flying time
            log.WriteLine($"Continuous flight {pass}: reached {reached}/{best.Count} stops in {realSeconds:F0} s, Swift alive {swift != null && swift.IsAlive}, " +
                          $"battery {(swift != null ? swift.Battery * 100f : 0f):F0}% (charge drift {battery:F1} s), queued left {(swift != null ? swift.Waypoints.Count : 0)}, " +
                          $"new contacts {knownAfter - knownBefore} ({farContacts} outside the sector):{contacts}");
            float slow999 = P(slow, 0.999f), fast999 = P(fast, 0.999f);
            float readyShare = 1f - unreadyFrames / (float)Mathf.Max(1, flightFrames), readyFast = 1f - unreadyFast / (float)Mathf.Max(1, framesFast);
            log.WriteLine($"Continuous flight {pass} frames: 1x p95 {slow95:F2} p99 {slow99:F2} p99.9 {slow999:F2} ms ({slow.Count}), 4x p95 {fast95:F2} p99 {fast99:F2} p99.9 {fast999:F2} ms ({fast.Count}); " +
                          $"legs:{legs} over 25 ms {spikeCount}:{spikes}");
            // ChatGPT's release gate (critique 20): no frame over 33.3 ms after warm-up and at least 99.9% of frames with
            // every tile within 500 m of the Swift live.
            bool gate = hitches == 0 && readyShare >= 0.999f;
            log.WriteLine($"Continuous flight {pass} gate: frames over 33.3 ms {hitches} (4x {hitchesFast}), near tiles ready {readyShare * 100f:F2}% of frames " +
                          $"(4x {readyFast * 100f:F2}%), 4x held back {throttledFrames}/{framesFast} frames -> {(gate ? "PASS" : "FAIL")}");
            if (!gate && pass <= 2) errors++;
            log.WriteLine($"Continuous flight {pass} streaming: worst commit {FarDetail.WorstCommitMs:F3} ms, over 2 ms {FarDetail.BudgetOverruns}, live {FarDetail.Live}/{FarDetail.MaxTiles}, " +
                          $"nearest missing tile {(nearestMissing == float.MaxValue ? "none" : nearestMissing.ToString("F0") + " m")}, own tile missing {ownTileMissing}/{tileSamples} (4x {ownMissingFast}/{samplesFast}), " +
                          $"worst missing canopy blocks {worstMissingBlocks}, peak texture memory {peakTexture / 1048576} MB, " +
                          $"committed {committedBefore / 1048576} -> {System.Diagnostics.Process.GetCurrentProcess().PrivateMemorySize64 / 1048576} MB, {Screen.width}x{Screen.height}");
            if (reached < best.Count || swift == null || !swift.IsAlive || Mathf.Max(slow95, fast95) > 16.7f || FarDetail.WorstCommitMs > 2.0 ||
                farContacts > 0 || Mathf.Abs(battery) > 2f) { if (pass <= 2) errors++; }
        }

        // One permanent landmark of each kind as the world draws it outside a sector (streamed ground, far masses), from
        // the strategy camera's 300 m and from 2.5 km, so its look can be judged and matched to the sector's own detail.
        IEnumerator LandmarkViews(RtsController rts)
        {
            var seen = new HashSet<WorldFeatureKind>();
            foreach (var f in WorldPlan.Features)
            {
                if (!f.valid || !seen.Add(f.kind)) continue;
                Vector2 p = f.center - FarTerrain.Origin;
                foreach (var (name, distance) in new[] { ("300m", 300f), ("2500m", 2500f) })
                {
                    rts.LookAt(new Vector3(p.x, 0f, p.y), distance, 35f);
                    yield return new WaitForSecondsRealtime(3f); // streamed tiles round the new view
                    yield return ForestMassesSettle($"89_landmark_{f.kind}_{name}");
                    yield return Shot($"89_landmark_{f.kind}_{name}");
                }
            }
            log.WriteLine($"Landmark views: {seen.Count} kinds, tiles live {FarDetail.Live}, landmark draws {LandmarkHlod.DrawCalls}, triangles {LandmarkHlod.TrianglesDrawn}");
        }

        // The far forest's canopy for the view: hd144's forest shot caught 3 blocks after 6 s and showed bare ground for
        // forest. Coverage must be complete (no missing block) within 3 s of a 2.5 km jump; waits up to 15 s to see how
        // long it really takes, and says so either way.
        // Game time stands still meanwhile, so a mission doesn't play on, pop a tip or leave photo mode while it waits
        // (hd149's river valley shot showed the HUD at dusk).
        IEnumerator ForestMassesSettle(string shot)
        {
            float forestWaitStarted = Time.unscaledTime, timeScale = Time.timeScale;
            Time.timeScale = 0f;
            yield return null;
            while (ForestHlod.MissingBlocks > 0 && Time.unscaledTime - forestWaitStarted < 15f) yield return null;
            Time.timeScale = timeScale;
            float took = Time.unscaledTime - forestWaitStarted;
            log.WriteLine($"{shot}: forest masses {(ForestHlod.MissingBlocks == 0 ? "settled" : "timed out")} at {ForestHlod.BlocksDrawn} blocks after {took:F2} s; " +
                          $"missing {ForestHlod.MissingBlocks}, coarse {ForestHlod.CoarseBlocks}, cached {ForestHlod.CachedBlocks}, builds {ForestHlod.CompletedBuilds}, " +
                          $"refining {ForestHlod.Building}, last build slice {ForestHlod.BuildMs:F3} ms{(took > 3f ? " (over the 3 s target)" : "")}");
        }

        // What of the woods the last camera drew, and what the forest cost (Codex's counters).
        static string ForestLine() =>
            $"forest drawn: mesh trees {FarScatter.MeshTreesDrawn}, cards {FarScatter.CardTreesDrawn}, canopy blocks {ForestHlod.BlocksDrawn} of {ForestHlod.CachedBlocks} cached, " +
            $"{SectorForest.Stems.Count} sector trees; ready {SectorForest.Ready}, cells {FarScatter.ReadyCells}, tree reach {FarScatter.TierDistances.z:F0} m, " +
            $"missing canopy {ForestHlod.MissingBlocks}, coarse {ForestHlod.CoarseBlocks}, visited {FarScatter.RecordsVisited}, tree triangles {FarScatter.TreeTrianglesDrawn}, " +
            $"canopy triangles {ForestHlod.TrianglesDrawn}, forest CPU {FarScatter.MainThreadMs:F3} ms (mean {FarScatter.MeanMs:F3}, worst {FarScatter.WorstMs:F3}, " +
            $"{FarScatter.Samples} frames), crown CPU {Canopy.QueryMs:F3} ms, fallback materials {ForestMaterials.FallbackCount}";

        // The largest kinds of texture in memory, by name with digits dropped, estimated at their size and format with
        // mipmaps: which of them grow from sector to sector (hd179 gained 45 MB of textures per sector).
        static string TextureTally()
        {
            var bytes = new Dictionary<string, long>();
            var counts = new Dictionary<string, int>();
            foreach (var t in Resources.FindObjectsOfTypeAll<Texture>())
            {
                if (t == null) continue;
                int depth = t is Texture2DArray array ? array.depth : t is Cubemap ? 6 : t is Texture3D volume ? volume.depth : 1;
                long texels = (long)t.width * t.height * depth;
                float perTexel = t.graphicsFormat.ToString().Contains("BC1") || t.graphicsFormat.ToString().Contains("DXT1") ? 0.5f
                    : t.graphicsFormat.ToString().Contains("BC") || t.graphicsFormat.ToString().Contains("DXT") ? 1f : 4f;
                long size = (long)(texels * perTexel * (t.mipmapCount > 1 ? 1.33f : 1f));
                var key = new string(System.Array.FindAll((t.GetType().Name + ":" + t.name).ToCharArray(), c => !char.IsDigit(c)));
                bytes.TryGetValue(key, out long b); bytes[key] = b + size;
                counts.TryGetValue(key, out int c2); counts[key] = c2 + 1;
            }
            var keys = new List<string>(bytes.Keys);
            keys.Sort((a, b) => bytes[b].CompareTo(bytes[a]));
            var text = new System.Text.StringBuilder();
            for (int i = 0; i < Mathf.Min(14, keys.Count); i++) text.Append($" {keys[i]} x{counts[keys[i]]} {bytes[keys[i]] / 1048576} MB;");
            return text.ToString();
        }

        // Vertices held by every mesh in memory, for the sector-change memory check (hd179 grew 2.8 GB over the tour).
        static long MeshVertices()
        {
            long total = 0;
            foreach (var mesh in Resources.FindObjectsOfTypeAll<Mesh>()) total += mesh.vertexCount;
            return total;
        }

        IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            var texture = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(Path.Combine(outDir, name + ".png"), texture.EncodeToPNG());
            Destroy(texture);
            // Memory as we go, to catch a leak: texture memory, how many textures, render textures, materials and meshes
            // exist, and the process's committed memory, which holds D3D12's system-memory (non-local) heaps: hd173 died when
            // 6,400 one-megabyte scratch buffers (6.4 GB) filled that budget.
            long committed = System.Diagnostics.Process.GetCurrentProcess().PrivateMemorySize64;
            log.WriteLine($"[{Time.time:F1}] screenshot {name}   tex {Texture.currentTextureMemory / 1048576} MB, " +
                          $"{Resources.FindObjectsOfTypeAll<Texture2D>().Length} tex2d, {Resources.FindObjectsOfTypeAll<RenderTexture>().Length} rt, " +
                          $"{Resources.FindObjectsOfTypeAll<Material>().Length} mat, {Resources.FindObjectsOfTypeAll<Mesh>().Length} mesh, committed {committed / 1048576} MB, " +
                          $"managed {System.GC.GetTotalMemory(false) / 1048576} MB, {Resources.FindObjectsOfTypeAll<Texture2DArray>().Length} arrays, " +
                          $"{Resources.FindObjectsOfTypeAll<Cubemap>().Length} cubes, {MeshVertices() / 1000}k vertices, {Resources.FindObjectsOfTypeAll<GameObject>().Length} objects");
        }
    }
}
