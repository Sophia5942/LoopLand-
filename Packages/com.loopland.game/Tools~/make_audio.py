#!/usr/bin/env python3
"""Synthesizes LoopLand's audio: theme tune, city ambience, fountain/waterfall water and elevator sounds.
Everything is generated (no samples), written as loop-ready OGG Vorbis files into ../Audio.
Needs numpy, scipy and ffmpeg (with libvorbis). Run: python3 make_audio.py"""
import os, subprocess, wave
import numpy as np
from scipy import signal

SR = 44100
OUT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'Audio'))
rng = np.random.default_rng(11)


# ----------------------------------------------------------------------------- helpers

def secs(n):
    return np.arange(n) / SR


def filt(x, kind, fc, order=2):
    fc = np.atleast_1d(np.asarray(fc, dtype=float)) / (SR / 2)
    b, a = signal.butter(order, fc if len(fc) > 1 else fc[0], kind)
    return signal.lfilter(b, a, x, axis=0)


def hz(midi):
    return 440.0 * 2 ** ((midi - 69) / 12)


def pan(x, p):
    """Mono -> stereo, p in -1..1 (equal power)."""
    a = (p + 1) * np.pi / 4
    return np.stack([x * np.cos(a), x * np.sin(a)], 1)


def add(buf, x, start):
    i = int(round(start * SR))
    if i >= len(buf):
        return
    n = min(len(x), len(buf) - i)
    buf[i:i + n] += x[:n]


def saw(f, n, ph0=0.0):
    ph = (ph0 + f * np.arange(n) / SR) % 1.0
    y = 2 * ph - 1
    dt = f / SR
    m = ph < dt
    t = ph[m] / dt
    y[m] -= t + t - t * t - 1
    m = ph > 1 - dt
    t = (ph[m] - 1) / dt
    y[m] -= t * t + t + t + 1
    return y


def adsr(n, a, d, s, r, hold):
    """Envelope of n samples: attack a, decay d to level s, held until `hold` seconds, release r."""
    t = secs(n)
    e = np.where(t < a, t / max(a, 1e-4), s + (1 - s) * np.exp(-(t - a) / max(d, 1e-4)))
    rel = t > hold
    e[rel] *= np.exp(-(t[rel] - hold) / max(r, 1e-4))
    return e


def reverb_ir(length, decay, bright=9000, dark=2200, seed=3):
    r = np.random.default_rng(seed)
    n = int(length * SR)
    t = secs(n)
    ir = r.standard_normal((n, 2)) * np.exp(-t * 6.9 / decay)[:, None]
    lo = filt(ir, 'low', dark)
    hi = filt(ir, 'low', bright)
    w = np.clip(t / length * 1.6, 0, 1)[:, None]
    ir = hi * (1 - w) + lo * w
    k = int(0.01 * SR)
    ir[:k] *= np.linspace(0, 1, k)[:, None]
    return ir / np.sqrt(np.sum(ir ** 2, axis=0))


def convolve(x, ir):
    return np.stack([signal.fftconvolve(x[:, c], ir[:, c % ir.shape[1]]) for c in range(x.shape[1])], 1)


def fold_loop(x, n):
    """Wrap everything past n samples back onto the start: a seamless loop of length n."""
    y = x[:n].copy()
    tail = x[n:]
    while len(tail):
        k = min(len(tail), n)
        y[:k] += tail[:k]
        tail = tail[k:]
    return y


def crossfade_loop(x, n, fade):
    """Loop of n samples from a render of n + fade samples (equal-power crossfade of the overhang)."""
    f = int(fade * SR)
    y = x[:n].copy()
    w = np.sin(np.linspace(0, np.pi / 2, f)) ** 2
    if y.ndim == 2:
        w = w[:, None]
    y[:f] = y[:f] * w + x[n:n + f] * (1 - w)
    return y


def normalize(x, peak_db):
    return x * (10 ** (peak_db / 20) / (np.max(np.abs(x)) + 1e-9))


def write(name, x, quality):
    os.makedirs(OUT, exist_ok=True)
    assert np.all(np.isfinite(x)), name + ' has NaN/inf samples'
    x = np.clip(x, -1, 1)
    ch = 1 if x.ndim == 1 else x.shape[1]
    tmp = os.path.join(OUT, name + '.tmp.wav')
    with wave.open(tmp, 'wb') as w:
        w.setnchannels(ch)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes((x * 32767).astype('<i2').tobytes())
    dst = os.path.join(OUT, name + '.ogg')
    subprocess.run(['ffmpeg', '-y', '-loglevel', 'error', '-i', tmp, '-c:a', 'libvorbis', '-q:a', str(quality), dst], check=True)
    os.remove(tmp)
    print('%-22s %5.1f s  %d ch  %6.0f KB' % (name, len(x) / SR, ch, os.path.getsize(dst) / 1024))


# ----------------------------------------------------------------------------- theme tune

BPM = 100
BEAT = 60 / BPM
BAR = 4 * BEAT
BARS = 32
F, G, A, Bb, C, D, E = 65, 67, 69, 70, 72, 74, 76  # F4..E5 (midi)

CHORDS = {  # root (bass) + voicing (midi)
    'Fmaj7': (41, [53, 57, 60, 64]), 'Cadd9': (36, [55, 60, 62, 64]), 'Dm7': (38, [53, 57, 60, 62]),
    'Bbmaj7': (34, [53, 57, 58, 62]), 'F': (41, [53, 57, 60, 65]), 'C': (36, [52, 55, 60, 64]),
    'Dm': (38, [53, 57, 62, 65]), 'Bb': (34, [53, 58, 62, 65]), 'Gm7': (43, [53, 58, 62, 67]), 'C7': (36, [52, 55, 58, 64]),
}
PROG = (['Fmaj7', 'Cadd9', 'Dm7', 'Bbmaj7'] +
        ['F', 'C', 'Dm', 'Bb', 'F', 'C', 'Bb', 'C'] +
        ['Dm', 'Bb', 'F', 'C', 'Dm', 'Bb', 'Gm7', 'C7'] +
        ['F', 'C', 'Dm', 'Bb', 'F', 'C', 'Bb', 'C'] +
        ['Fmaj7', 'Cadd9', 'Dm7', 'Bbmaj7'])

# main hook (8 bars), (midi, beat, length in beats)
HOOK = [
    [(69, 0, .5), (72, .5, .5), (77, 1, 1), (76, 2, .5), (77, 2.5, .5), (72, 3, 1)],
    [(67, 0, .5), (72, .5, .5), (76, 1, 1), (74, 2, .5), (76, 2.5, .5), (79, 3, 1)],
    [(77, 0, 1.5), (76, 1.5, .5), (74, 2, 1), (69, 3, 1)],
    [(70, 0, .5), (74, .5, .5), (77, 1, 1), (76, 2, .5), (74, 2.5, .5), (72, 3, 1)],
    [(69, 0, .5), (72, .5, .5), (77, 1, 1), (79, 2, .5), (81, 2.5, .5), (79, 3, 1)],
    [(76, 0, 1), (72, 1, .5), (74, 1.5, .5), (76, 2, 1), (79, 3, 1)],
    [(77, 0, 1), (74, 1, .5), (70, 1.5, .5), (74, 2, 1), (77, 3, 1)],
    [(76, 0, 1.5), (74, 1.5, .5), (72, 2, 2)],
]
COUNTER = [
    [(74, 0, 1), (77, 1, 1), (81, 2, 1.5), (79, 3.5, .5)],
    [(77, 0, 1), (74, 1, 1), (70, 2, 2)],
    [(72, 0, 1), (77, 1, 1), (81, 2, 1), (84, 3, 1)],
    [(79, 0, 2), (76, 2, 1), (72, 3, 1)],
    [(74, 0, .5), (76, .5, .5), (77, 1, 1), (81, 2, 1), (86, 3, 1)],
    [(84, 0, 1), (82, 1, 1), (77, 2, 2)],
    [(82, 0, 1), (81, 1, 1), (79, 2, 1), (77, 3, 1)],
    [(76, 0, 2), (79, 2, 2)],
]


def pad_voice(f, n):
    y = np.zeros(n)
    for det in (-0.07, 0.0, 0.07):
        y += saw(f * 2 ** (det / 12), n, rng.random())
    return y / 3


def ep(f, dur, vel):
    """Soft FM electric piano / bell."""
    n = int(dur * SR)
    t = secs(n)
    mod = np.sin(2 * np.pi * f * t) * 1.6 * np.exp(-t * 3.5)
    y = np.sin(2 * np.pi * f * t + mod) * np.exp(-t * 2.2)
    y += 0.25 * np.sin(2 * np.pi * f * 4.0 * t) * np.exp(-t * 9)
    a = int(0.003 * SR)
    y[:a] *= np.linspace(0, 1, a)
    return y * vel


def bell(f, dur, vel):
    n = int(dur * SR)
    t = secs(n)
    mod = np.sin(2 * np.pi * f * 3.5 * t) * 2.2 * np.exp(-t * 5)
    y = np.sin(2 * np.pi * f * t + mod) * np.exp(-t * 1.6)
    a = int(0.002 * SR)
    y[:a] *= np.linspace(0, 1, a)
    return y * vel


def lead(f, dur, vel):
    n = int((dur + 0.25) * SR)
    t = secs(n)
    vib = 1 + 0.004 * np.sin(2 * np.pi * 5.2 * t) * np.clip((t - 0.18) / 0.2, 0, 1)
    ph = np.cumsum(f * vib) / SR
    sq = np.tanh(3 * np.sin(2 * np.pi * ph)) * 0.6 + 0.4 * np.sin(2 * np.pi * 2 * ph) * 0.5
    y = filt(sq, 'low', 3200)
    return y * adsr(n, 0.025, 0.25, 0.75, 0.12, dur) * vel


def bass(f, dur, vel):
    n = int((dur + 0.12) * SR)
    t = secs(n)
    y = np.sin(2 * np.pi * f * t) + 0.35 * np.sin(2 * np.pi * 2 * f * t) + 0.12 * np.sin(2 * np.pi * 3 * f * t)
    return np.tanh(1.4 * y) * adsr(n, 0.006, 0.3, 0.7, 0.06, dur) * vel


def kick():
    n = int(0.45 * SR)
    t = secs(n)
    f = 46 + 80 * np.exp(-t / 0.035)
    y = np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-t / 0.22)
    click = filt(rng.standard_normal(n), 'high', 2500) * np.exp(-t / 0.003) * 0.25
    return np.tanh(1.5 * (y + click))


def clap():
    n = int(0.5 * SR)
    t = secs(n)
    nz = filt(rng.standard_normal(n), 'band', [900, 4200])
    env = np.zeros(n)
    for k, off in enumerate((0, 0.011, 0.022)):
        m = t >= off
        env[m] += np.exp(-(t[m] - off) / 0.006) * (0.8 if k < 2 else 1)
    m = t >= 0.03
    env[m] += 0.55 * np.exp(-(t[m] - 0.03) / 0.11)
    body = np.sin(2 * np.pi * 190 * t) * np.exp(-t / 0.05) * 0.3
    return nz * env + body


def hat(open_=False):
    n = int((0.25 if open_ else 0.08) * SR)
    t = secs(n)
    nz = filt(rng.standard_normal(n), 'high', 7000)
    return nz * np.exp(-t / (0.09 if open_ else 0.022))


def shaker():
    n = int(0.09 * SR)
    t = secs(n)
    nz = filt(rng.standard_normal(n), 'band', [4500, 10000])
    return nz * np.sin(np.pi * np.clip(t / 0.09, 0, 1)) ** 2


def riser(dur):
    n = int(dur * SR)
    t = secs(n)
    nz = rng.standard_normal(n)
    y = np.zeros(n)
    for i in range(0, n, 2048):  # sweeping band
        fc = 600 + 7000 * (i / n) ** 2
        seg = nz[i:i + 2048]
        y[i:i + len(seg)] = filt(seg, 'band', [fc, min(fc * 1.8, 20000)], 1)
    return y * (t / dur) ** 2


def theme():
    total = int(BARS * BAR * SR)
    tail = int(4 * SR)
    stems = {k: np.zeros((total + tail, 2)) for k in ('pad', 'keys', 'lead', 'bass', 'drums', 'fx')}
    duck = np.ones(total + tail)
    for bar, name in enumerate(PROG):
        t0 = bar * BAR
        root, notes = CHORDS[name]
        intro = bar < 4 or bar >= 28
        groove = not intro
        # pad (held chord with swell), wide stereo
        n = int((BAR + 1.0) * SR)
        env = adsr(n, 0.45, 1.0, 0.85, 0.5, BAR)
        for i, m in enumerate(notes):
            v = pad_voice(hz(m), n) * env * 0.16
            add(stems['pad'], pan(v, -0.6 + 1.2 * i / 3), t0)
        # electric piano arpeggio in eighths
        arp = [notes[0] + 12, notes[1] + 12, notes[2] + 12, notes[3] + 12, notes[2] + 12, notes[1] + 12, notes[3] + 12, notes[2] + 12]
        for k, m in enumerate(arp):
            add(stems['keys'], pan(ep(hz(m), 0.9, 0.11 if intro else 0.08), -0.35 if k % 2 == 0 else 0.35), t0 + k * BEAT / 2)
        # bass
        if intro:
            add(stems['bass'], pan(bass(hz(root), BAR * 0.95, 0.35), 0), t0)
        else:
            for beat, ln, oct_ in ((0, 1.4, 0), (1.5, 0.45, 0), (2, 0.9, 0), (3, 0.4, 12), (3.5, 0.45, 0)):
                add(stems['bass'], pan(bass(hz(root + oct_), ln * BEAT, 0.42), 0), t0 + beat * BEAT)
        # drums
        for s in range(8):
            sw = 0.04 * BEAT if s % 2 == 1 else 0.0
            add(stems['drums'], pan(hat(open_=(s == 7 and bar % 2 == 1 and groove)) * (0.16 if s % 2 == 0 else 0.11), 0.25), t0 + s * BEAT / 2 + sw)
        if groove:
            for b in (0, 2, 2.75) if bar % 2 == 1 else (0, 2):
                add(stems['drums'], pan(kick() * 0.85, 0), t0 + b * BEAT)
                i = int((t0 + b * BEAT) * SR)
                k = np.arange(min(int(0.3 * SR), total + tail - i))
                duck[i:i + len(k)] = np.minimum(duck[i:i + len(k)], 1 - 0.45 * np.exp(-k / SR / 0.12))
            for b in (1, 3):
                add(stems['drums'], pan(clap() * 0.38, -0.05), t0 + b * BEAT)
            for s in range(16):
                add(stems['drums'], pan(shaker() * (0.09 if s % 4 == 2 else 0.05), -0.4), t0 + s * BEAT / 4)
        # melodies
        if 4 <= bar < 12 or 20 <= bar < 28:
            for m, b, ln in HOOK[(bar - 4) % 8]:
                add(stems['lead'], pan(lead(hz(m), ln * BEAT * 0.92, 0.2), 0.05), t0 + b * BEAT)
                if bar >= 20:
                    add(stems['keys'], pan(bell(hz(m + 12), 1.6, 0.05), 0.3), t0 + b * BEAT)
        if 12 <= bar < 20:
            for m, b, ln in COUNTER[bar - 12]:
                add(stems['lead'], pan(bell(hz(m), max(1.2, ln * BEAT * 1.6), 0.16), -0.15), t0 + b * BEAT)
        if bar in (3, 19):
            add(stems['fx'], pan(riser(BAR) * 0.12, 0), t0)
    # ping-pong delay on the lead (dotted eighth)
    d = int(0.75 * BEAT * SR)
    ld = stems['lead']
    echo = np.zeros_like(ld)
    for k, g in ((1, 0.32), (2, 0.18), (3, 0.1)):
        sh = np.zeros_like(ld)
        sh[d * k:] = ld[:-d * k]
        echo[:, (k + 1) % 2] += (sh[:, 0] + sh[:, 1]) * 0.5 * g
    stems['lead'] = ld + filt(echo, 'low', 5000)
    stems['pad'] = filt(stems['pad'], 'low', 2400) * duck[:, None]
    stems['bass'] = stems['bass'] * (0.6 + 0.4 * duck[:, None])
    dry = stems['pad'] + stems['keys'] + stems['lead'] * 1.1 + stems['bass'] + stems['drums'] + stems['fx']
    send = stems['pad'] * 0.35 + stems['keys'] * 0.5 + stems['lead'] * 0.4 + stems['drums'] * 0.08 + stems['fx'] * 0.5
    wet = convolve(send, reverb_ir(2.8, 2.6))[:len(dry)]
    mix = dry + wet * 0.45
    mix = filt(mix, 'high', 30)
    mix = fold_loop(mix, total)
    mix = np.tanh(1.1 * mix / np.max(np.abs(mix))) / np.tanh(1.1)
    return normalize(mix, -3.5)


# ----------------------------------------------------------------------------- water

def bubbles(n, rate, rmin, rmax, gain=1.0):
    """Liquid sound from many resonating bubbles (van den Doel's model)."""
    y = np.zeros(n)
    count = int(rate * n / SR)
    starts = rng.integers(0, n, count)
    radii = np.exp(rng.uniform(np.log(rmin), np.log(rmax), count))  # mm
    for s, r in zip(starts, radii):
        f0 = 3260.0 / r
        dec = 0.043 * f0 + 0.0014 * f0 ** 1.5
        ln = min(int(5.0 / dec * SR), n - s)
        if ln < 8:
            continue
        t = np.arange(ln) / SR
        f = f0 * (1 + 0.12 * dec * t)
        y[s:s + ln] += np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-dec * t) * (r ** 0.6) * rng.uniform(0.3, 1.0)
    return y * gain


def slow_mod(n, rate, depth, seed):
    r = np.random.default_rng(seed)
    k = max(4, int(n / SR * rate * 4))
    pts = r.uniform(-1, 1, k)
    m = np.interp(np.linspace(0, k - 1, n), np.arange(k), pts)
    m = filt(m, 'low', max(rate, 0.05), 1)
    return 1 + depth * m / (np.max(np.abs(m)) + 1e-9)


def pink(n):
    w = rng.standard_normal(n)
    b = [0.049922035, -0.095993537, 0.050612699, -0.004408786]
    a = [1, -2.494956002, 2.017265875, -0.522189400]
    return signal.lfilter(b, a, w)


def brown(n):
    y = np.cumsum(rng.standard_normal(n))
    return filt(y, 'high', 20)


def fountain():
    L, fade = 14.0, 1.5
    n = int((L + fade) * SR)
    splash = filt(rng.standard_normal(n), 'band', [1200, 7500]) * slow_mod(n, 9, 0.6, 1)
    body = filt(pink(n), 'band', [250, 2500]) * slow_mod(n, 0.4, 0.25, 2)
    y = bubbles(n, 900, 0.7, 5.0) * 0.5 + splash * 0.35 + body * 2.2
    y = crossfade_loop(filt(y, 'high', 60), int(L * SR), fade)
    return normalize(y, -3.0)


def waterfall():
    L, fade = 14.0, 1.5
    n = int((L + fade) * SR)
    roar = filt(pink(n), 'low', 3500) * slow_mod(n, 0.3, 0.2, 3) * 3.0
    hiss = filt(rng.standard_normal(n), 'band', [3500, 11000]) * slow_mod(n, 1.5, 0.3, 4) * 0.12
    rumble = filt(brown(n), 'low', 220) * 0.02
    y = roar + hiss + rumble + bubbles(n, 1800, 1.0, 7.0) * 0.35
    y = crossfade_loop(filt(y, 'high', 35), int(L * SR), fade)
    return normalize(y, -3.0)


# ----------------------------------------------------------------------------- city ambience

def bird_call(kind):
    if kind == 0:  # chirp-chirp
        parts = []
        for _ in range(rng.integers(2, 5)):
            n = int(0.06 * SR)
            t = secs(n)
            f = np.linspace(rng.uniform(2800, 3400), rng.uniform(4600, 5600), n)
            parts.append(np.sin(2 * np.pi * np.cumsum(f) / SR) * np.clip(np.sin(np.pi * t / t[-1]), 0, None) ** 2)
            parts.append(np.zeros(int(rng.uniform(0.06, 0.1) * SR)))
        return np.concatenate(parts)
    if kind == 1:  # warble
        n = int(rng.uniform(0.5, 0.9) * SR)
        t = secs(n)
        f = rng.uniform(3200, 3900) + 650 * np.sin(2 * np.pi * rng.uniform(18, 28) * t)
        return np.sin(2 * np.pi * np.cumsum(f) / SR) * np.clip(np.sin(np.pi * t / t[-1]), 0, None) ** 1.5 * 0.7
    n = int(0.13 * SR)  # descending tweet
    t = secs(n)
    f = np.linspace(rng.uniform(5800, 6600), rng.uniform(3300, 3800), n)
    one = np.sin(2 * np.pi * np.cumsum(f) / SR) * np.sin(np.pi * t / t[-1]) ** 2
    return np.concatenate([one, np.zeros(int(0.12 * SR)), one * 0.8])


def city():
    L, fade = 40.0, 3.0
    n = int((L + fade) * SR)
    gust = slow_mod(n, 0.12, 0.7, 5)
    wind = np.stack([filt(brown(n), 'low', 700), filt(brown(n), 'low', 700)], 1) * gust[:, None] * 0.004
    leaves = np.stack([filt(rng.standard_normal(n), 'band', [2000, 6500]) for _ in range(2)], 1) * (gust ** 2)[:, None] * 0.012
    hum = pan(filt(brown(n), 'low', 160) * 0.0012, 0)
    birds = np.zeros((n, 2))
    for _ in range(16):
        call = bird_call(int(rng.integers(0, 3)))
        add(birds, pan(call * rng.uniform(0.05, 0.14), rng.uniform(-0.9, 0.9)), rng.uniform(0, L))
    birds = birds + convolve(birds, reverb_ir(1.2, 0.9, seed=8))[:n] * 0.25
    y = wind + leaves + hum + birds
    y = crossfade_loop(filt(y, 'high', 25), int(L * SR), fade)
    return normalize(y, -6.0)


# ----------------------------------------------------------------------------- elevator

def ding():
    n = int(2.6 * SR)
    y = np.zeros(n)
    for start, f, vel in ((0.0, 1318.5, 1.0), (0.42, 1046.5, 0.85)):
        m = n - int(start * SR)
        t = secs(m)
        tone = np.zeros(m)
        for ratio, amp, dec in ((1, 1, 1.5), (2.0, 0.32, 0.8), (2.92, 0.22, 0.55), (4.16, 0.1, 0.35), (5.43, 0.07, 0.25)):
            tone += amp * np.sin(2 * np.pi * f * ratio * t) * np.exp(-t / dec)
        a = int(0.002 * SR)
        tone[:a] *= np.linspace(0, 1, a)
        y[int(start * SR):] += tone * vel
    st = np.stack([y, y], 1)
    y = (st + convolve(st, reverb_ir(0.9, 0.7, seed=9))[:n] * 0.3)[:, 0]
    return normalize(y, -3.0)


def doors():
    n = int(1.5 * SR)
    t = secs(n)
    slide = filt(rng.standard_normal(n), 'band', [350, 1800]) * np.clip(t / 0.15, 0, 1) * np.clip((1.15 - t) / 0.25, 0, 1) * 0.5
    whine = np.sin(2 * np.pi * (430 - 40 * t) * t) * 0.02 * np.clip((1.15 - t) / 0.3, 0, 1)
    thump_t = np.clip(t - 1.17, 0, None)
    thump = (np.sin(2 * np.pi * 72 * thump_t) * np.exp(-thump_t / 0.07) + filt(rng.standard_normal(n), 'low', 900) * np.exp(-thump_t / 0.012) * 0.3) * (t >= 1.17)
    y = slide + whine + thump * 0.8
    return normalize(y, -6.0)


def hum():
    L, fade = 6.0, 1.0
    n = int((L + fade) * SR)
    t = secs(n)
    am = 1 + 0.08 * np.sin(2 * np.pi * 0.5 * t)
    tone = sum(a * np.sin(2 * np.pi * f * t) for f, a in ((100, 0.5), (200, 0.22), (300, 0.1), (400, 0.05)))
    rum = filt(brown(n), 'low', 260) * 0.01
    air = filt(rng.standard_normal(n), 'band', [600, 2400]) * 0.05
    y = tone * am * 0.6 + rum + air
    y = crossfade_loop(y, int(L * SR), fade)
    return normalize(y, -6.0)


if __name__ == '__main__':
    write('LoopLand_Theme', theme(), 5)
    write('Ambience_City', city(), 4)
    write('Water_Fountain', fountain(), 4)
    write('Water_Fall', waterfall(), 4)
    write('Lift_Ding', ding(), 5)
    write('Lift_Doors', doors(), 4)
    write('Lift_Hum', hum(), 4)
