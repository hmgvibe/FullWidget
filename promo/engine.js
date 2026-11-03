/* The promo's timeline core. Every picture is a pure function of the time t (seconds): nothing
   runs on its own clock, so the page can be scrubbed in a browser and recorded frame by frame. */
(function () {
  "use strict";

  const W = 1920, H = 1080, FPS = 60, BPM = 120;
  const beat = (n) => (n * 60) / BPM; // 0.5 s
  const bar = (n) => beat(n * 4);     // 2 s

  const clamp = (v, a = 0, b = 1) => Math.min(b, Math.max(a, v));
  const lerp = (a, b, p) => a + (b - a) * p;

  /** CSS cubic-bezier(x1, y1, x2, y2) as a function of progress. */
  function cubicBezier(x1, y1, x2, y2) {
    const cx = 3 * x1, bx = 3 * (x2 - x1) - cx, ax = 1 - cx - bx;
    const cy = 3 * y1, by = 3 * (y2 - y1) - cy, ay = 1 - cy - by;
    const x = (s) => ((ax * s + bx) * s + cx) * s;
    const y = (s) => ((ay * s + by) * s + cy) * s;
    const dx = (s) => (3 * ax * s + 2 * bx) * s + cx;
    return (p) => {
      if (p <= 0) return 0;
      if (p >= 1) return 1;
      let s = p;
      for (let i = 0; i < 8; i++) {
        const e = x(s) - p;
        if (Math.abs(e) < 1e-6) return y(s);
        const d = dx(s);
        if (Math.abs(d) < 1e-6) break;
        s -= e / d;
      }
      let lo = 0, hi = 1; s = p;
      for (let i = 0; i < 30; i++) { if (x(s) < p) lo = s; else hi = s; s = (lo + hi) / 2; }
      return y(s);
    };
  }

  const ease = {
    linear: (p) => p,
    // the website's two curves: "slide" for things settling, "click" for a magnet snapping shut
    slide: cubicBezier(0.22, 1, 0.36, 1),
    click: cubicBezier(0.3, 1.45, 0.5, 1),
    inOut: cubicBezier(0.65, 0, 0.35, 1),
    out: cubicBezier(0.16, 1, 0.3, 1),
    in: cubicBezier(0.55, 0, 0.75, 0.1),
    soft: cubicBezier(0.4, 0, 0.2, 1),
  };

  /** Eased progress of t through [start, start + dur]. */
  const prog = (t, start, dur, e = ease.inOut) => e(clamp((t - start) / dur));

  const mix = (a, b, p) => (Array.isArray(a) ? a.map((v, i) => lerp(v, b[i], p)) : lerp(a, b, p));

  /** Keyframes: [[t, value], [t, value, ease], ...]; values are numbers or arrays of numbers. */
  function keys(t, list) {
    if (t <= list[0][0]) return list[0][1];
    for (let i = 1; i < list.length; i++) {
      if (t < list[i][0]) {
        const [t0, v0] = list[i - 1];
        const [t1, v1, e = ease.inOut] = list[i];
        return mix(v0, v1, e((t - t0) / (t1 - t0)));
      }
    }
    return list[list.length - 1][1];
  }

  /** "#7de2c4" → [r, g, b]; mixing colours as numbers keeps every frame exact. */
  const rgb = (hex) => { const n = parseInt(hex.slice(1), 16); return [(n >> 16) & 255, (n >> 8) & 255, n & 255]; };
  const css = ([r, g, b], a = 1) => `rgba(${Math.round(r)},${Math.round(g)},${Math.round(b)},${a})`;

  /** Writes styles, but only what changed since the last frame. */
  function style(el, props) {
    const last = el.__s || (el.__s = {});
    for (const k in props) {
      const v = props[k];
      if (last[k] === v) continue;
      last[k] = v;
      if (k.startsWith("--")) el.style.setProperty(k, v); else el.style[k] = v;
    }
  }
  const show = (el, on) => style(el, { visibility: on ? "visible" : "hidden" });

  function el(tag, cls, parent, html) {
    const e = document.createElement(tag);
    if (cls) e.className = cls;
    if (html != null) e.innerHTML = html;
    if (parent) parent.append(e);
    return e;
  }

  /** Splits text into one span per character (spaces kept), for the per-letter reveals. */
  function letters(parent, text) {
    const spans = [];
    for (const ch of Array.from(text)) {
      const s = el("span", ch === " " ? "ch sp" : "ch", parent);
      s.textContent = ch;
      spans.push(s);
    }
    return spans;
  }

  /** Apple-style entrance: each letter rises out of a blur, one after another. */
  function reveal(spans, t, start, { stagger = 0.028, dur = 0.7, rise = 0.32, blur = 10 } = {}) {
    spans.forEach((s, i) => {
      const p = prog(t, start + i * stagger, dur, ease.slide);
      style(s, {
        opacity: p.toFixed(4),
        transform: `translateY(${((1 - p) * rise).toFixed(4)}em)`,
        filter: p >= 1 ? "none" : `blur(${((1 - p) * blur).toFixed(2)}px)`,
      });
    });
  }

  /** The way out: the whole group lifts a little and fades. */
  function conceal(node, t, start, { dur = 0.35, lift = 18 } = {}) {
    const p = prog(t, start, dur, ease.in);
    style(node, {
      opacity: (1 - p).toFixed(4),
      transform: `translateY(${(-lift * p).toFixed(2)}px)`,
      filter: p <= 0 ? "none" : `blur(${(p * 6).toFixed(2)}px)`,
    });
    return p;
  }

  /** Typed text: how many characters of `text` show at t when typing starts at `start`. */
  const typed = (text, t, start, cps = 26) => text.slice(0, Math.max(0, Math.min(text.length, Math.floor((t - start) * cps))));

  window.E = { W, H, FPS, BPM, beat, bar, clamp, lerp, cubicBezier, ease, prog, mix, keys, rgb, css, style, show, el, letters, reveal, conceal, typed };
})();
