"""Generates the game's sound effects with the ElevenLabs sound API and prepares them for Unity.

Reads the API key from C:/Users/ali10/.elevenlabs-key (never printed). Raw MP3s go to ./raw, the finished
WAVs (trimmed, mono for placed sounds, loudness-normalised with a plain gain so loops stay seamless) to the
game's Resources/Sounds, named so Sound.cs picks them up ("explosion 1" is a variant of "explosion").
Usage: python make_sfx.py [name-filter]   (already generated raw files are reused, not paid for twice)
"""
import json
import os
import re
import subprocess
import sys
import urllib.request

RAW = os.path.join(os.environ.get("TEMP", "."), "dwprev", "sfx", "raw")  # paid-for originals, kept out of the repo
OUT = r"C:\Users\ali10\Documents\DroneWarfare\Assets\DroneSandbox\Resources\Sounds"
KEY_FILE = os.path.join(os.path.expanduser("~"), ".elevenlabs-key")
FFMPEG = r"C:\Users\ali10\ffmpeg-2025-06-04-git-a4c1a5b084-essentials_build\ffmpeg-2025-06-04-git-a4c1a5b084-essentials_build\bin\ffmpeg.exe"
NV = " No music, no voices."

# name, prompt, seconds, loop, influence, loudness target (LUFS-I), true-peak ceiling, stereo, trim (seconds or None)
SOUNDS = [
    ("rain", "Broad, steady rainfall across open farmland and leafy treetops, heard from 80 metres above. Soft diffuse hiss, distant leaf patter, no distinct close droplets. Constant intensity." + NV, 25, True, 0.65, -27, -3, True, None),
    ("thunder 1", "A distant thunderstorm rolls across open countryside. Deep, soft low-frequency rumble, slowly swelling and fading, no sharp initial crack. Natural outdoor distance." + NV, 8, False, 0.75, -21, -2, True, None),
    ("thunder 2", "One sudden, sharp nearby thunder crack, followed by a powerful low rolling rumble spreading across open fields. Natural decay, no artificial cinematic bass." + NV, 7, False, 0.8, -17, -1, True, None),
    ("thunder 3", "Long, irregular rolling thunder across distant countryside. Several overlapping low rumbles swell and fade gradually, no distinct close lightning crack." + NV, 12, False, 0.7, -21, -2, True, None),
    ("explosion 1", "A light military truck struck by a small explosive projectile outdoors. Sharp metallic impact, brief compact explosion, glass breaking and light debris scattering onto dirt." + NV, 2, False, 0.85, -19, -1, False, None),
    ("explosion 2", "A parked light vehicle explodes in an open field. Short punchy blast, metal body panels tearing, small fragments falling, brief dusty decay. Realistic scale." + NV, 2.5, False, 0.8, -19, -1, False, None),
    ("explosion 3", "A small armoured vehicle takes a direct explosive hit outdoors. Heavy metallic thump, compact pressure blast, rattling steel fragments and debris settling." + NV, 3, False, 0.85, -18, -1, False, None),
    ("big explosion 1", "A fuel tanker catches fire and erupts into a large outdoor explosion. Deep initial blast, roaring flame, rupturing metal and two smaller delayed fuel bursts. Long decay." + NV, 6, False, 0.85, -17, -1, False, None),
    ("big explosion 2", "An ammunition storage vehicle detonates outdoors. One powerful initial explosion followed by several irregular smaller sharp detonations, metallic debris and fading smoke roar." + NV, 7, False, 0.85, -17, -1, False, None),
    ("gunshot 1", "One isolated small-calibre automatic weapon shot from a compact drone outdoors. Very short dry mechanical crack, tight metallic snap, almost no reverb or tail. No burst." + NV, 0.5, False, 0.9, -22, -2, False, 0.14),
    ("gunshot 2", "Single small-calibre gunshot, sharp dry pop with a slightly deeper mechanical body. Close, controlled transient, minimal outdoor echo, no burst or additional shots." + NV, 0.5, False, 0.9, -22, -2, False, 0.14),
    ("gunshot 3", "One light automatic weapon shot, crisp metallic report, short muzzle blast, dry mechanical tail. Compact and punchy, no sustained firing, no long reverberation." + NV, 0.5, False, 0.9, -22, -2, False, 0.14),
    ("cannon 1", "One isolated 23 mm-class anti-aircraft autocannon shot outdoors. Heavy, hard metallic cannon crack with a deep pressure thump and very short open-air decay. No burst." + NV, 0.7, False, 0.9, -19, -1, False, 0.6),
    ("cannon 2", "Single medium-calibre anti-aircraft cannon firing across an open field. Sharp concussive blast, solid mechanical clank, deep punch and short distant reflection. No burst." + NV, 0.8, False, 0.9, -19, -1, False, 0.7),
    ("missile", "A small missile launches from a flying drone. Sudden ignition snap followed by a short forceful rocket exhaust whoosh that quickly moves away and fades in open air. No explosion." + NV, 1.5, False, 0.85, -21, -2, False, None),
    ("whistle", "A heavy unguided bomb falling through open air. Thin descending aerodynamic whistle grows louder and lower over two seconds, with rushing air. Ends before impact. No explosion." + NV, 2.5, False, 0.85, -25, -3, False, None),
    ("ricochet 1", "One small projectile strikes a hard metal plate, producing a sharp metallic ping and brief high-pitched ricochet whine that disappears into open air." + NV, 0.7, False, 0.85, -25, -3, False, None),
    ("ricochet 2", "One bullet glances off thick steel at an angle. Dry metallic impact followed by a short descending spinning zing, outdoors, no repeated impacts." + NV, 0.8, False, 0.85, -25, -3, False, None),
    ("ricochet 3", "One projectile skips off hard stone beside a road. Sharp stone chip, gritty fragments and a brief thin whistling ricochet fading away." + NV, 0.8, False, 0.85, -25, -3, False, None),
    ("drone down", "A small quadcopter is struck and breaks apart in flight. Carbon-fibre arms snapping, brittle plastic cracking, electric motors whining down unevenly, followed by debris hitting the ground." + NV, 4, False, 0.85, -20, -1, False, None),
    ("motor Scout", "Continuous sound of a lightweight electric quadcopter in steady flight. Four small high-pitched propellers, thin electric whine, rapid smooth rotor buzz with slight natural vibration." + NV, 12, True, 0.8, -27, -3, False, None),
    ("motor Strike", "Continuous medium-sized quadcopter flight. Four stronger electric motors, mid-pitched layered propeller hum, stable rotor wash and subtle mechanical vibration." + NV, 12, True, 0.8, -26, -3, False, None),
    ("motor Kamikaze", "Continuous fast quadcopter motor sound. High-RPM electric whine, aggressive tight propeller buzz, slight vibration, constant elevated thrust without acceleration or deceleration." + NV, 10, True, 0.85, -26, -3, False, None),
    ("motor Bomber", "Large heavy hexacopter hovering in steady flight. Six powerful electric rotors creating a deep layered propeller drone, broad low-frequency air wash and steady mechanical hum." + NV, 12, True, 0.8, -27, -3, False, None),
    ("wind", "Gentle countryside wind passing over open farmland, grasses and distant tree canopies. Soft broad moving air, faint leaves rustling, natural steady atmosphere with no strong gusts." + NV, 25, True, 0.65, -31, -3, True, None),
    ("birds", "Sparse European farmland birds heard across open fields in daylight. Occasional distant skylark, blackbird and small songbird calls, natural spacing with long quiet intervals." + NV, 30, True, 0.7, -31, -3, True, None),
    ("crickets", "Calm rural European night atmosphere. Sparse crickets and grass insects chirping softly from different distances, light natural pauses, no constant loud buzzing." + NV, 25, True, 0.7, -32, -3, True, None),
]


def generate(key, name, prompt, seconds, loop, influence):
    path = os.path.join(RAW, name + ".mp3")
    if os.path.exists(path) and os.path.getsize(path) > 1000:
        return path, False
    body = {"text": prompt, "duration_seconds": max(0.5, seconds), "prompt_influence": influence, "loop": loop,
            # Loops on v2 (seamless looping works there); one-shots on v3, which follows multi-part prompts better.
            "model_id": "eleven_text_to_sound_v2" if loop else "eleven_text_to_sound_v3"}
    req = urllib.request.Request("https://api.elevenlabs.io/v1/sound-generation?output_format=mp3_44100_192",
                                 data=json.dumps(body).encode(), method="POST",
                                 headers={"xi-api-key": key, "Content-Type": "application/json"})
    with urllib.request.urlopen(req, timeout=180) as r:
        data = r.read()
    with open(path, "wb") as f:
        f.write(data)
    return path, True


def loudness(path, chain):
    """Integrated loudness and true peak of the file after the filter chain (ffmpeg loudnorm's measurement)."""
    af = (chain + "," if chain else "") + "loudnorm=print_format=json"
    p = subprocess.run([FFMPEG, "-hide_banner", "-nostats", "-i", path, "-af", af, "-f", "null", "-"],
                       capture_output=True, text=True)
    m = re.search(r"\{[^{}]*\"input_i\"[^{}]*\}", p.stderr, re.S)
    j = json.loads(m.group(0))
    return float(j["input_i"]), float(j["input_tp"])


def finish(raw, name, lufs, peak, stereo, trim):
    chain = []
    if trim:
        # A single crack: drop any lead-in silence, keep the first `trim` seconds and fade the end.
        chain.append("silenceremove=start_periods=1:start_threshold=-45dB")
        chain.append(f"atrim=0:{trim}")
        chain.append(f"afade=t=out:st={trim - 0.015:.3f}:d=0.015")
    if not stereo:
        chain.append("pan=mono|c0=0.5*c0+0.5*c1")
    pre = ",".join(chain)
    i, tp = loudness(raw, pre)
    gain = min(lufs - i, peak - tp)  # a plain gain (loops stay seamless), never past the peak ceiling
    af = (pre + "," if pre else "") + f"volume={gain:.2f}dB"
    out = os.path.join(OUT, name + ".wav")
    subprocess.run([FFMPEG, "-hide_banner", "-loglevel", "error", "-y", "-i", raw, "-af", af,
                    "-ar", "44100", "-c:a", "pcm_s16le", out], check=True)
    return i, tp, gain


def main():
    only = sys.argv[1] if len(sys.argv) > 1 else ""
    os.makedirs(RAW, exist_ok=True)
    os.makedirs(OUT, exist_ok=True)
    with open(KEY_FILE, encoding="utf-8") as f:
        key = f.read().strip()
    for name, prompt, seconds, loop, influence, lufs, peak, stereo, trim in SOUNDS:
        if only and only not in name:
            continue
        try:
            raw, paid = generate(key, name, prompt, seconds, loop, influence)
            i, tp, gain = finish(raw, name, lufs, peak, stereo, trim)
            print(f"{name:16s} {'new' if paid else 'kept'}  in {i:6.1f} LUFS / {tp:5.1f} dBTP  gain {gain:+5.1f} dB")
        except urllib.error.HTTPError as e:
            print(f"{name:16s} FAILED {e.code}: {e.read()[:200]!r}")
        except Exception as e:
            print(f"{name:16s} FAILED {type(e).__name__}: {e}")


if __name__ == "__main__":
    main()
