// The promo's music, written in code: an original, bright 120 BPM pop tune in D major in the
// stomp-and-clap style of a product video — stomps, group claps, a double-tracked acoustic guitar,
// a plucked bass, marimba and a whistled hook — with every cut, click and snap placed on the
// video's own timeline (promo/scenes.js).
//
//   node promo/music.mjs            → promo/out/music.wav
//   node promo/music.mjs --mux      → also promo/out/FullWidget-promo-music.mp4 (video + music,
//                                     loudness normalised to −14 LUFS for online video)
//
// No samples and no npm packages: plucked strings (Karplus–Strong), struck bars, noise, filters and
// a room reverb — all deterministic, so every run gives the same file.
import fs from "node:fs";
import path from "node:path";
import { spawnSync } from "node:child_process";

const SR = 48000, DUR = 74, N = SR * DUR, TAU = 2 * Math.PI;
const E8 = 0.25; // an eighth note at 120 BPM
const out = path.join(import.meta.dirname, "out");
fs.mkdirSync(out, { recursive: true });

// ───────────────────────── tools ─────────────────────────
let seed = 0x9e3779b9;
const rnd = () => { seed ^= seed << 13; seed ^= seed >>> 17; seed ^= seed << 5; return ((seed >>> 0) / 4294967296) * 2 - 1; };
const mtof = (m) => 440 * 2 ** ((m - 69) / 12);
const clamp = (v, a, b) => Math.min(b, Math.max(a, v));

/** RBJ biquad: lp, hp, bp (constant peak gain) or peak (with a gain in dB). */
class Biquad {
  constructor(type, f, q = 0.707, db = 0) { this.type = type; this.db = db; this.x1 = this.x2 = this.y1 = this.y2 = 0; this.set(f, q); }
  set(f, q = this.q) {
    this.q = q;
    const w = (TAU * clamp(f, 10, SR * 0.45)) / SR, c = Math.cos(w), a = Math.sin(w) / (2 * q);
    let b0, b1, b2, a0 = 1 + a, a1 = -2 * c, a2 = 1 - a;
    if (this.type === "lp") { b0 = (1 - c) / 2; b1 = 1 - c; b2 = b0; }
    else if (this.type === "hp") { b0 = (1 + c) / 2; b1 = -(1 + c); b2 = b0; }
    else if (this.type === "bp") { b0 = a; b1 = 0; b2 = -a; }
    else { const A = 10 ** (this.db / 40); b0 = 1 + a * A; b1 = -2 * c; b2 = 1 - a * A; a0 = 1 + a / A; a2 = 1 - a / A; }
    this.b0 = b0 / a0; this.b1 = b1 / a0; this.b2 = b2 / a0; this.a1 = a1 / a0; this.a2 = a2 / a0;
  }
  run(x) {
    const y = this.b0 * x + this.b1 * this.x1 + this.b2 * this.x2 - this.a1 * this.y1 - this.a2 * this.y2;
    this.x2 = this.x1; this.x1 = x; this.y2 = this.y1; this.y1 = y;
    return y;
  }
}

// buses, mixed at the end; SEND feeds the room reverb
const bus = () => [new Float32Array(N), new Float32Array(N)];
const PERC = bus(), GTR = bus(), BASS = bus(), KEYS = bus(), LEAD = bus(), FX = bus(), SEND = bus();
const BUSES = { perc: PERC, guitar: GTR, bass: BASS, keys: KEYS, whistle: LEAD, fx: FX };
const stomps = []; // [time, gain]: the band ducks a little under each

/** Adds a mono voice at time t: fn(k, i) gives sample i (k seconds since the start). */
function voice(target, t, len, fn, { pan = 0, gain = 1, send = 0 } = {}) {
  const s0 = Math.round(t * SR), n = Math.round(len * SR);
  const gl = gain * Math.cos(((pan + 1) * Math.PI) / 4), gr = gain * Math.sin(((pan + 1) * Math.PI) / 4);
  for (let i = 0; i < n; i++) {
    const j = s0 + i;
    const v = fn(i / SR, i);
    if (j < 0 || j >= N) continue;
    target[0][j] += v * gl; target[1][j] += v * gr;
    if (send) { SEND[0][j] += v * gl * send; SEND[1][j] += v * gr * send; }
  }
}

// ───────────────────────── strings ─────────────────────────
/**
 * A plucked string (Karplus–Strong): a burst of noise circulates in a delay line one period long,
 * losing its highs each trip. An all-pass sets the exact length, so chords stay in tune.
 * stop: seconds after which the string is damped (the next strum lands on it).
 */
function string(target, t, m, { g = 0.2, len = 2, stop = Infinity, t60 = 2.5, s = 0.4, tone = 0.6, pick = 0.15, pan = 0, send = 0.1 } = {}) {
  const f = mtof(m), P = SR / f;
  const n = Math.floor(P - s - 0.1), d = P - s - n, C = (1 - d) / (1 + d);
  const loss = 0.001 ** (1 / (t60 * f));
  const ex = new Float32Array(n), buf = new Float32Array(n);
  let lp = 0, mean = 0;
  for (let i = 0; i < n; i++) { lp += (rnd() - lp) * tone; ex[i] = lp; }
  const pk = Math.max(1, Math.round(pick * n)); // where the pick hits: a comb that thins some overtones
  for (let i = 0; i < n; i++) { buf[i] = ex[i] - (i >= pk ? ex[i - pk] : 0); mean += buf[i]; }
  mean /= n;
  for (let i = 0; i < n; i++) buf[i] -= mean;
  let idx = 0, prev = 0, apx = 0, apy = 0;
  voice(target, t, Math.min(len, stop + 0.1), (k) => {
    const y = buf[idx];
    const l = (1 - s) * y + s * prev; prev = y;
    const a = C * l + apx - C * apy; apx = l; apy = a;
    buf[idx] = a * loss;
    idx = idx + 1 === n ? 0 : idx + 1;
    return k > stop ? y * Math.exp(-(k - stop) / 0.015) : y;
  }, { gain: g, pan, send });
}

/** One strum of the acoustic guitar, double-tracked left and right. Down: low → high; up: the top three strings, high → low. */
function strum(t, ch, up, v, ringTop, ringLow) {
  const notes = up ? ch.gtr.slice(-3).reverse() : ch.gtr;
  [[-0.65, 0, 0.58], [0.65, 0.007, 0.66]].forEach(([pan, off, tone]) => {
    notes.forEach((m, i) => {
      const at = t + off + i * (up ? 0.008 : 0.011) + rnd() * 0.0015;
      const top = up || i >= notes.length - 3;
      const stop = Math.max(0.03, (top ? ringTop : ringLow) - (at - t));
      string(GTR, at, m, { g: 0.18 * v * (up ? 0.7 : 1), len: stop + 0.12, stop, t60: 2.6, s: 0.38, tone: up ? tone + 0.12 : tone, pick: 0.12 + Math.abs(rnd()) * 0.06, pan: pan * (0.5 + (0.5 * (i + 1)) / notes.length), send: 0.1 });
    });
  });
}

/** A plucked electric bass: a dark Karplus–Strong string with a sine underneath for weight. */
function bassNote(t, m, len, g = 0.5) {
  string(BASS, t, m, { g, len: len + 0.12, stop: len, t60: 1.6, s: 0.5, tone: 0.25, pick: 0.3, send: 0 });
  const f = mtof(m);
  voice(BASS, t, len + 0.08, (k) => Math.sin(TAU * f * k) * Math.min(1, k / 0.006) * Math.exp(-k / 0.9) * (k > len ? Math.exp(-(k - len) / 0.012) : 1), { gain: g * 0.55 });
}

// ───────────────────────── mallets ─────────────────────────
/** Marimba: a wooden bar's fundamental, its 4th overtone and a soft mallet. */
function marimba(t, m, g = 0.12, pan = 0) {
  const f = mtof(m), d = clamp(0.5 * (330 / f) ** 0.6, 0.12, 0.9), lp = new Biquad("lp", 2500);
  voice(KEYS, t, d * 5, (k) => Math.min(1, k / 0.0012) * (
    Math.sin(TAU * f * k) * Math.exp(-k / d)
    + 0.3 * Math.sin(TAU * 3.93 * f * k) * Math.exp(-k / (d * 0.15))
    + 0.08 * Math.sin(TAU * 9.6 * f * k) * Math.exp(-k / 0.01)
    + lp.run(rnd()) * 0.3 * Math.exp(-k / 0.002)), { gain: g, pan, send: 0.22 });
}
/** Glockenspiel: a steel bar's out-of-tune overtones ring out over a long fundamental. */
function glock(t, m, g = 0.1, pan = 0) {
  const f = mtof(m);
  voice(KEYS, t, 2.2, (k) => Math.min(1, k / 0.0008) * (
    Math.sin(TAU * f * k) * Math.exp(-k / 0.9)
    + 0.4 * Math.sin(TAU * 2.76 * f * k) * Math.exp(-k / 0.25)
    + 0.15 * Math.sin(TAU * 5.4 * f * k) * Math.exp(-k / 0.08)
    + (k < 0.002 ? rnd() * 0.3 : 0)), { gain: g, pan, send: 0.4 });
}

// ───────────────────────── the whistle ─────────────────────────
/**
 * A whistled line: notes [[start, midi, length, glideTo?], …] as one breath. Each fresh note scoops
 * up into pitch; joined notes slide and re-articulate; long notes grow a vibrato; a little breath
 * noise follows the pitch.
 */
function whistle(notes, g = 0.15) {
  const t0 = notes[0][0] - 0.05, t1 = Math.max(...notes.map(([s, , l]) => s + l)) + 0.3, n = Math.round((t1 - t0) * SR);
  const tgt = new Float32Array(n).fill(-1), age = new Float32Array(n), fresh = new Uint8Array(n);
  notes.forEach(([s, m, l, to = m], i) => {
    const prevEnd = i ? notes[i - 1][0] + notes[i - 1][2] : -9;
    const a = Math.round((s - t0) * SR), b = Math.min(n, Math.round((s + l - t0) * SR));
    for (let j = a; j < b; j++) { const p = (j - a) / (b - a); tgt[j] = m + (to - m) * p * p * (3 - 2 * p); age[j] = (j - a) / SR; }
    if (s - prevEnd > 0.03) fresh[a] = 1;
  });
  const bp = new Biquad("bp", 1000, 5), air = new Biquad("hp", 5000);
  const kp = 1 - Math.exp(-1 / (SR * 0.028)), ka = 1 - Math.exp(-1 / (SR * 0.012)), kr = 1 - Math.exp(-1 / (SR * 0.05));
  let p = notes[0][1] - 0.7, amp = 0, ph = 0, vph = 0, drift = 0;
  voice(LEAD, t0, (n - 1) / SR, (k, j) => {
    const on = tgt[j] >= 0;
    if (fresh[j]) p = tgt[j] - 0.7;
    if (on) p += (tgt[j] - p) * kp;
    amp += ((on ? 1 : 0) - amp) * (on ? ka : kr);
    drift = drift * 0.9995 + rnd() * 0.0006;
    const vib = on ? 0.16 * clamp((age[j] - 0.15) / 0.25, 0, 1) : 0;
    vph += (5.4 + 0.3 * Math.sin(k * 0.7)) / SR;
    const f = mtof(p + vib * Math.sin(TAU * vph) + drift);
    ph += f / SR;
    if (j % 16 === 0) bp.set(f, 5);
    const a = amp * (on ? 1 - 0.4 * Math.exp(-age[j] / 0.02) : 1) * (1 + 0.3 * vib * Math.sin(TAU * vph));
    return (Math.sin(TAU * ph) + 0.03 * Math.sin(2 * TAU * ph) + bp.run(rnd()) * 0.9 + air.run(rnd()) * 0.02) * a;
  }, { gain: g, pan: 0.05, send: 0.32 });
}

// ───────────────────────── percussion ─────────────────────────
/** A stomp on a wooden stage: two feet a few ms apart, a low thump, the boards ringing. */
function stomp(t, g = 0.9) {
  stomps.push([t, g]);
  [[0, 1, -0.12, 125], [0.008, 0.75, 0.14, 105]].forEach(([o, gg, pan, top]) => {
    let ph = 0;
    const lp = new Biquad("lp", 900, 0.7), bp = new Biquad("bp", 240, 1.8);
    voice(PERC, t + o, 0.7, (k) => {
      ph += (50 + top * Math.exp(-k / 0.028)) / SR;
      return (Math.sin(TAU * ph) * Math.exp(-k / 0.17) + lp.run(rnd()) * 0.9 * Math.exp(-k / 0.02) + bp.run(rnd()) * 1.4 * Math.exp(-k / 0.09)) * Math.min(1, k / 0.0012);
    }, { gain: g * gg * 0.6, pan, send: 0.22 });
  });
}
/** A group of people clapping: each clap a little early or late, a little brighter or darker. */
function claps(t, g = 0.5, n = 6) {
  for (let c = 0; c < n; c++) {
    const o = -0.004 + Math.abs(rnd()) * 0.02, f = 950 + Math.abs(rnd()) * 1250, d2 = 0.004 + Math.abs(rnd()) * 0.004;
    const bp = new Biquad("bp", f, 1.3), hp = new Biquad("hp", 500);
    voice(PERC, t + o, 0.25, (k) => {
      const e = (k < 0.001 ? k / 0.001 : Math.exp(-(k - 0.001) / 0.008)) + (k > d2 ? 0.8 * Math.exp(-(k - d2) / 0.022) : 0);
      return hp.run(bp.run(rnd())) * e * 2.4;
    }, { gain: g / Math.sqrt(n), pan: rnd() * 0.7, send: 0.38 });
  }
}
function clapRoll(from, to, g = 0.4) { // claps speeding up into `to`
  for (let t = from; t < to - 0.01;) { const p = (t - from) / (to - from); claps(t, g * (0.35 + 0.65 * p), 3); t += p < 0.5 ? 0.125 : 0.0625; }
}
function tamb(t, g = 0.1, pan = 0.4) {
  const hp = new Biquad("hp", 6500, 0.7), fr = [5400, 6700, 7900, 9300, 11200].map((f) => f * (1 + rnd() * 0.015));
  const bursts = [0, 0.012 + Math.abs(rnd()) * 0.006, 0.03 + Math.abs(rnd()) * 0.01];
  voice(PERC, t - 0.004, 0.3, (k) => {
    let e = 0, ring = 0;
    bursts.forEach((b, i) => { if (k >= b) e += Math.exp(-(k - b) / (i === 2 ? 0.07 : 0.018)) / (1 + i * 0.6); });
    for (const f of fr) ring += Math.sin(TAU * f * k);
    return (hp.run(rnd()) * 0.8 + ring * 0.06 * Math.exp(-k / 0.09)) * e * Math.min(1, k / 0.0015);
  }, { gain: g, pan, send: 0.2 });
}
function shaker(t, g = 0.05, pan = -0.35) {
  const hp = new Biquad("hp", 5000, 0.7), bp = new Biquad("bp", 8500, 1.2);
  voice(PERC, t - 0.01, 0.09, (k) => (hp.run(rnd()) * 0.5 + bp.run(rnd())) * (k < 0.012 ? k / 0.012 : Math.exp(-(k - 0.012) / 0.03)), { gain: g, pan, send: 0.12 });
}
function snap(t, g = 0.3, pan = 0.25) { // a finger snap
  const bp = new Biquad("bp", 2400, 3), hp = new Biquad("hp", 1500);
  voice(PERC, t, 0.15, (k) => (bp.run(rnd()) * 2.2 + hp.run(rnd()) * 0.35 + Math.sin(TAU * 1900 * k) * 0.25 * Math.exp(-k / 0.006)) * Math.exp(-k / 0.011) * Math.min(1, k / 0.0004), { gain: g, pan, send: 0.35 });
}
function boom(t, g = 0.5) { // a big floor tom under the important hits
  stomps.push([t, g]);
  let ph = 0;
  const lp = new Biquad("lp", 400);
  voice(PERC, t, 2.0, (k) => { ph += (52 + 60 * Math.exp(-k / 0.05)) / SR; return Math.sin(TAU * ph) * Math.exp(-k / 0.55) + lp.run(rnd()) * Math.exp(-k / 0.04) * 0.5; }, { gain: g, send: 0.25 });
}
function crash(t, g = 0.16, pan = -0.2) {
  const hp = new Biquad("hp", 3500, 0.6), bp = new Biquad("bp", 6500, 0.8), fr = [3150, 4320, 5170, 6910, 8030, 9680];
  voice(PERC, t, 2.6, (k) => {
    let m = 0;
    for (const f of fr) m += Math.sign(Math.sin(TAU * f * k));
    return (hp.run(rnd()) + bp.run(m * 0.15)) * Math.exp(-k / 0.8) * Math.min(1, k / 0.002);
  }, { gain: g, pan, send: 0.3 });
}
function swell(t, len = 1, g = 0.15) { // a cymbal played backwards, swelling into t
  const hp = new Biquad("hp", 2500, 0.6);
  voice(PERC, t - len, len, (k) => hp.run(rnd()) * (k / len) ** 2.5, { gain: g, send: 0.35, pan: 0.1 });
}

// ───────────────────────── interface sounds ─────────────────────────
/** Air rushing past: noise through a band-pass that sweeps, louder in the middle. */
function whoosh(t, len = 0.45, g = 0.14, up = true, pan = 0) {
  const bp = new Biquad("bp", 600, 1.4);
  voice(FX, t - len * 0.6, len, (k, i) => {
    const p = k / len;
    if (i % 32 === 0) bp.set(up ? 300 * 12 ** p : 3600 / 12 ** p, 1.4);
    return bp.run(rnd()) * Math.sin(Math.PI * p) ** 2 * 1.4;
  }, { gain: g, pan, send: 0.2 });
}
function click(t, g = 0.18) {
  const hp = new Biquad("hp", 2500);
  voice(FX, t, 0.05, (k) => (Math.sin(TAU * 2600 * k) * 0.6 + hp.run(rnd()) * 0.6) * Math.exp(-k / 0.008), { gain: g, send: 0.1, pan: 0.1 });
}
function tick(t, g = 0.06, f = 1800, pan = 0) {
  voice(FX, t, 0.04, (k) => Math.sin(TAU * f * k) * Math.exp(-k / 0.01), { gain: g, pan });
}
function key(t, g = 0.05, pan = 0) { // a keyboard key
  const bp = new Biquad("bp", 3200 + rnd() * 600, 1.5);
  voice(FX, t, 0.05, (k) => bp.run(rnd()) * 1.5 * Math.exp(-k / 0.005) + Math.sin(TAU * 420 * k) * 0.3 * Math.exp(-k / 0.012), { gain: g, pan });
}

// ───────────────────────── the score ─────────────────────────
// D major, plain triads. The tune runs I–V–vi–IV (D A Bm G), a chord a bar; the quiet part climbs
// IV–V–vi, then walks home IV–I/3–ii–V; the end is D A G A D.
const CH = {
  D:  { root: 38, gtr: [50, 57, 62, 66], tones: [62, 66, 69, 74, 78] },
  A:  { root: 33, gtr: [45, 52, 57, 61, 64], tones: [61, 64, 69, 73, 76] },
  Bm: { root: 35, gtr: [47, 54, 59, 62, 66], tones: [62, 66, 71, 74, 78] },
  G:  { root: 31, gtr: [43, 47, 50, 55, 59, 67], tones: [62, 67, 71, 74, 79] },
  Em: { root: 40, gtr: [40, 47, 52, 55, 59, 64], tones: [64, 67, 71, 76, 79] },
  DF: { root: 42, gtr: [42, 50, 57, 62, 66], tones: [62, 66, 69, 74, 78] }, // D over F#
};
const TL = [[0, "D"], [2.5, "A"], [3, "Bm"], [4, "G"], [4.5, "A"]];
const LOOP = ["D", "A", "Bm", "G"];
for (let k = 0; 5 + 2 * k < 59; k++) TL.push([5 + 2 * k, LOOP[k % 4]]);
TL.push([59, "G"], [59.5, "A"], [60, "Bm"], [61, "G"], [63, "DF"], [64, "Em"], [65, "A"], [66, "D"], [68, "A"], [70, "G"], [71, "A"], [72, "D"]);
const chordAt = (t) => { let c = "D"; for (const [s, n] of TL) if (t >= s - 1e-6) c = n; return CH[c]; };
const within = (t, ranges) => { for (const [a, b, g = 1] of ranges) if (t >= a - 1e-6 && t < b - 1e-6) return g; return 0; };
// bars run from the drop at 5 s, and again from the return at 66 s (59–66 is the quiet part)
const barOf = (t) => { const a = t >= 66 - 1e-6 ? 66 : 5, n = Math.floor((t - a + 1e-6) / 2); return { start: a + n * 2, n }; };

const FULL = [[5, 41], [45, 59], [66, 72]]; // the whole band
const LITE = [[41, 45]];                    // writing a widget: guitar, snaps and typing
const MAR = [[5, 13, 1.35], [13, 21, 0.5], [21, 41, 0.9], [45, 53, 0.5], [53, 59, 1], [66, 72, 0.45]];
const STRUM = { 0: [false, 1], 2: [false, 0.8], 3: [true, 0.55], 5: [true, 0.6], 6: [false, 0.85], 7: [true, 0.5] }; // D . D U . U D U
const RIFF = [0, 2, 3, 1, 2, 4, 2, 3], RIFF_V = [1, 0.6, 0.65, 0.95, 0.6, 0.65, 0.95, 0.6]; // accents 3 + 3 + 2

// the whistled tune, in eighth notes from the start of its phrase: [position, midi, length]
const HOOK_A = [[0, 81, 1], [1, 86, 1], [2, 90, 2], [4, 88, 1], [5, 86, 3], [8, 81, 1], [9, 85, 1], [10, 88, 2], [12, 86, 1], [13, 85, 3],
  [16, 83, 1], [17, 86, 1], [18, 90, 2], [20, 93, 1], [21, 90, 2], [23, 88, 1], [24, 86, 2], [26, 83, 1], [27, 86, 1], [28, 88, 4]];
const HOOK_B = [[0, 81, 1], [1, 86, 1], [2, 90, 2], [4, 93, 1], [5, 95, 1], [6, 93, 2], [8, 88, 1], [9, 90, 1], [10, 88, 2], [12, 85, 1], [13, 81, 3],
  [16, 83, 1], [17, 86, 1], [18, 90, 2], [20, 93, 1], [21, 90, 1], [22, 86, 1], [23, 88, 1], [24, 86, 2], [26, 83, 1], [27, 86, 1], [28, 83, 1], [29, 86, 3]];
const HOOK_END = [[0, 81, 1], [1, 86, 1], [2, 90, 2], [4, 93, 1], [5, 95, 1], [6, 93, 2], [8, 88, 1], [9, 90, 1], [10, 88, 2], [12, 85, 1], [13, 81, 3],
  [16, 83, 1], [17, 86, 1], [18, 91, 2], [20, 93, 1], [21, 88, 2], [23, 85, 1], [24, 86, 8]];
const line = (t0, notes) => notes.map(([p, m, l]) => [t0 + p * E8, m, l * E8]);

const strums = []; // [time, chord, up, velocity, ring?]
const basses = []; // [time, midi, length, velocity]

// ── 0–2: the logo's four pieces land, the F pulses, then bursts ──
[[0.25, 86], [0.5, 90], [0.75, 93]].forEach(([t, m], i) => { glock(t, m, 0.1, (i - 1) * 0.3); marimba(t, m - 12, 0.09); stomp(t, 0.3 + i * 0.08); });
glock(1.0, 98, 0.12); marimba(1.0, 86, 0.1); stomp(1.0, 0.85); claps(1.0, 0.45);
strums.push([1.0, CH.D, false, 1.05]); basses.push([1.0, 38, 0.95, 0.9]);
whoosh(1.45, 0.5, 0.12); swell(2.0, 0.7, 0.1);
// ── 2–5: 桌面 / 也可以 / 很好看。, then a run-up of claps into the drop ──
[[2.0, "D"], [2.5, "A"], [3.0, "Bm"], [4.0, "G"], [4.5, "A"]].forEach(([t, c], i) => {
  stomp(t, i < 3 ? 0.85 : 0.65);
  if (i < 3) claps(t, 0.5);
  strums.push([t, CH[c], false, 1]); basses.push([t, CH[c].root, i === 2 ? 0.95 : 0.45, 0.9]);
});
for (let t = 3.0; t < 5 - 1e-6; t += 0.125) shaker(t, 0.012 + (0.03 * (t - 3)) / 2);
clapRoll(4.0, 5.0, 0.42); swell(5.0, 1.0, 0.14);

// ── the band: 5–41, 45–59 and 66–72, with a lighter groove for the code (41–45) ──
for (let t = 5; t < 72 - 1e-6; t += E8) {
  if (t >= 59 - 1e-6 && t < 66 - 1e-6) continue;
  const { start, n } = barOf(t), pos = Math.round((t - start) / E8), ch = chordAt(t), nextRoot = chordAt(start + 2).root;
  if (within(t, FULL)) {
    if (pos === 0) stomp(t, 0.9);
    if (pos === 3) stomp(t, 0.5);
    if (pos === 4) stomp(t, 0.8);
    if (pos === 7 && n % 4 === 3) stomp(t, 0.5);
    if (pos === 2 || pos === 6) { claps(t, 0.5); tamb(t, 0.09); }
    shaker(t, 0.028); shaker(t + 0.125, 0.045);
    if (STRUM[pos]) strums.push([t, ch, ...STRUM[pos]]);
    if (pos === 0) basses.push([t, ch.root, 0.45, 1]);
    if (pos === 3) basses.push([t, ch.root, 0.2, 0.8]);
    if (pos === 4) basses.push([t, ch.root, 0.45, 0.95]);
    if (pos === 6) basses.push([t, ch.root + 7, 0.2, 0.8]);
    if (pos === 7) basses.push([t, nextRoot - 1, 0.2, 0.75]); // a half step below the next chord
  } else if (within(t, LITE)) {
    if (pos === 0 || pos === 4) strums.push([t, ch, false, 0.6]);
    if (pos === 7) strums.push([t, ch, true, 0.35]);
    if (pos === 2 || pos === 6) snap(t, 0.3, pos === 2 ? 0.3 : -0.3);
    if (pos === 0) basses.push([t, ch.root, 1.2, 0.6]);
    shaker(t, 0.02); shaker(t + 0.125, 0.03);
  }
  const mg = within(t, MAR);
  if (mg) marimba(t, ch.tones[RIFF[pos]], 0.11 * mg * RIFF_V[pos], pos % 2 ? 0.3 : -0.3);
}

// ── 5–8: the drop; 38 counted up; the dive ──
crash(5.0, 0.16); boom(5.0, 0.35);
[74, 76, 78, 79, 81, 83, 85, 86, 88, 90, 91, 93].forEach((m, i) => glock(5.3 + i * 0.09, m, 0.035 + i * 0.002, 0.3));
whoosh(7.6, 0.7, 0.16);
// ── 8–23: six categories (whip cuts), the whistle comes in, calculator keys, to-do ticks ──
[8, 10.5, 13, 15.5, 18, 20.5].forEach((t, i) => { whoosh(t, 0.42, 0.12, i % 2 === 0, i % 2 ? 0.3 : -0.3); tamb(t, 0.12, i % 2 ? -0.3 : 0.3); });
glock(8.0, 88, 0.08); glock(10.5, 90, 0.08);
whistle(line(13, HOOK_A), 0.17);
for (let i = 0; i < 10; i++) key(15.95 + i * 0.12, 0.05, 0.2);
marimba(16.55, 81, 0.09, 0.3); marimba(17.05, 74, 0.09, -0.3);
// ── 23–27: three themes, one per beat ──
crash(23.0, 0.1);
[[23.5, 88], [24, 93], [24.5, 97], [25, 86], [25.5, 90], [26, 95]].forEach(([t, m], i) => glock(t, m, 0.09, ((i % 3) - 1) * 0.4));
// ── 27–31: colour, radius, opacity ──
whoosh(27.0, 0.4, 0.11, false);
[[27.6, 79], [28.1, 83], [28.6, 86], [29.1, 81]].forEach(([t, m], i) => marimba(t, m, 0.11, i % 2 ? 0.3 : -0.3));
[29.2, 29.6, 30.1, 30.45, 30.8].forEach((t) => tick(t, 0.06, 1900));
// ── 31–33: resize, the content grows (a slide whistle up an octave) ──
whoosh(31.0, 0.4, 0.11); click(31.3, 0.16);
whistle([[31.35, 81, 1.2, 93]], 0.11);
// ── 33–36: drag and snap ──
[33.45, 34.75].forEach((t) => click(t, 0.15));
snap(34.3, 0.4, 0.2); glock(34.3, 90, 0.06);
snap(35.45, 0.4, -0.2); glock(35.45, 95, 0.06);
// ── 36–39: layers ──
whoosh(36.0, 0.4, 0.11, false);
[[36.4, 79], [36.9, 83], [37.35, 86]].forEach(([t, m], i) => marimba(t, m, 0.12, (i - 1) * 0.4));
click(38.05, 0.17);
// ── 39–42: full screen (the music is muffled while the game fills the screen) ──
whoosh(39.0, 0.4, 0.12); whoosh(39.6, 0.5, 0.16, false); whoosh(41.35, 0.5, 0.14, true);
// ── 42–48: write a widget: typing over a light groove; the band is back when the card goes live ──
whoosh(42.0, 0.4, 0.1);
[[42.35, 63], [43.75, 84], [45.45, 62]].forEach(([at, len]) => { for (let i = 0; i < len; i += 2) key(at + i / 62, 2.5 * (0.035 + (0.02 * ((i * 7) % 3)) / 2), ((i % 4) - 1.5) * 0.15); });
marimba(43.47, 86, 0.1); // the card appears
clapRoll(44.5, 45.0, 0.32); swell(45.0, 1.0, 0.12); crash(45.0, 0.14); boom(45.0, 0.3);
glock(46.45, 98, 0.06); // it turns yellow
whistle(line(45, HOOK_B), 0.17);
// ── 48–51: a .fwz; double-click; allow; on the desk ──
whoosh(48.0, 0.5, 0.13, false);
[49.05, 49.22, 50.15].forEach((t) => click(t, 0.18));
marimba(49.45, 78, 0.09); marimba(50.4, 74, 0.1);
// ── 51–54: the market ──
whoosh(51.0, 0.5, 0.13); click(52.35, 0.18); whoosh(53.45, 0.6, 0.11, false); marimba(53.75, 78, 0.1);
// ── 54–57: 0.4 % ──
whoosh(54.0, 0.4, 0.11);
for (let i = 0; i < 10; i++) tick(54.35 + i * 0.1, 0.045, 1200 + i * 90, 0.25);
// ── 57–59: three languages, into the quiet part ──
[[57, 86], [57.5, 90], [58, 95], [58.5, 98]].forEach(([t, m], i) => glock(t, m, 0.09, (i - 1.5) * 0.25));
clapRoll(58.0, 59.0, 0.35); swell(59.0, 1.0, 0.14);
// ── 59–66: free, open source: three big hits, a finger-picked guitar, the beat creeping back ──
[[59.0, "G"], [59.5, "A"], [60.0, "Bm"]].forEach(([t, c], i) => {
  stomp(t, 0.95); boom(t, 0.4); claps(t, 0.5);
  strums.push([t, CH[c], false, 1.1, i === 2 ? 1.0 : 0.5]);
  basses.push([t, CH[c].root + 12, i === 2 ? 1.0 : 0.48, 0.9]);
});
[[60.5, 86], [60.75, 90], [61.0, 95]].forEach(([t, m], i) => glock(t, m, 0.09, (i - 1) * 0.4));
glock(61.25, 98, 0.06);
const PICK = [0, 3, 2, 4, 1, 3, 2, 4];
for (let t = 61; t < 66 - 1e-6; t += E8) {
  const ch = chordAt(t), i = Math.round((t - 61) / E8) % 8;
  string(GTR, t, ch.gtr[Math.min(PICK[i], ch.gtr.length - 1)], { g: 0.2 + (0.08 * (t - 61)) / 5, len: 1.6, t60: 2.4, s: 0.4, tone: 0.5, pick: 0.15, pan: i % 2 ? 0.3 : -0.3, send: 0.3 });
}
[[61, 43, 1.95], [63, 42, 0.95], [64, 40, 0.95], [65, 45, 0.95]].forEach(([t, m, l]) => basses.push([t, m, l, 0.6]));
for (let t = 62; t < 66 - 1e-6; t += 0.125) shaker(t, 0.01 + (0.035 * (t - 62)) / 4);
[63.5, 64.5].forEach((t) => claps(t, 0.3));
[63, 64, 64.5].forEach((t, i) => stomp(t, 0.45 + i * 0.1));
clapRoll(65.0, 66.0, 0.45); swell(66.0, 1.5, 0.16);
// ── 66–74: everything comes back, the whistle sings the tune home, folding into the logo ──
crash(66.0, 0.18); boom(66.0, 0.45);
whistle(line(66, HOOK_END), 0.18);
whoosh(68.4, 1.0, 0.12, false); stomp(68.5, 0.6); tamb(68.5, 0.14);
[68.65, 69.5, 70.5].forEach((t) => glock(t, 98, 0.045));
clapRoll(71.5, 72.0, 0.35);
// the last chord rings out
crash(72.0, 0.18); boom(72.0, 0.5); stomp(72.0, 1.0); claps(72.0, 0.6); tamb(72.0, 0.14);
strums.push([72.0, CH.D, false, 1.15, 2.0]); basses.push([72.0, 38, 1.9, 1]);
glock(72.0, 86, 0.1); glock(72.0, 93, 0.07); marimba(72.0, 74, 0.12);

// play the guitar: a string rings until the next strum lands on it
strums.sort((a, b) => a[0] - b[0]);
strums.forEach(([t, ch, up, v, ring], i) => {
  const next = strums[i + 1], nextDown = strums.slice(i + 1).find((s) => !s[2]);
  strum(t, ch, up, v, Math.min(ring ?? (next ? next[0] - t : 2), 2.5), Math.min(ring ?? (nextDown ? nextDown[0] - t : 2), 2.5));
});
basses.forEach(([t, m, len, v]) => bassNote(t, m, len, 0.5 * v));

// ───────────────────────── mixing ─────────────────────────
function chain(b, filters) { for (let c = 0; c < 2; c++) { const fl = filters(); for (let j = 0; j < N; j++) { let v = b[c][j]; for (const f of fl) v = f.run(v); b[c][j] = v; } } }
// the guitar's body: a little warmth and a little sparkle; the bass kept round
chain(GTR, () => [new Biquad("hp", 75), new Biquad("peak", 220, 1, 2), new Biquad("peak", 3200, 0.9, 2.5), new Biquad("lp", 10000)]);
chain(BASS, () => [new Biquad("hp", 35), new Biquad("lp", 2000)]);

// how loud each part is, K-weighted like a loudness meter (BS.1770 at 48 kHz, ungated), to keep the balance honest
function loud(b, a, z) {
  let s = 0;
  for (let c = 0; c < 2; c++) {
    let x1 = 0, x2 = 0, y1 = 0, y2 = 0, u1 = 0, u2 = 0, w1 = 0, w2 = 0;
    for (let j = Math.round(a * SR); j < Math.round(z * SR); j++) {
      const x = b[c][j];
      const y = 1.53512485958697 * x - 2.69169618940638 * x1 + 1.19839281085285 * x2 + 1.69065929318241 * y1 - 0.73248077421585 * y2;
      x2 = x1; x1 = x; y2 = y1; y1 = y;
      const w = y - 2 * u1 + u2 + 1.99004745483398 * w1 - 0.99007225036621 * w2;
      u2 = u1; u1 = y; w2 = w1; w1 = w;
      s += w * w;
    }
  }
  return -0.691 + 10 * Math.log10(s / ((z - a) * SR) + 1e-12);
}
for (const [a, z] of [[5, 13], [13, 21], [41, 45], [45, 53], [61, 66]]) console.log(`${a}–${z} s: ` + Object.entries(BUSES).map(([k, b]) => `${k} ${loud(b, a, z).toFixed(1)}`).join(", "));

// the band ducks a little under each stomp, so the stomps punch
const duck = new Float32Array(N).fill(1);
for (const [t, g] of stomps) { const s0 = Math.round(t * SR); for (let i = 0; i < SR * 0.25 && s0 + i < N; i++) duck[s0 + i] = Math.min(duck[s0 + i], 1 - 0.3 * g * Math.exp(-i / SR / 0.07)); }
const mix = [new Float32Array(N), new Float32Array(N)];
for (let c = 0; c < 2; c++) for (let j = 0; j < N; j++) mix[c][j] = PERC[c][j] + FX[c][j] * 1.4 + LEAD[c][j] + (GTR[c][j] + BASS[c][j] + KEYS[c][j]) * duck[j];

// a room (Freeverb-style: combs into all-passes), fed without its lows so it stays clear
function reverb(x, offset) {
  const hp = new Biquad("hp", 220);
  const combs = [1557, 1617, 1491, 1422, 1277, 1356, 1188, 1116].map((d) => ({ buf: new Float32Array(d + offset), i: 0, f: 0 }));
  const alls = [556, 441, 341, 225].map((d) => ({ buf: new Float32Array(d + offset), i: 0 }));
  const y = new Float32Array(N), fb = 0.83, damp = 0.3;
  for (let j = 0; j < N; j++) {
    const xin = hp.run(x[j]);
    let s = 0;
    for (const c of combs) { const o = c.buf[c.i]; c.f = o * (1 - damp) + c.f * damp; c.buf[c.i] = xin + c.f * fb; c.i = (c.i + 1) % c.buf.length; s += o; }
    s *= 0.12;
    for (const a of alls) { const o = a.buf[a.i]; const v = -s + o; a.buf[a.i] = s + o * 0.5; a.i = (a.i + 1) % a.buf.length; s = v; }
    y[j] = s;
  }
  return y;
}
const rv = [reverb(SEND[0], 0), reverb(SEND[1], 23)];
for (let c = 0; c < 2; c++) for (let j = 0; j < N; j++) mix[c][j] += rv[c][j] * 0.8;

// full screen (39.55–41.35 s): everything muffled, as if through a wall
for (let c = 0; c < 2; c++) {
  const lp = new Biquad("lp", 18000, 0.8);
  for (let j = Math.round(39.3 * SR); j < Math.round(41.8 * SR); j++) {
    const t = j / SR;
    const m = t < 39.55 ? (t - 39.3) / 0.25 : t < 41.35 ? 1 : 1 - (t - 41.35) / 0.45;
    if (j % 32 === 0) lp.set(18000 * (1 - clamp(m, 0, 1)) + 420 * clamp(m, 0, 1), 0.8);
    mix[c][j] = lp.run(mix[c][j]);
  }
}
// the end: a short fade after the last chord
for (let c = 0; c < 2; c++) for (let j = Math.round(73.0 * SR); j < N; j++) mix[c][j] *= Math.max(0, (N - j) / (SR * 1.0));

// gain staging, then a gentle limiter: only the loudest transients reach the soft clip
let raw = 0, sq = 0;
for (let c = 0; c < 2; c++) for (let j = 0; j < N; j++) { raw = Math.max(raw, Math.abs(mix[c][j])); sq += mix[c][j] ** 2; }
const pre = 1.15 / raw;
let peak = 0;
for (let c = 0; c < 2; c++) for (let j = 0; j < N; j++) { const v = Math.tanh(mix[c][j] * pre); mix[c][j] = v; peak = Math.max(peak, Math.abs(v)); }
const norm = 0.891 / peak;
const rmsDb = 20 * Math.log10(Math.sqrt(sq / (2 * N)) * pre * norm);

// 24-bit stereo WAV
const data = Buffer.alloc(N * 2 * 3);
for (let j = 0, o = 0; j < N; j++) for (let c = 0; c < 2; c++, o += 3) data.writeIntLE(Math.round(clamp(mix[c][j] * norm, -1, 1) * 8388607), o, 3);
const head = Buffer.alloc(44);
head.write("RIFF", 0); head.writeUInt32LE(36 + data.length, 4); head.write("WAVE", 8); head.write("fmt ", 12);
head.writeUInt32LE(16, 16); head.writeUInt16LE(1, 20); head.writeUInt16LE(2, 22); head.writeUInt32LE(SR, 24);
head.writeUInt32LE(SR * 6, 28); head.writeUInt16LE(6, 32); head.writeUInt16LE(24, 34); head.write("data", 36); head.writeUInt32LE(data.length, 40);
const wav = path.join(out, "music.wav");
fs.writeFileSync(wav, Buffer.concat([head, data]));
console.log(`${path.relative(process.cwd(), wav)}: ${DUR} s, ${strums.length} strums, ${stomps.length} stomps, raw peak ${raw.toFixed(2)}, RMS ${rmsDb.toFixed(1)} dBFS`);

if (process.argv.includes("--mux")) {
  const root = path.resolve(import.meta.dirname, "..");
  const ff = [path.join(root, ".tools", "ffmpeg", "bin", "ffmpeg.exe")].find((p) => fs.existsSync(p)) || "ffmpeg";
  const video = path.join(out, "FullWidget-promo.mp4"), result = path.join(out, "FullWidget-promo-music.mp4");
  const r = spawnSync(ff, ["-y", "-loglevel", "error", "-i", video, "-i", wav, "-map", "0:v", "-map", "1:a", "-c:v", "copy",
    "-af", "loudnorm=I=-14:TP=-1.5:LRA=11", "-ar", "48000", "-c:a", "aac", "-b:a", "256k", "-shortest", "-movflags", "+faststart", result], { stdio: "inherit" });
  if (r.status !== 0) throw new Error("ffmpeg failed");
  console.log(`${path.relative(process.cwd(), result)}: ${(fs.statSync(result).size / 1048576).toFixed(1)} MB`);
}
