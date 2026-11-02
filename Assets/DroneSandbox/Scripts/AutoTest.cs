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
            yield return new WaitForSeconds(1.5f);
            var game = SandboxGame.Instance;
            var rts = game.GetComponent<RtsController>();
            log.WriteLine($"Started. Drones: {game.drones.Count}, enemies: {game.enemies.Count}, screen {Screen.width}x{Screen.height}");

            Shot("01_start");

            // Close-up of the drones at the base, to check the models.
            rts.LookAt(game.basePosition, 18f, 30f);
            yield return new WaitForSeconds(2.5f);
            Shot("02_drones_closeup");

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
            Shot("03_moving");

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
            Shot("04_engaging");
            yield return new WaitForSeconds(4f);
            Shot("05_battle");
            if (tank != null) rts.LookAt(tank.transform.position, 35f, 45f);
            yield return new WaitForSeconds(4f);
            Shot("06_battle_later");

            // Wide view of the whole map.
            rts.LookAt(new Vector3(10f, 0f, 10f), 110f, 45f);
            yield return new WaitForSeconds(1f);
            Shot("07_overview");

            log.WriteLine($"Done at {Time.time:F1}s. Destroyed: {game.enemiesDestroyed}, lost: {game.dronesLost}, " +
                          $"expended: {game.dronesExpended}, drones left: {game.drones.Count}, errors: {errors}, " +
                          $"fps: {1f / Mathf.Max(Time.smoothDeltaTime, 0.0001f):F0}");
            yield return new WaitForSeconds(0.5f);
            Application.Quit();
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

        void Shot(string name)
        {
            ScreenCapture.CaptureScreenshot(Path.Combine(outDir, name + ".png"));
            log.WriteLine($"[{Time.time:F1}] screenshot {name}");
        }
    }
}
