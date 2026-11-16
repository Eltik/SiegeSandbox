#!/usr/bin/env python3
"""
Builds the recorded sound clips the game uses from the raw downloaded recordings.

    python3 Tools/build_sounds.py        (needs numpy and ffmpeg)

Reads   AudioSources/           raw packs, not committed (see Resources/Sfx/SOURCES.txt)
Writes  Assets/SiegeSandbox/Resources/Sfx/<SoundName>/*.wav   mono, 48 kHz, 16-bit

Each output folder is named after a SoundId. Whatever is in a folder replaces the built-in
synthesized stand-in for that sound, one file per variation.
"""
import os, subprocess, wave, shutil
import numpy as np

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "AudioSources")
OUT = os.path.join(ROOT, "Assets", "SiegeSandbox", "Resources", "Sfx")
SR = 48000
rng = np.random.default_rng(7)


def load(rel):
    raw = subprocess.run(["ffmpeg", "-v", "error", "-i", os.path.join(SRC, rel), "-ac", "1", "-ar", str(SR), "-f", "f32le", "-"],
                         capture_output=True, check=True).stdout
    return np.frombuffer(raw, dtype=np.float32).astype(np.float64)


def cut(x, start, length=None, fade_in=0.002, fade_out=0.05):
    """A piece of a recording, with short fades so it neither clicks in nor out."""
    a = max(0, int(start * SR))
    b = len(x) if length is None else min(len(x), a + int(length * SR))
    y = x[a:b].copy()
    n = min(len(y), int(fade_in * SR))
    if n > 1: y[:n] *= np.linspace(0.0, 1.0, n)
    n = min(len(y), int(fade_out * SR))
    if n > 1: y[-n:] *= np.linspace(1.0, 0.0, n) ** 2
    return y


def onset(x, near, window=0.04):
    """The moment a hit actually starts: the sharpest rise in loudness around an estimate."""
    a = max(0, int((near - window) * SR)); b = min(len(x), int((near + window) * SR))
    step = SR // 1000
    seg = np.abs(x[a:b])
    env = seg[:len(seg) // step * step].reshape(-1, step).max(axis=1)
    rise = env[2:] - env[:-2]
    return (a + (int(np.argmax(rise)) + 1) * step) / SR


def level(x, peak):
    m = np.abs(x).max()
    return x * (peak / m) if m > 0 else x


def pitch(x, factor):
    """Play faster (factor above 1: higher and shorter) or slower."""
    n = int(len(x) / factor)
    return np.interp(np.linspace(0, len(x) - 1, n), np.arange(len(x)), x)


def lowpass(x, hz):
    a = 1.0 - np.exp(-2.0 * np.pi * hz / SR)
    y = np.empty_like(x); s = 0.0
    for i in range(len(x)):
        s += a * (x[i] - s); y[i] = s
    return y


def mix(parts, length):
    """Lay several sounds over each other: parts is a list of (sound, start seconds, gain)."""
    y = np.zeros(int(length * SR))
    for x, at, gain in parts:
        a = int(at * SR); b = min(len(y), a + len(x))
        if b > a: y[a:b] += x[:b - a] * gain
    return y


def room(dry, rt60, predelay, bright_hz, length):
    """
    The room's answer to a sound, without the sound itself: the recording convolved with a
    decaying burst of noise standing in for a measured impulse response.
    """
    n = int(length * SR)
    t = np.arange(n) / SR
    ir = rng.standard_normal(n) * np.exp(-6.91 * t / rt60)
    ir = lowpass(ir, bright_hz)
    # A few distinct early reflections before the wash sets in.
    for k in range(6):
        at = int((predelay + 0.004 + 0.011 * k * (1.0 + 0.3 * rng.random())) * SR)
        if at < n: ir[at] += (0.9 - 0.12 * k) * (1 if k % 2 == 0 else -1) * 6.0
    ir[:int(predelay * SR)] = 0.0
    size = 1 << int(np.ceil(np.log2(len(dry) + n)))
    wet = np.fft.irfft(np.fft.rfft(dry, size) * np.fft.rfft(ir, size), size)[:len(dry) + n]
    return cut(wet, 0, None, 0.004, min(0.4, length * 0.5))


def save(name, clips, peak):
    target = os.path.join(OUT, name)
    if os.path.isdir(target):
        for f in os.listdir(target):
            if f.endswith(".wav"): os.remove(os.path.join(target, f))
    os.makedirs(target, exist_ok=True)
    clips = [x for x in clips if len(x) > SR // 100]
    for i, x in enumerate(clips):
        pcm = (np.clip(level(x, peak), -1.0, 1.0) * 32767.0).astype("<i2")
        with wave.open(os.path.join(target, "%s_%02d.wav" % (name, i + 1)), "wb") as w:
            w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR); w.writeframes(pcm.tobytes())
    print("%-18s %d clip(s)  %s" % (name, len(clips), " ".join("%.2fs" % (len(x) / SR) for x in clips)))


def folder(rel, stem=""):
    """Every recording in a source folder whose name starts with stem, in name order."""
    names = sorted(f for f in os.listdir(os.path.join(SRC, rel)) if f.startswith(stem) and f.lower().endswith((".wav", ".ogg")))
    return [load(os.path.join(rel, f)) for f in names]


def prep(x, longest):
    """Drop the silence before a sound starts and anything past the length wanted."""
    if len(x) == 0: return x
    loud = np.nonzero(np.abs(x) > 0.04 * np.abs(x).max())[0]
    start = max(0, loud[0] - int(0.002 * SR)) if len(loud) else 0
    return cut(x[start:], 0, longest, 0.001, min(0.08, longest * 0.3))


def main():
    # ------------------------------------------------------------------ the rifle (Snake's Authentic Gun Sounds, 5.56)
    single = load("snake/556 Single Isolated WAV.wav")
    double = load("snake/556 Double Tap Isolated WAV.wav")
    burst = load("snake/556 Burst Isolated WAV.wav")
    slow = load("snake/556 Spray Slow Isolated WAV.wav")
    full = load("snake/556 Single WAV.wav")

    def shot(x, near):
        return cut(x, onset(x, near) - 0.003, 0.48, 0.001, 0.12)

    # Four separate shots, each cut where its own tail is free of the next shot.
    shots = [shot(single, 0.028), shot(double, 0.278), shot(burst, 0.43), shot(slow, 0.63)]
    save("GunShot", shots, 0.95)

    # Outdoors: what is left ringing after the shot in the full recordings.
    t0 = onset(full, 0.028)
    near = load("firearm/D_32P.wav")
    n0 = onset(near, 0.702, 0.06)
    save("TailOutdoor", [cut(full, t0 + 0.16, 2.0, 0.10, 0.5), cut(near, n0 + 0.14, 2.2, 0.08, 0.6)], 0.7)

    # The same rifle heard from a distance, for the shot coming back off a far wall.
    mid = load("firearm/D_24P.wav")
    m0 = onset(mid, 0.548, 0.06)
    save("Echo", [lowpass(cut(mid, m0 - 0.004, 1.3, 0.002, 0.5), 3800.0)], 0.6)

    # Indoors: the dry shot answered by a small room and by a hall.
    dry = cut(single, t0 - 0.003, 0.30, 0.001, 0.1)
    dry2 = cut(double, onset(double, 0.278) - 0.003, 0.30, 0.001, 0.1)
    save("TailSmall", [room(dry, 0.38, 0.006, 6500.0, 0.6), room(dry2, 0.42, 0.007, 6000.0, 0.65)], 0.7)
    save("TailRoom", [room(dry, 1.0, 0.018, 3800.0, 1.4), room(dry2, 1.1, 0.020, 3500.0, 1.5)], 0.7)

    # Handling: magazine out, magazine in (trimmed so the click lands on the cue), charging handle back and home.
    part1 = load("snake/AR Reload Part 1 WAV.wav")
    part2 = load("snake/AR Reload Part 2 WAV.wav")
    handle = load("snake/AR Charging Handle WAV.wav")
    save("MagOut", [cut(part1, 0.17, None, 0.01, 0.05)], 0.7)
    save("MagIn", [cut(part2, 0.33, None, 0.01, 0.05)], 0.8)
    save("BoltBack", [cut(handle, 0.015, 0.275, 0.003, 0.03)], 0.65)
    save("BoltForward", [cut(handle, 0.290, None, 0.003, 0.05)], 0.85)

    # ------------------------------------------------------------------ boots (Nox Sound, Footsteps Essentials: hard floor)
    tile = "nox/Essentials_Series_NOX_SOUND/Footsteps_Essentials_NOX_SOUND/Footsteps_Tile/"
    rock = "nox/Essentials_Series_NOX_SOUND/Footsteps_Essentials_NOX_SOUND/Footsteps_Rock/"
    walk = [prep(x, 0.6) for x in folder(tile + "Footsteps_Tile_Walk")]
    run = [prep(x, 0.45) for x in folder(tile + "Footsteps_Tile_Run")]
    save("Footstep", walk, 0.8)
    save("FootstepRun", run, 0.9)
    save("NpcFootstep", [pitch(x, 0.94) for x in run], 0.9)          # somebody else's boots
    jump = folder(tile + "Footsteps_Tile_Jump")                       # sorted: three landings, then three take-offs
    lands, starts = [prep(x, 0.7) for x in jump[:3]], [prep(x, 0.5) for x in jump[3:]]
    save("Land", lands, 0.9)
    save("Jump", starts, 0.6)
    grit = [prep(x, 0.5) for x in folder(rock + "Footsteps_Rock_Walk")]
    save("Crawl", [pitch(x, 0.8) for x in grit[:5]], 0.45)            # knees and elbows dragging over grit

    # ------------------------------------------------------------------ wood and metal (rubberduck, 100 CC0 metal and wood SFX)
    def duck(stem):
        return [prep(x, 1.6) for x in folder("ogawood", stem)]
    breaking, cracking, falling = duck("wood_breaking"), duck("wood_cracking"), duck("wood_falling")
    hits, slams, squeaks = duck("wood_hit"), duck("wood_slam"), duck("wood_squeak")
    save("PlankBreak", [cut(x, 0, 0.9, 0.0005, 0.25) for x in breaking + cracking], 0.9)
    save("ImpactWood", [cut(x, 0, 0.35, 0.0005, 0.12) for x in cracking + hits[:3]], 0.75)
    save("WallPunch", [cut(x, 0, 0.6, 0.0005, 0.2) for x in slams], 0.9)
    save("Hammer", [cut(x, 0, 0.4, 0.0005, 0.12) for x in duck("wood_hammer") + duck("hammer")[:2]], 0.85)
    save("Pry", [cut(x, 0, 0.7, 0.002, 0.2) for x in squeaks + cracking[:1]], 0.65)
    save("BarricadeUnroll", [cut(x, 0, 0.7, 0.002, 0.25) for x in falling[:3]], 0.55)
    # A whole barricade coming down: it breaks, then the boards land one after another.
    def collapse(order):
        parts = [(breaking[order[0] % len(breaking)], 0.0, 1.0), (falling[order[1]], 0.08, 0.9), (cracking[order[2] % len(cracking)], 0.17, 0.7),
                 (falling[order[3]], 0.30, 0.8), (falling[order[4]], 0.52, 0.6)]
        return cut(mix(parts, 2.0), 0, None, 0.0005, 0.4)
    save("BarricadeCollapse", [collapse([0, 3, 1, 4, 5]), collapse([1, 5, 2, 3, 4])], 0.9)
    save("MagDrop", [cut(x, 0, 0.6, 0.0005, 0.2) for x in duck("metal_falling")], 0.55)

    # ------------------------------------------------------------------ brass and an empty chamber (Cyber Apps, Gun Pack)
    casings = [cut(prep(x, 1.1), 0, 1.1, 0.0005, 0.35) for x in folder("cyber/gun/v1/shellcasings/wav")[:8]]
    save("ShellCasing", casings, 0.5)
    save("DryFire", [cut(prep(x, 0.4), 0, 0.4, 0.0005, 0.1) for x in folder("cyber/gun/v1/gunempty/wav")[:5]], 0.55)

    # ------------------------------------------------------------------ bullets arriving
    # None of the packs has recorded bullet impacts, so these are built from the nearest real
    # material: grit for masonry, a wooden knock for the soft wall, a body-weight thump for a hit.
    stone_run = [prep(x, 0.4) for x in folder(rock + "Footsteps_Rock_Run")]
    save("ImpactHard", [cut(pitch(x, 1.5), 0, 0.28, 0.0005, 0.12) for x in stone_run[:5]], 0.7)
    save("ImpactSoftWall", [cut(pitch(x, 1.25), 0, 0.25, 0.0005, 0.1) for x in hits[3:8]], 0.6)
    thump = [lowpass(pitch(x, 0.8), 1800.0) for x in lands]
    save("ImpactFlesh", [cut(x, 0, 0.4, 0.0005, 0.15) for x in thump], 0.7)
    save("Headshot", [cut(mix([(thump[i], 0.0, 1.0), (pitch(cracking[i], 1.3), 0.0, 0.8)], 0.5), 0, None, 0.0005, 0.15) for i in range(3)], 0.85)
    save("BodyFall", [cut(lowpass(pitch(x, 0.7), 2500.0), 0, 0.9, 0.0005, 0.3) for x in lands], 0.9)


if __name__ == "__main__":
    main()
