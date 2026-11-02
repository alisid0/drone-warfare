using System;
using System.Collections;
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
            SandboxGame.SaveProgress = false; // test runs don't touch the player's stars
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

            yield return PlayMissions(game, rts);

            log.WriteLine($"Done at {Time.realtimeSinceStartup:F1}s real time. Errors: {errors}");
            yield return new WaitForSecondsRealtime(0.5f);
            Application.Quit();
        }

        // Plays mission 1 to the end with Kamikaze drones, checks spotting in Hidden Guns, and looks at the
        // Command Post target. Missions pause the game behind their panels, so waits here use real time.
        IEnumerator PlayMissions(SandboxGame game, RtsController rts)
        {
            game.StartMission(0);
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Shot("08_briefing");
            game.CloseBriefing();
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
                          $"targets {game.TargetsDestroyed}/{game.TargetsTotal}, time {game.MissionTime:F1}s");
            yield return new WaitForSecondsRealtime(2f);
            yield return Shot("09_mission_complete");

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
