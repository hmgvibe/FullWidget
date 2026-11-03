// The promo's music, written in code: an original 120 BPM track in D major in the manner of a
// product feature video (tight drums, plucky synths, a bass that moves, a pad underneath), with
// every hit, whoosh and click placed on the video's own timeline (promo/scenes.js).
//
//   node promo/music.mjs            → promo/out/music.wav
//   node promo/music.mjs --mux      → also promo/out/FullWidget-promo-music.mp4 (video + music,
//                                     loudness normalised to −14 LUFS for online video)
//
// No samples and no npm packages: oscillators, noise, filters and a small reverb, all deterministic.
import fs from "node:fs";
import path from "node:path";
import { spawnSync } from "node:child_process";

const SR = 48000, DUR = 74, N = SR * DUR;
const BEAT = 0.5, BAR = 2;
const out = path.join(import.meta.dirname, "out");
fs.mkdirSync(out, { recursive: true });

// ───────────────────────── tools ─────────────────────────
let seed = 0x9e3779b9;
const rnd = () => { seed ^= seed << 13; seed ^= seed >>> 17; seed ^= seed << 5; return ((seed >>> 0) / 4294967296) * 2 - 1; };
const mtof = (m) => 440 * 2 ** ((m - 69) / 12);
const clamp = (v, a, b) => Math.min(b, Math.max(a, v));

/** RBJ biquad; set() can be called while running for moving filters. */
class Biquad {
  constructor(type, f, q = 0.707) { this.type = type; this.x1 = this.x2 = this.y1 = this.y2 = 0; this.set(f, q); }
  set(f, q = this.q) {
    this.q = q;
    const w = (2 * Math.PI * clamp(f, 10, SR * 0.45)) / SR, c = Math.cos(w), a = Math.sin(w) / (2 * q);
    let b0, b1, b2;
    if (this.type === "lp") { b0 = (1 - c) / 2; b1 = 1 - c; b2 = (1 - c) / 2; }
    else if (this.type === "hp") { b0 = (1 + c) / 2; b1 = -(1 + c); b2 = (1 + c) / 2; }
    else { b0 = a; b1 = 0; b2 = -a; } // band-pass (constant peak gain)
    const a0 = 1 + a;
    this.b0 = b0 / a0; this.b1 = b1 / a0; this.b2 = b2 / a0; this.a1 = (-2 * c) / a0; this.a2 = (1 - a) / a0;
  }
  run(x) {
    const y = this.b0 * x + this.b1 * this.x1 + this.b2 * this.x2 - this.a1 * this.y1 - this.a2 * this.y2;
    this.x2 = this.x1; this.x1 = x; this.y2 = this.y1; this.y1 = y;
    return y;
  }
}

// buses: drums & effects (dry), music (ducked by the kick), and a reverb send
const bus = () => [new Float32Array(N), new Float32Array(N)];
const DRY = bus(), MUSIC = bus(), SEND = bus();
const kicks = [];

/** Adds a mono voice at time t: fn(k, dt) gives sample k (seconds since start). */
function voice(target, t, len, fn, { pan = 0, gain = 1, send = 0 } = {}) {
  const s0 = Math.round(t * SR), n = Math.round(len * SR);
  const gl = gain * Math.cos(((pan + 1) * Math.PI) / 4), gr = gain * Math.sin(((pan + 1) * Math.PI) / 4);
  for (let i = 0; i < n; i++) {
    const j = s0 + i;
    if (j < 0 || j >= N) continue;
    const v = fn(i / SR, i);
    target[0][j] += v * gl; target[1][j] += v * gr;
    if (send) { SEND[0][j] += v * gl * send; SEND[1][j] += v * gr * send; }
  }
}
const env = (k, a, d) => (k < a ? k / a : Math.exp(-(k - a) / d));
const saw = (ph) => 2 * (ph - Math.floor(ph + 0.5));

// ───────────────────────── instruments ─────────────────────────
function kick(t, g = 0.88) {
  kicks.push(t);
  let ph = 0;
  voice(DRY, t, 0.5, (k) => {
    const f = 46 + 110 * Math.exp(-k / 0.035);
    ph += f / SR;
    return Math.sin(2 * Math.PI * ph) * Math.exp(-k / 0.28) + (k < 0.004 ? rnd() * 0.3 * (1 - k / 0.004) : 0);
  }, { gain: g });
}
function clap(t, g = 0.45) {
  const bp = new Biquad("bp", 1500, 0.9), hp = new Biquad("hp", 600);
  voice(DRY, t, 0.35, (k) => {
    const bursts = [0, 0.011, 0.022].reduce((s, o) => s + (k >= o ? Math.exp(-(k - o) / (o === 0.022 ? 0.09 : 0.012)) : 0), 0);
    return hp.run(bp.run(rnd())) * bursts * 1.6;
  }, { gain: g, send: 0.25, pan: 0.05 });
}
function hat(t, g = 0.16, open = false, pan = 0.2) {
  const hp = new Biquad("hp", 7500, 0.8), bp = new Biquad("bp", 10000, 1.2);
  voice(DRY, t, open ? 0.3 : 0.08, (k) => (hp.run(rnd()) * 0.6 + bp.run(rnd()) * 0.8) * Math.exp(-k / (open ? 0.11 : 0.022)), { gain: g, pan });
}
function snare(t, g = 0.35) {
  const bp = new Biquad("bp", 2200, 0.7);
  let ph = 0;
  voice(DRY, t, 0.25, (k) => { ph += 190 / SR; return (bp.run(rnd()) * 1.3 + Math.sin(2 * Math.PI * ph) * 0.5) * Math.exp(-k / 0.07); }, { gain: g, send: 0.2 });
}
function crash(t, g = 0.22) {
  const hp = new Biquad("hp", 4000, 0.6);
  voice(DRY, t, 2.2, (k) => hp.run(rnd()) * Math.exp(-k / 0.7), { gain: g, send: 0.3, pan: -0.15 });
}
/** A plucked synth note: two detuned saws through a closing low-pass. */
function pluck(t, m, { g = 0.16, len = 0.5, bright = 5200, decay = 0.22, pan = 0, send = 0.3, target = MUSIC } = {}) {
  const f = mtof(m), lp = new Biquad("lp", bright, 1.1);
  let a = Math.abs(rnd()), b = Math.abs(rnd());
  voice(target, t, len, (k, i) => {
    if (i % 32 === 0) lp.set(400 + bright * Math.exp(-k / (decay * 0.6)), 1.1);
    a += f / SR; b += (f * 1.006) / SR;
    return lp.run(saw(a) + saw(b) * 0.7) * env(k, 0.002, decay) * 0.5;
  }, { gain: g, pan, send });
}
const chordStab = (t, notes, g = 0.3) => notes.forEach((m, i) => pluck(t, m + 12, { g: g / notes.length * 2.2, len: 1.4, bright: 6000, decay: 0.55, pan: (i / (notes.length - 1) - 0.5) * 0.8, send: 0.45 }));
/** A soft pad: three detuned saws per note, slow in, slow out, a little wider in stereo. */
function pad(t, len, notes, { g = 0.2, cutoff = 1700, target = MUSIC } = {}) {
  for (const [side, pan] of [[0, -0.6], [1, 0.6]]) {
    const lp = new Biquad("lp", cutoff, 0.6);
    const phs = notes.map(() => [Math.abs(rnd()), Math.abs(rnd()), Math.abs(rnd())]);
    voice(target, t, len + 0.6, (k, i) => {
      if (i % 64 === 0) lp.set(cutoff * (0.85 + 0.15 * Math.sin(k * 1.3 + side)), 0.6);
      let s = 0;
      notes.forEach((m, n) => {
        const f = mtof(m);
        [0.997 + side * 0.001, 1.0, 1.004 - side * 0.001].forEach((d, v) => { phs[n][v] += (f * d) / SR; s += saw(phs[n][v]); });
      });
      const a = Math.min(1, k / 0.35) * (k > len ? Math.exp(-(k - len) / 0.25) : 1);
      return lp.run(s / (notes.length * 3)) * a;
    }, { gain: g, pan, send: 0.35 });
  }
}
function bass(t, m, len, g = 0.5) {
  const f = mtof(m), lp = new Biquad("lp", 900, 1.2);
  let ph = 0, ps = 0;
  voice(MUSIC, t, len + 0.03, (k, i) => {
    if (i % 32 === 0) lp.set(260 + 900 * Math.exp(-k / 0.08), 1.2);
    ph += f / SR; ps += f / SR;
    const a = Math.min(1, k / 0.004) * (0.75 + 0.25 * Math.exp(-k / 0.1)) * (k > len - 0.02 ? Math.max(0, (len + 0.03 - k) / 0.05) : 1);
    return (lp.run(saw(ph)) * 0.7 + Math.sin(2 * Math.PI * ps) * 0.6) * a;
  }, { gain: g });
}
/** Air rushing past: noise through a band-pass that sweeps, louder in the middle. */
function whoosh(t, len = 0.45, g = 0.28, up = true, pan = 0) {
  const bp = new Biquad("bp", 600, 1.4);
  voice(DRY, t - len * 0.6, len, (k, i) => {
    const p = k / len;
    if (i % 32 === 0) bp.set(up ? 300 * 12 ** p : 3600 / 12 ** p, 1.4);
    return bp.run(rnd()) * Math.sin(Math.PI * p) ** 2 * 1.4;
  }, { gain: g, pan, send: 0.2 });
}
/** Builds tension into t: rising filtered noise and a rising tone. */
function riser(t, len, g = 0.3) {
  const bp = new Biquad("bp", 400, 2);
  let ph = 0;
  voice(DRY, t - len, len, (k, i) => {
    const p = k / len;
    if (i % 32 === 0) bp.set(300 * 20 ** p, 2);
    ph += (110 * 4 ** p) / SR;
    return (bp.run(rnd()) * 1.5 + saw(ph) * 0.12) * p ** 2;
  }, { gain: g, send: 0.3 });
}
function roll(from, to, g = 0.22) { // a snare roll speeding up into `to`
  let t = from;
  while (t < to - 0.01) { const p = (t - from) / (to - from); snare(t, g * (0.4 + 0.6 * p)); t += p < 0.5 ? 0.125 : p < 0.8 ? 0.0625 : 0.03125; }
}
function boom(t, g = 0.8) {
  let ph = 0;
  const lp = new Biquad("lp", 300);
  voice(DRY, t, 2.2, (k) => { ph += (42 + 50 * Math.exp(-k / 0.06)) / SR; return Math.sin(2 * Math.PI * ph) * Math.exp(-k / 0.7) + lp.run(rnd()) * Math.exp(-k / 0.15) * 0.4; }, { gain: g, send: 0.4 });
}
// interface sounds: crisp, short, a little reverb
function click(t, g = 0.3) {
  const hp = new Biquad("hp", 2500);
  voice(DRY, t, 0.05, (k) => (Math.sin(2 * Math.PI * 2600 * k) * 0.6 + hp.run(rnd()) * 0.6) * Math.exp(-k / 0.008), { gain: g, send: 0.1, pan: 0.1 });
}
function pop(t, g = 0.22, f0 = 520, pan = 0) {
  let ph = 0;
  voice(DRY, t, 0.12, (k) => { ph += (f0 * (1 + 1.2 * Math.min(1, k / 0.05))) / SR; return Math.sin(2 * Math.PI * ph) * env(k, 0.003, 0.04); }, { gain: g, send: 0.25, pan });
}
function tick(t, g = 0.12, f = 1800, pan = 0) {
  voice(DRY, t, 0.04, (k) => Math.sin(2 * Math.PI * f * k) * Math.exp(-k / 0.01), { gain: g, pan });
}
function chime(t, m, g = 0.16, pan = 0) {
  const f = mtof(m);
  voice(DRY, t, 1.2, (k) => [1, 2.76, 5.4].reduce((s, r, n) => s + Math.sin(2 * Math.PI * f * r * k) * Math.exp(-k / (0.45 / (n + 1))) / (n + 1), 0) * Math.min(1, k / 0.002), { gain: g, send: 0.5, pan });
}

// ───────────────────────── the score ─────────────────────────
// D major: Dmaj9, Bm9, Gmaj9, Asus — one chord per bar
const CHORDS = [
  { root: 38, pad: [50, 54, 57, 61, 64] },
  { root: 35, pad: [47, 50, 54, 57, 61] },
  { root: 31, pad: [43, 47, 50, 54, 57] },
  { root: 33, pad: [45, 50, 52, 57, 59] },
];
const chordAt = (t) => CHORDS[Math.floor(t / BAR) % 4];
const inAny = (t, ranges) => ranges.some(([a, b]) => t >= a - 1e-6 && t < b - 1e-6);

const DRUMS = [[5, 42], [46, 59], [66, 72]];
const BASS = [[5, 59], [66, 72]];
const ARP = [[5, 59], [66, 72]];
const ARP_STEPS = [0, 1, 2, 3, 4, 3, 2, 1, 0, 2, 4, 2, 1, 3, 4, 3];
const BASS_STEPS = [0, 0, 12, 0, 0, 0, 12, 0];

for (let b = 0; b < DUR / BAR; b++) {
  const t0 = b * BAR, ch = chordAt(t0);
  // pad all the way, darker in the breakdown
  if (t0 >= 2 && t0 < 72) pad(t0, BAR, ch.pad, { g: t0 >= 59 && t0 < 66 ? 0.36 : 0.17, cutoff: t0 >= 59 && t0 < 66 ? 1500 : 1900 });
  for (let s = 0; s < 16; s++) {
    const t = t0 + s * (BEAT / 4);
    if (inAny(t, DRUMS)) {
      if (s % 4 === 0) kick(t);
      if (s === 4 || s === 12) clap(t);
      if (s % 2 === 1) hat(t, 0.15, s % 4 === 2 ? false : false, s % 4 === 1 ? 0.25 : -0.2);
      else hat(t, 0.06, false, 0.3);
      if (s === 14 && b % 4 === 3) hat(t, 0.12, true, -0.3); // an open hat at the end of each phrase
    } else if (t >= 3 && t < 5) { // the intro's hats fading in under the words
      if (s % 2 === 1) hat(t, 0.06 + 0.06 * ((t - 3) / 2));
    } else if (t >= 42 && t < 46) { // the code section: half time, so it breathes but keeps moving
      if (s === 0 || s === 10) kick(t, 0.72);
      if (s === 8) clap(t, 0.42);
      if (s % 2 === 1) hat(t, 0.12, false, s % 4 === 1 ? 0.25 : -0.2);
    }
    if (inAny(t, BASS) && s % 2 === 0) bass(t, ch.root + BASS_STEPS[(s / 2) % 8], BEAT / 2 - 0.02, t >= 66 ? 0.46 : 0.42);
    if (inAny(t, ARP)) {
      const n = ch.pad[ARP_STEPS[s] % ch.pad.length] + 24;
      pluck(t, n, { g: t >= 42 && t < 46 ? 0.15 : 0.14, pan: s % 2 ? 0.35 : -0.35, decay: 0.16 });
    }
  }
}

// ── 0–2: the logo slams together, bursts ──
[74, 78, 81, 86].forEach((m, i) => { pluck(i * 0.25, m, { g: 0.32, decay: 0.3, len: 0.8, send: 0.5, target: DRY }); tick(i * 0.25, 0.18, 2400); });
chordStab(1.0, CHORDS[0].pad, 0.34); clap(1.0, 0.35);
riser(2.0, 0.75, 0.32);
// ── 2–5: 桌面 / 也可以 / 很好看。 ──
[[2.0, 0], [2.5, 1], [3.0, 2]].forEach(([t, c]) => { kick(t, 0.85); chordStab(t, CHORDS[c].pad, 0.32); clap(t, 0.3); });
riser(5.0, 1.5, 0.36); roll(4.0, 5.0, 0.2);
// ── 5–8: the drop; 38 counted up; the dive ──
crash(5.0); boom(5.0, 0.38);
for (let i = 0; i < 12; i++) tick(5.3 + i * 0.09, 0.08, 1500 + i * 60, 0.3);
whoosh(7.6, 0.7, 0.32);
// ── 8–23: six categories (whip cuts), calculator keys, to-do ticks ──
[8, 10.5, 13, 15.5, 18, 20.5].forEach((t, i) => { whoosh(t, 0.42, 0.26, i % 2 === 0, i % 2 ? 0.3 : -0.3); chime(t, [74, 81, 78, 83, 76, 81][i], 0.1); });
for (let i = 0; i < 10; i++) tick(15.95 + i * 0.12, 0.12, 1300 + (i % 3) * 200, 0.2);
[16.55, 17.05].forEach((t) => { click(t, 0.22); pop(t + 0.02, 0.15, 700); });
// ── 23–27: three themes, one per beat ──
crash(23.0, 0.18); whoosh(23.0, 0.4, 0.24);
[23.5, 24, 24.5, 25, 25.5, 26].forEach((t, i) => chime(t, [86, 90, 93, 86, 90, 93][i], 0.17, (i % 3 - 1) * 0.4));
// ── 27–31: colour, radius, opacity ──
whoosh(27.0, 0.4, 0.22, false);
[27.6, 28.1, 28.6, 29.1].forEach((t, i) => pop(t, 0.18, 500 + i * 90, 0.2));
[29.2, 29.6, 30.1, 30.45, 30.8].forEach((t) => tick(t, 0.1, 1900));
// ── 31–33: resize, the content grows ──
whoosh(31.0, 0.4, 0.22); click(31.3, 0.26);
(() => { let ph = 0; voice(DRY, 31.35, 1.2, (k) => { ph += (220 * 2 ** (k / 1.2 * 1.5)) / SR; return Math.sin(2 * Math.PI * ph) * Math.sin(Math.PI * k / 1.2) * 0.5; }, { gain: 0.16, send: 0.4 }); })();
// ── 33–36: drag and snap ──
[33.45, 34.75].forEach((t) => click(t, 0.24));
[34.3, 35.45].forEach((t) => { pop(t, 0.26, 620); click(t + 0.01, 0.2); });
// ── 36–39: layers ──
whoosh(36.0, 0.4, 0.22, false);
[36.4, 36.9, 37.35].forEach((t, i) => pop(t, 0.2, 480 + i * 120, (i - 1) * 0.4));
click(38.05, 0.28);
// ── 39–42: full screen (the music is muffled while the game fills the screen) ──
whoosh(39.0, 0.4, 0.22); whoosh(39.6, 0.5, 0.3, false); whoosh(41.35, 0.5, 0.26, true);
// ── 42–48: write a widget: keys while it types, the beat comes back ──
whoosh(42.0, 0.4, 0.22);
[[42.35, 83], [43.75, 92], [45.45, 77]].forEach(([at, len]) => { for (let i = 0; i < len; i += 2) tick(at + i / 62, 0.05 + 0.03 * ((i * 7) % 3) / 2, 2600 + ((i * 37) % 5) * 180, ((i % 4) - 1.5) * 0.15); });
pop(43.5, 0.2, 560); chime(45.1, 86, 0.14); chime(46.75, 93, 0.14);
riser(46.0, 1.0, 0.28); roll(45.5, 46.0, 0.18); crash(46.0, 0.18);
// ── 48–51: a .fwz; double-click; allow; on the desk ──
whoosh(48.0, 0.5, 0.26, false);
[49.05, 49.22, 50.15].forEach((t) => click(t, 0.3));
pop(49.45, 0.2, 440); pop(50.4, 0.24, 640);
// ── 51–54: the market ──
whoosh(51.0, 0.5, 0.26); click(52.35, 0.3); whoosh(52.95 + 0.5, 0.6, 0.22, false); pop(53.75, 0.24, 660);
// ── 54–57: 0.4 % ──
whoosh(54.0, 0.4, 0.22);
for (let i = 0; i < 10; i++) tick(54.35 + i * 0.1, 0.09, 1200 + i * 90, 0.25);
// ── 57–59: three languages, into the breakdown ──
[57, 57.5, 58, 58.5].forEach((t, i) => chime(t, [86, 90, 93, 98][i], 0.15));
riser(59.0, 1.0, 0.32); roll(58.0, 59.0, 0.2);
// ── 59–66: free, open source: big, slow hits; a build back up ──
[59.04, 59.54, 60.04].forEach((t, i) => { boom(t, 0.5); chordStab(t, CHORDS[[0, 1, 2][i]].pad, 0.3); });
[60.5, 60.75, 61.0].forEach((t, i) => pop(t, 0.2, 520 + i * 80, (i - 1) * 0.4));
chime(61.25, 81, 0.2);
for (let t = 60.5; t < 66; t += 0.25) pluck(t, CHORDS[Math.floor(t / 2) % 4].pad[(t * 4) % 5 | 0] + 24, { g: 0.13 + 0.06 * ((t - 60.5) / 5.5), decay: 0.35, len: 0.9, send: 0.55 });
for (let t = 62; t < 66; t += 0.5) hat(t + 0.25, 0.05 + 0.07 * ((t - 62) / 4)); // the beat starts creeping back
riser(66.0, 2.5, 0.38); roll(64.0, 66.0, 0.22);
// ── 66–74: everything comes back and folds into the logo ──
crash(66.0, 0.26); boom(66.0, 0.38);
whoosh(68.4, 1.0, 0.24, false);
kick(68.5, 0.9); chordStab(68.5, CHORDS[0].pad, 0.3); clap(68.5, 0.3);
[68.65, 69.5, 70.5].forEach((t) => chime(t, 86, 0.1));
// the last chord rings out
crash(72.0, 0.22); kick(72.0, 0.9); chordStab(72.0, CHORDS[0].pad, 0.36);
pad(72.0, 1.4, CHORDS[0].pad, { g: 0.22, cutoff: 2400 });
pluck(72.0, 50, { g: 0.3, len: 2, decay: 1.0, send: 0.6 });

// ───────────────────────── mixing ─────────────────────────
// the music bus ducks under each kick (side-chain), so the beat stays clear
const duck = new Float32Array(N).fill(1);
for (const t of kicks) { const s0 = Math.round(t * SR); for (let i = 0; i < SR * 0.3 && s0 + i < N; i++) duck[s0 + i] = Math.min(duck[s0 + i], 1 - 0.55 * Math.exp(-i / SR / 0.09)); }
const mix = [new Float32Array(N), new Float32Array(N)];
for (let c = 0; c < 2; c++) for (let j = 0; j < N; j++) mix[c][j] = DRY[c][j] + MUSIC[c][j] * duck[j];

// a small stereo reverb on the send (Freeverb-style: combs into all-passes)
function reverb(x, offset) {
  const combs = [1557, 1617, 1491, 1422, 1277, 1356, 1188, 1116].map((d) => ({ buf: new Float32Array(d + offset), i: 0, f: 0 }));
  const alls = [556, 441, 341, 225].map((d) => ({ buf: new Float32Array(d + offset), i: 0 }));
  const y = new Float32Array(N), fb = 0.86, damp = 0.25;
  for (let j = 0; j < N; j++) {
    let s = 0;
    for (const c of combs) { const o = c.buf[c.i]; c.f = o * (1 - damp) + c.f * damp; c.buf[c.i] = x[j] + c.f * fb; c.i = (c.i + 1) % c.buf.length; s += o; }
    s *= 0.12;
    for (const a of alls) { const o = a.buf[a.i]; const v = -s + o; a.buf[a.i] = s + o * 0.5; a.i = (a.i + 1) % a.buf.length; s = v; }
    y[j] = s;
  }
  return y;
}
const rv = [reverb(SEND[0], 0), reverb(SEND[1], 23)];
for (let c = 0; c < 2; c++) for (let j = 0; j < N; j++) mix[c][j] += rv[c][j] * 0.9;

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
const pre = 1.25 / raw; // the top of the peaks goes 2 dB into the soft clip
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
console.log(`${path.relative(process.cwd(), wav)}: ${DUR} s, ${kicks.length} kicks, raw peak ${raw.toFixed(2)}, RMS ${rmsDb.toFixed(1)} dBFS`);

if (process.argv.includes("--mux")) {
  const root = path.resolve(import.meta.dirname, "..");
  const ff = [path.join(root, ".tools", "ffmpeg", "bin", "ffmpeg.exe")].find((p) => fs.existsSync(p)) || "ffmpeg";
  const video = path.join(out, "FullWidget-promo.mp4"), result = path.join(out, "FullWidget-promo-music.mp4");
  const r = spawnSync(ff, ["-y", "-loglevel", "error", "-i", video, "-i", wav, "-map", "0:v", "-map", "1:a", "-c:v", "copy",
    "-af", "loudnorm=I=-14:TP=-1.0:LRA=11", "-ar", "48000", "-c:a", "aac", "-b:a", "256k", "-shortest", "-movflags", "+faststart", result], { stdio: "inherit" });
  if (r.status !== 0) throw new Error("ffmpeg failed");
  console.log(`${path.relative(process.cwd(), result)}: ${(fs.statSync(result).size / 1048576).toFixed(1)} MB`);
}
