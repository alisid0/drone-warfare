using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace DroneSandbox
{
    // Scripted smoke test. Start the game with  -autotest <folder>  and it plays a short battle by itself,
    // saves screenshots plus a log of errors and stats into that folder, then quits.
    // Without the flag this does nothing.
    public class AutoTest : MonoBehaviour
    {
        string outDir;
        StreamWriter log;
        int errors;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Launch()
        {
            string[] args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-autotest");
            if (i < 0 || i + 1 >= args.Length) return;
            new GameObject("Auto Test").AddComponent<AutoTest>().Begin(args[i + 1]);
        }

        void Begin(string folder)
        {
            SandboxGame.SaveProgress = false; // test runs don't touch the player's stars, points or purchases
            Tech.UseTestWallet();
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
            yield return Shot("00_menu");
            game.StartSandbox();
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

            // Send everyone toward the forward AA post and follow them.
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

            // Wide view of the whole map.
            rts.LookAt(new Vector3(10f, 0f, 10f), 110f, 45f);
            yield return new WaitForSeconds(1f);
            yield return Shot("07_overview");

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
            yield return EmptyAndRearm(game, rts);
            yield return PlayMissions(game, rts);

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
            photo.Exit();
            dayNight.SetTime(10f, 1f);
            log.WriteLine($"Photo mode: {shots.Length} shots, dam at {dam}, lake level {lake}, river at the dam {river:F1}");
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
            var targets = game.enemies.FindAll(e => e.isTarget);
            foreach (var target in targets)
            {
                var drone = game.SpawnDrone(DroneType.Kamikaze);
                if (drone != null) drone.Attack(target);
            }
            rts.LookAt(targets.Count > 0 ? targets[0].transform.position : game.basePosition, 60f, 70f);
            for (float t = 0f; t < 30f && game.Result == MissionResult.None; t += 0.5f)
                yield return new WaitForSecondsRealtime(0.5f);
            log.WriteLine($"Mission 1: {game.Result}, {game.ResultReason} Stars {game.Stars}, drones used {game.DronesUsed}, " +
                          $"targets {game.TargetsDestroyed}/{game.TargetsTotal}, time {game.MissionTime:F1}s, " +
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
            log.WriteLine($"[{Time.time:F1}] screenshot {name}");
        }
    }
}
