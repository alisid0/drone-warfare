using System;
using System.Reflection;
using System.Threading.Tasks;
using DroneSandbox;
using UnityEngine;

// Drive the real regional transaction stages using managed engine wrappers; these are lifecycle checks, not frame timings.
static class RegionalStreaming
{
    static readonly BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    static readonly FieldInfo Worker = typeof(FarDetailRegional).GetField("worker", Private);
    static readonly FieldInfo Pages = typeof(FarDetailRegional).GetField("pages", Private);
    static readonly FieldInfo Ready = typeof(FarDetailRegional).GetField("ready", Private);
    static readonly FieldInfo Stage = typeof(FarDetailRegional).GetField("stage", Private);

    // Fill the cache, move across both axes, teleport while uploading, then reset with a task in flight.
    public static object Run(FarDetailData plan)
    {
        var cache = new FarDetailRegional(plan); int frames = 0, blockedFrames = 0;
        void Frame(Vector2 eye, bool commit)
        {
            var task = Worker.GetValue(cache) as Task; task?.GetAwaiter().GetResult();
            int before = cache.Committed, stage = (int)Stage.GetValue(cache);
            Time.unscaledTime += 1f / 60f; cache.Tick(eye, commit); frames++;
            if (!commit && (cache.Committed != before || (int)Stage.GetValue(cache) != stage)) throw new Exception("Regional upload ran during a tile stage");
            if (cache.Committed - before > 1) throw new Exception("Publication burst");
            if (!commit) blockedFrames++;
            if (cache.Committed != before && stage != 7) throw new Exception("Page published before all three GPU copies");
        }
        void Fill(Vector2 eye)
        {
            int cx = (int)Math.Floor(eye.x / 2048f), cz = (int)Math.Floor(eye.y / 2048f);
            for (int frame = 0; frame < 1800; frame++)
            {
                Frame(eye, frame % 3 != 0);
                var table = (Vector4[])Pages.GetValue(cache); bool full = true;
                for (int z = cz - 4; z <= cz + 4; z++) for (int x = cx - 4; x <= cx + 4; x++)
                {
                    if (!FarDetailRegional.Wanted(x, z, eye)) continue;
                    var page = table[FarDetailRegional.Index(x, z)]; if (page.w == 0 || page.x != x || page.y != z) full = false;
                }
                if (full) return;
            }
            throw new Exception("Regional refill stalled");
        }
        Fill(new Vector2(-12000, 16000)); int first = cache.Committed;
        Fill(new Vector2(-9900, 18100)); int moved = cache.Committed - first;
        var far = new Vector2(13000, -13000);
        for (int i = 0; i < 6; i++) Frame(far, true);
        // Return before a stale transaction can publish; overwritten pages must refill rather than claim stale ownership.
        Fill(new Vector2(-12000, 16000));
        cache.Tick(far, true); var inFlight = Worker.GetValue(cache) as Task;
        cache.Clear(); inFlight?.GetAwaiter().GetResult(); int stopped = cache.Committed;
        for (int i = 0; i < 20; i++) cache.Tick(far, true);
        if (cache.Committed != stopped || Ready.GetValue(cache) != null) throw new Exception("Cleared cache published stale work");
        return new { frames, blockedFrames, initialPages = first, newPagesAfterDiagonalMove = moved, stalePublication = false,
            note = "Real state machine, synchronized workers, managed engine storage. Does not measure native upload, main-thread or GPU time." };
    }
}
