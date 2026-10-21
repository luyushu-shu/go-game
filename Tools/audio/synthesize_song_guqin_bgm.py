#!/usr/bin/env python3
"""
Song-dynasty minimal guqin BGM for Go UI.
Sparse, cold, loopable, 48 kHz stereo WAV. No drums / vocal / glitter.
"""

from __future__ import annotations

import math
import wave
from pathlib import Path

import numpy as np
from scipy.signal import lfilter

SR = 48000
BPM = 58.0
LOOP_SEC = 72.0  # multiple of 12 s (xun cadence)
N = int(SR * LOOP_SEC)

# C Gong pentatonic (宫商角徵羽), mid-low — 平正冲和
NOTE = {
    "G2": 98.0000,
    "A2": 110.0000,
    "C3": 130.8128,
    "D3": 146.8324,
    "E3": 164.8138,
    "G3": 196.0000,
    "A3": 220.0000,
    "C4": 261.6256,
    "D4": 293.6648,
    "E4": 329.6276,
}


def db(x: float) -> float:
    return 10 ** (x / 20.0)


def soft_clip(x: np.ndarray, drive: float = 1.04) -> np.ndarray:
    return np.tanh(x * drive) / math.tanh(drive)


def one_pole_lp(x: np.ndarray, cutoff_hz: float) -> np.ndarray:
    if cutoff_hz <= 0:
        return np.zeros_like(x)
    a = math.exp(-2.0 * math.pi * cutoff_hz / SR)
    return lfilter([1.0 - a], [1.0, -a], x)


def one_pole_hp(x: np.ndarray, cutoff_hz: float) -> np.ndarray:
    return x - one_pole_lp(x, cutoff_hz)


def apply_reverb(x: np.ndarray, wet: float = 0.12, decay: float = 1.35) -> np.ndarray:
    """Small clear 书斋 room — transparent, not muddy."""
    delays_ms = [27.0, 34.0, 41.0, 52.0]
    wet_sig = np.zeros_like(x)
    for i, ms in enumerate(delays_ms):
        d = int(ms * 0.001 * SR)
        tap = np.zeros_like(x)
        if d < len(x):
            tap[d:] = x[:-d]
        env = np.exp(-np.arange(len(x)) / (SR * decay * (0.5 + 0.1 * i)))
        wet_sig += tap * env * (0.5**i)
    wet_sig = one_pole_lp(wet_sig, 3800)
    return soft_clip((1.0 - wet) * x + wet * wet_sig)


def place(buf: np.ndarray, sig: np.ndarray, t: float) -> None:
    start = int(round(t * SR))
    if start >= len(buf) or start < 0:
        return
    end = min(len(buf), start + len(sig))
    buf[start:end] += sig[: end - start]


def jade_qing(freq: float = 1480.0, amp: float = db(-16)) -> np.ndarray:
    """Single jade chime — clear, cold, short airy tail."""
    dur = 2.4
    n = int(dur * SR)
    t = np.arange(n) / SR
    partials = [
        (1.00, 1.00, 1.8),
        (2.01, 0.32, 1.1),
        (2.76, 0.18, 0.8),
        (4.15, 0.06, 0.45),
    ]
    y = np.zeros(n)
    for m, a, d in partials:
        y += a * np.sin(2 * math.pi * freq * m * t) * np.exp(-t / d)
    y += np.exp(-t / 0.0035) * np.sin(2 * math.pi * 5100 * t) * 0.22
    atk = np.minimum(1.0, t / 0.002)
    y *= atk * np.exp(-t / 0.95)
    y = one_pole_lp(y, 6200)
    peak = np.max(np.abs(y)) + 1e-12
    return y / peak * amp


def guqin_note(
    freq: float,
    dur: float = 4.2,
    amp: float = db(-24),
    brightness: float = 0.42,
) -> np.ndarray:
    """素弦冷韵 — dry, thin, no vibrato, no lush glitter."""
    n = int(dur * SR)
    t = np.arange(n) / SR
    rng = np.random.default_rng(int(freq * 997) % (2**31 - 1))
    noise = rng.uniform(-1, 1, size=max(8, int(0.005 * SR)))
    exc = np.zeros(n)
    exc[: len(noise)] = noise * np.hanning(len(noise))

    partials = [
        (1.000, 1.00, 2.1),
        (2.003, 0.22 * brightness, 1.15),
        (3.010, 0.07 * brightness, 0.65),
        (4.020, 0.022 * brightness, 0.38),
    ]
    y = np.zeros(n)
    for m, a, d in partials:
        det = 1.0 + (0.00035 if m > 1 else 0.0)
        y += a * np.sin(2 * math.pi * freq * m * det * t) * np.exp(-t / d)

    exc_f = one_pole_lp(exc, 1600)
    y = 0.90 * y + 0.10 * exc_f * np.exp(-t / 0.28)
    y *= np.minimum(1.0, t / 0.007) * np.exp(-t / (dur * 0.36))
    y = one_pole_hp(y, 95)
    y = one_pole_lp(y, 2500 + 700 * brightness)
    peak = np.max(np.abs(y)) + 1e-12
    return y / peak * amp


def xun_drone(freq: float = 146.83, dur: float = 8.5, amp: float = db(-41)) -> np.ndarray:
    """极淡竹埙 — flat, distant, no swell drama."""
    n = int(dur * SR)
    t = np.arange(n) / SR
    rng = np.random.default_rng(20261021 + int(freq))
    breath = one_pole_lp(rng.normal(0, 1, n), 520) * 0.07
    tone = (
        0.88 * np.sin(2 * math.pi * freq * t)
        + 0.10 * np.sin(2 * math.pi * freq * 2.0 * t)
        + 0.02 * np.sin(2 * math.pi * freq * 3.0 * t)
    )
    gate = np.ones(n)
    fade = int(2.0 * SR)
    gate[:fade] = np.linspace(0, 1, fade)
    gate[-fade:] = np.linspace(1, 0, fade)
    # almost no amplitude contour beyond fades
    y = one_pole_lp((tone * 0.93 + breath) * gate, 850)
    peak = np.max(np.abs(y)) + 1e-12
    return y / peak * amp


def forest_air(n: int, amp: float = db(-50)) -> np.ndarray:
    """细碎空林风息 — barely-there bed."""
    rng = np.random.default_rng(42)
    x = one_pole_hp(one_pole_lp(rng.normal(0, 1, n), 340), 130)
    t = np.arange(n) / SR
    mod = 0.88 + 0.12 * np.sin(2 * math.pi * t / 29.0)
    y = x * mod
    peak = np.max(np.abs(y)) + 1e-12
    return y / peak * amp


def make_piece() -> np.ndarray:
    dry = np.zeros(N, dtype=np.float64)

    # Opening: 玉磬 → 2s absolute blank → three lean guqin tones
    place(dry, jade_qing(1480.0, amp=db(-15)), 0.00)
    # 玉磬落地后 2.0s 绝对空白，再以三音起韵
    place(dry, guqin_note(NOTE["D4"], dur=4.8, amp=db(-22), brightness=0.40), 2.55)
    place(dry, guqin_note(NOTE["A3"], dur=5.0, amp=db(-24), brightness=0.36), 6.00)
    place(dry, guqin_note(NOTE["G3"], dur=5.2, amp=db(-25), brightness=0.34), 9.80)

    # Body: 马一角夏半边 — few notes, long rests (aim ~70% blank)
    # Keep decays short enough that gaps stay empty.
    body = [
        (15.5, "E3", 4.5, -27, 0.32),
        (21.0, "C4", 4.0, -26, 0.38),
        (27.5, "G3", 4.8, -28, 0.30),
        (34.0, "A3", 4.2, -27, 0.34),
        (40.5, "D3", 5.0, -29, 0.28),
        (47.0, "E4", 3.8, -28, 0.36),
        (53.0, "C3", 5.2, -30, 0.26),
        (59.5, "G3", 4.5, -28, 0.30),
        # last gesture early enough to die before loop seam silence
        (65.0, "A2", 3.5, -31, 0.24),
    ]
    for t, name, dur, adb, br in body:
        place(dry, guqin_note(NOTE[name], dur=dur, amp=db(adb), brightness=br), t)

    # Xun every 12 s (skip t=0 — leave for qing)
    for t, name in zip([12.0, 24.0, 36.0, 48.0, 60.0], ["D3", "A2", "G3", "C3", "D3"]):
        place(dry, xun_drone(NOTE[name], dur=8.0, amp=db(-42)), t)

    air = forest_air(N, amp=db(-51))

    # Absolute blank after qing attack body: exactly 2.0 s of true zeros
    z0 = int(0.50 * SR)
    z1 = int(2.50 * SR)
    dry[z0:z1] = 0.0
    air[z0:z1] = 0.0

    # Reverb only on instrumental dry (not air), then restore blank
    wet = apply_reverb(dry, wet=0.11, decay=1.25)
    wet[z0:z1] = 0.0
    mix = wet + air
    mix[z0:z1] = 0.0

    # Loop seam: last 3.2 s and first 0.0–0.0 are designed quiet.
    # Micro equal-power crossfade only on final/first 0.35 s (both near-silent air).
    # Do NOT pull tail music into the sacred blank window.
    fade = int(0.35 * SR)
    # Ensure tail is near silent: duck last 2.8 s gently into air-only
    duck_n = int(2.8 * SR)
    duck = np.linspace(1.0, 0.15, duck_n)
    mix[-duck_n:] *= duck
    # Keep opening qing intact; crossfade only samples before qing peak clutter:
    # fade region is 0–0.35s which includes qing attack — better: crossfade
    # at the *end* only by blending a copy of the near-silent pre-qing air.
    # Build seamless join: end should match the silence just before t=0.
    # Soften first 0.05s of qing? No — keep qing clean each loop.
    # Use very short fade (50 ms) of air continuity only:
    fade = int(0.05 * SR)
    head = mix[:fade].copy()
    tail = mix[-fade:].copy()
    mix[-fade:] = tail * np.linspace(1, 0, fade) + head * np.linspace(0, 1, fade)

    # Re-assert absolute blank after all processing
    mix[z0:z1] = 0.0

    mix = soft_clip(mix * 0.95)
    peak = np.max(np.abs(mix)) + 1e-12
    mix *= db(-15) / peak
    mix = one_pole_hp(mix, 28)
    mix[z0:z1] = 0.0
    return mix


def write_stereo_wav(path: Path, audio: np.ndarray) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    d = int(0.009 * SR)
    right = np.zeros_like(audio)
    right[d:] = audio[:-d] * 0.98
    left = audio * 0.98
    l = 0.93 * left + 0.07 * right
    r = 0.93 * right + 0.07 * left
    # preserve absolute blank on both channels
    z0 = int(0.50 * SR)
    z1 = int(2.50 * SR)
    l[z0:z1] = 0.0
    r[z0:z1] = 0.0
    interleaved = np.empty(len(audio) * 2, dtype=np.float64)
    interleaved[0::2] = l
    interleaved[1::2] = r
    pcm16 = (np.clip(interleaved, -1.0, 1.0) * 32767.0).astype(np.int16)
    with wave.open(str(path), "wb") as w:
        w.setnchannels(2)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(pcm16.tobytes())


def main() -> None:
    print(f"Rendering {LOOP_SEC:.0f}s @ {SR} Hz, BPM {BPM} ...")
    audio = make_piece()
    out = Path("/workspace/Assets/Audio/BGM/go_ui_song_guqin_loop.wav")
    write_stereo_wav(out, audio)
    peak = float(np.max(np.abs(audio)))
    rms = float(np.sqrt(np.mean(audio**2)))
    print(f"Wrote {out}")
    print(f"peak={peak:.4f} rms={rms:.5f} ({20 * math.log10(rms + 1e-12):.1f} dBFS)")


if __name__ == "__main__":
    main()
