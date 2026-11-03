/* The eight scenes of the promo, as one continuous shot. Each frame is drawn by render(t).
   Timing is in seconds on a 120 BPM grid (one beat = 0.5 s, one bar = 2 s):
     0–4    logo: three cards and a dot snap into the F, then open up into widget tiles
     4–12   38 widgets: the tiles pull back into the website's waterfall; we dive into the clock
     12–20  three themes: that tile opens into a desktop, which changes theme on the beat
     20–28  snapping: a widget is dragged and resized; mint guides show what it lined up with
     28–38  write your own: a code panel types a widget and the desktop follows
     38–46  the market: the new widget is submitted, another one is installed
     46–52  free, open source, collects nothing (hard cut to graphite)
     52–60  everything folds back into the logo; download */
(function () {
  "use strict";
  const { W, H, clamp, lerp, ease, prog, keys, rgb, css, style, show, el, letters, reveal, conceal, typed } = E;
  const T = window.COPY;
  const stage = document.getElementById("stage");
  const LANG = T.lang;
  // the clock everywhere shows 9:41, like the live one on the desk (render.mjs --assets)
  const pic = (folder, theme = "instrument") => folder === "clock" ? `assets/clock-0941.${theme}.png` : `../site/assets/widgets/${folder}/${theme}.${LANG}.webp`;
  const marketPic = (folder, theme = "instrument") => `assets/market/${folder}/${theme}.${LANG}.webp`;
  const THEMES = ["instrument", "paper", "type"];
  const px = (v) => `${v.toFixed(2)}px`;
  const bump = (p) => Math.sin(Math.PI * clamp(p));

  // the widget pictures' sizes (2× renders from the website, site/assets/widgets)
  const SIZE = {
    airquality: [600, 380], analogclock: [440, 440], battery: [480, 280], calculator: [520, 680], calendar: [600, 600],
    clock: [600, 300], countdown: [560, 360], currency: [600, 660], dailyimage: [800, 500], disks: [600, 360],
    flipclock: [640, 320], forecast: [720, 420], habits: [680, 380], infocard: [560, 340], label: [720, 240],
    launcher: [680, 320], media: [680, 272], netspeed: [600, 340], network: [600, 360], notes: [520, 460],
    onthisday: [680, 380], pcinfo: [640, 440], photoframe: [640, 480], pomodoro: [600, 340], power: [640, 220],
    progress: [600, 400], recyclebin: [480, 300], rss: [720, 480], search: [760, 180], sunmoon: [600, 380],
    sysinfo: [600, 420], timer: [600, 400], todo: [560, 600], usage: [640, 360], volume: [560, 220],
    weather: [640, 400], whitenoise: [600, 460], worldclock: [600, 400],
  };
  const aspect = (f) => SIZE[f][1] / SIZE[f][0];
  const LOGO = { cyan: "#4cc9f0", violet: "#9b5de5", yellow: "#ffd166", green: "#06d6a0" };

  const layers = {};
  const layer = (name, cls = "") => (layers[name] = el("div", `layer ${cls}`, stage));

  // ───────────────────────── logo shapes (intro and ending) ─────────────────────────
  // The F on a 64-unit grid (build/logo/logo.svg): x, y, w, h, radius.
  const F = {
    stem: [4, 4, 18, 56, 7, LOGO.cyan],
    top: [26, 4, 34, 18, 7, LOGO.violet],
    mid: [26, 26, 20, 16, 7, LOGO.yellow],
    dot: [43, 43, 17, 17, 8.5, LOGO.green],
  };
  const LOGO_K = 4.2; // 64 units → 269 px
  const logoRect = (name, ox, oy, k = LOGO_K) => {
    const [x, y, w, h, r] = F[name];
    return { x: ox + x * k, y: oy + y * k, w: w * k, h: h * k, r: r * k };
  };
  const mixRect = (a, b, p) => ({ x: lerp(a.x, b.x, p), y: lerp(a.y, b.y, p), w: lerp(a.w, b.w, p), h: lerp(a.h, b.h, p), r: lerp(a.r, b.r, p) });

  /** A logo shape that can turn into a widget tile: flat colour → wallpaper + widget picture. */
  function makeShape(parent, name, tile) {
    const s = el("div", "shape", parent);
    const fill = el("div", "fill", s);
    fill.style.background = F[name][5];
    const tex = el("div", `tex wp${tile.wp}`, s);
    const img = el("img", "", s);
    img.src = pic(tile.folder, tile.theme);
    return { s, fill, tex, img, tile };
  }
  /** Draws a shape at rect; tex/img are 0..1 (how much of the tile shows). */
  function drawShape(sh, r, { tex = 0, img = 0, opacity = 1, transform = "none" } = {}) {
    style(sh.s, { left: px(r.x), top: px(r.y), width: px(Math.max(1, r.w)), height: px(Math.max(1, r.h)), borderRadius: px(r.r), opacity: opacity.toFixed(4), transform });
    style(sh.tex, { opacity: tex.toFixed(4) });
    // the picture sits where it does on a tile: 16 px in, full width minus 32 (of a tile's own size)
    const tw = sh.tile.tw, th = sh.tile.th;
    style(sh.img, {
      opacity: img.toFixed(4),
      left: `${(16 / tw) * 100}%`, top: `${(16 / th) * 100}%`, width: `${((tw - 32) / tw) * 100}%`,
    });
  }

  // ───────────────────────── ① the waterfall (4–12 s) ─────────────────────────
  const COLS = 6, PAD = 24, GAP = 20;
  const COLW = (W - 2 * PAD - (COLS - 1) * GAP) / COLS;
  const tileH = (f) => (COLW - 32) * aspect(f) + 32;
  const colX = (c) => PAD + c * (COLW + GAP);
  const hash = (s) => { let h = 2166136261; for (const ch of s) { h ^= ch.charCodeAt(0); h = Math.imul(h, 16777619); } return h >>> 0; };

  const flow = (() => {
    const order = Object.keys(SIZE).sort((a, b) => hash(a) - hash(b));
    const themes = ["instrument", "paper", "instrument", "type"];
    const heights = new Array(COLS).fill(0);
    const lists = Array.from({ length: COLS }, () => []);
    order.forEach((f, i) => {
      const c = heights.indexOf(Math.min(...heights));
      lists[c].push({ folder: f, theme: themes[i % 4], wp: i % 6 });
      heights[c] += tileH(f) + GAP;
    });
    // the clock goes third in column 2: it is the first tile we see (from the logo's stem) and
    // the one we dive into
    const at = lists.flatMap((l, c) => l.map((it, i) => [c, i])).find(([c, i]) => lists[c][i].folder === "clock");
    [lists[2][2], lists[at[0]][at[1]]] = [lists[at[0]][at[1]], lists[2][2]];
    // the four tiles the logo opens into, in the logo's colours
    const A = lists[2][2], D = lists[2][3], B = lists[3][2], C = lists[3][3];
    Object.assign(A, { theme: "instrument", wp: 0 });
    Object.assign(B, { wp: 1 });
    Object.assign(C, { wp: 2 });
    Object.assign(D, { wp: 3 });
    // three copies per column, so nothing runs out while the columns flow down
    const cols = lists.map((l) => [...l, ...l.map((x) => ({ ...x })), ...l.map((x) => ({ ...x }))]);
    const first2 = (c) => tileH(cols[c][0].folder) + tileH(cols[c][1].folder) + 2 * GAP;
    const starts = [-250, -120, 300 - first2(2), 336 - first2(3), -330, -190];
    const items = [];
    cols.forEach((list, c) => {
      let y = starts[c];
      for (const it of list) {
        it.c = c; it.x = colX(c); it.y = y; it.tw = COLW; it.th = tileH(it.folder);
        y += it.th + GAP;
        items.push(it);
      }
    });
    return { items, cluster: { stem: A, top: B, mid: C, dot: D }, dive: A };
  })();

  const L1 = layer("flow");
  const world = el("div", "abs", L1);
  const colEls = Array.from({ length: COLS }, () => el("div", "abs", world));
  for (const it of flow.items) {
    const t = el("div", `tile wp${it.wp}`, colEls[it.c]);
    style(t, { left: px(it.x), top: px(it.y), width: px(it.tw), height: px(it.th) });
    const img = el("img", "", t);
    img.src = pic(it.folder, it.theme);
    it.el = t;
  }
  const fadeTop = el("div", "fade-top", L1);
  const fadeBottom = el("div", "fade-bottom", L1);

  // the cluster's box (world units, before the flow starts moving), and how big it is at 4 s
  const cl = Object.values(flow.cluster);
  const box = {
    x0: Math.min(...cl.map((i) => i.x)), x1: Math.max(...cl.map((i) => i.x + i.tw)),
    y0: Math.min(...cl.map((i) => i.y)), y1: Math.max(...cl.map((i) => i.y + i.th)),
  };
  const CAM0 = { fx: (box.x0 + box.x1) / 2, fy: (box.y0 + box.y1) / 2, s: Math.min((W * 0.6) / (box.x1 - box.x0), (H * 0.8) / (box.y1 - box.y0)) };
  const SPEED = [44, 36, 50, 40, 47, 34];
  // the flow starts at rest (so the cluster matches the logo exactly) and speeds up over 2 s
  const ramp = (t) => (t <= 4 ? 0 : t <= 6 ? ((t - 4) ** 2) / 4 : t - 5);
  const drift = (c, t) => SPEED[c] * ramp(t);

  function flowCam(t) {
    if (t <= 4) return CAM0;
    if (t <= 10.3) {
      const p = prog(t, 4, 2.4, ease.inOut);
      const home = { fx: 960, fy: 540 + 10 * prog(t, 6.4, 3.9, ease.linear), s: 1 + 0.03 * prog(t, 6.4, 3.9, ease.linear) };
      return { fx: lerp(CAM0.fx, home.fx, p), fy: lerp(CAM0.fy, home.fy, p), s: Math.exp(lerp(Math.log(CAM0.s), Math.log(home.s), p)) };
    }
    // the dive: towards the clock tile, which keeps flowing
    const d = flow.dive;
    const tx = d.x + d.tw / 2, ty = d.y + drift(2, t) + d.th / 2;
    const p = prog(t, 10.3, 1.7, ease.in);
    return { fx: lerp(960, tx, p), fy: lerp(550, ty, p), s: lerp(1.03, 1.5, p) };
  }
  const toScreen = (cam, x, y) => ({ x: (x - cam.fx) * cam.s + W / 2, y: (y - cam.fy) * cam.s + H / 2 });
  function tileScreen(it, t) {
    const cam = flowCam(t);
    const a = toScreen(cam, it.x, it.y + drift(it.c, t));
    return { x: a.x, y: a.y, w: it.tw * cam.s, h: it.th * cam.s, r: 18 * cam.s };
  }

  function renderFlow(t) {
    const on = t >= 4 && t < 12;
    show(L1, on);
    if (!on) return;
    const cam = flowCam(t);
    style(world, { transform: `translate(${px(W / 2 - cam.fx * cam.s)}, ${px(H / 2 - cam.fy * cam.s)}) scale(${cam.s.toFixed(5)})` });
    colEls.forEach((c, i) => style(c, { transform: `translateY(${px(drift(i, t))})` }));
    // the other tiles come in around the cluster, nearest first
    for (const it of flow.items) {
      let o = 1;
      if (!cl.includes(it)) {
        const dx = it.x + it.tw / 2 - CAM0.fx, dy = it.y + it.th / 2 - CAM0.fy;
        o = prog(t, 4.05 + Math.hypot(dx, dy) / 2600, 0.7, ease.soft);
      }
      if (it === flow.dive && t >= 10.6) o = 0; // the desk takes over from here
      style(it.el, { opacity: o.toFixed(4) });
    }
    const fade = prog(t, 4.8, 1.2, ease.soft) * (1 - prog(t, 10.6, 0.6, ease.soft));
    style(fadeTop, { opacity: fade.toFixed(4) });
    style(fadeBottom, { opacity: fade.toFixed(4) });
    style(world, { opacity: (1 - prog(t, 10.75, 0.85, ease.soft)).toFixed(4) });
  }

  // ───────────────────────── ⓪ intro logo (0–4 s) ─────────────────────────
  const L0 = layer("logo");
  const intro = {};
  for (const n of Object.keys(F)) intro[n] = makeShape(L0, n, flow.cluster[n]);
  const word0 = el("div", "wordmark", L0);
  const word0Letters = letters(word0, T.wordmark);
  const FLY = { stem: [-420, 60, -14], top: [300, -260, 12], mid: [420, 120, 9], dot: [180, 340, -10] };
  const ARRIVE = { stem: 0.15, top: 0.35, mid: 0.55, dot: 0.75 };
  let groupW = 0;

  function introOrigin(t, cy = H / 2) {
    // the logo starts centred, then slides left to make room for the wordmark
    const size = 64 * LOGO_K;
    const x0 = W / 2 - size / 2, x1 = W / 2 - groupW / 2;
    return { x: lerp(x0, x1, prog(t, 1.55, 0.8, ease.slide)), y: cy - size / 2 };
  }

  function renderIntro(t) {
    const on = t < 4;
    show(L0, on);
    if (!on) return;
    const o = introOrigin(t);
    const pulse = 1 + 0.035 * bump(prog(t, 1.5, 0.32, ease.linear));
    const names = Object.keys(F);
    names.forEach((n, i) => {
      const sh = intro[n];
      const home = logoRect(n, o.x, o.y);
      // fly in and click together
      const a = prog(t, ARRIVE[n], 0.75, ease.click);
      const [dx, dy, rot] = FLY[n];
      const flyT = `translate(${px(dx * (1 - a))}, ${px(dy * (1 - a))}) rotate(${(rot * (1 - a)).toFixed(3)}deg) scale(${(lerp(0.55, 1, a) * pulse).toFixed(4)})`;
      // then open up into the tile it becomes
      const m = prog(t, 2.6 + i * 0.06, 1.4 - i * 0.06, ease.inOut);
      const target = tileScreen(flow.cluster[n], 4);
      const r = mixRect(home, target, m);
      drawShape(sh, r, {
        opacity: clamp(a * 3),
        transform: m > 0 ? "none" : flyT,
        tex: prog(t, 2.9 + i * 0.05, 0.9, ease.soft),
        img: prog(t, 3.25 + i * 0.05, 0.7, ease.soft),
      });
    });
    // the wordmark slides out from behind the logo, then steps back for the tiles
    const size = 64 * LOGO_K;
    style(word0, { left: px(o.x + size + 56), top: px(H / 2 - 66) });
    reveal(word0Letters, t, 1.75, { stagger: 0.035, dur: 0.6, rise: 0.25, blur: 12 });
    if (t >= 2.6) conceal(word0, t, 2.6, { dur: 0.35 });
    else style(word0, { opacity: "1", transform: "none", filter: "none" });
  }

  // ───────────────────────── headlines (the band at the top) ─────────────────────────
  const LH = layer("heads");
  const heads = [
    { t0: 6.1, t1: 10.25, c: T.s1 },
    { t0: 12.35, t1: 19.85, c: T.s2 },
    { t0: 20.15, t1: 27.7, c: T.s3 },
    { t0: 28.2, t1: 37.6, c: T.s4 },
    { t0: 38.2, t1: 45.6, c: T.s5 },
  ].map((h) => {
    const node = el("div", "head", LH);
    const tt = el("div", "head-t", node);
    const ls = letters(tt, h.c.title);
    const sub = el("div", "head-s", node);
    const lines = (h.c.sub || []).map((s) => { const d = el("div", "", sub); d.textContent = s; d.style.display = "block"; return d; });
    return { ...h, node, ls, lines };
  });
  function renderHeads(t) {
    show(LH, t > 6 && t < 46);
    for (const h of heads) {
      const on = t >= h.t0 && t < h.t1 + 0.4;
      show(h.node, on);
      if (!on) continue;
      reveal(h.ls, t, h.t0, { stagger: 0.03, dur: 0.7 });
      reveal(h.lines, t, h.t0 + 0.3, { stagger: 0.1, dur: 0.8, rise: 0.6, blur: 8 });
      if (t >= h.t1) conceal(h.node, t, h.t1);
      else style(h.node, { opacity: "1", transform: "none", filter: "none" });
    }
  }

  // ───────────────────────── the desk (10.6–46 s) ─────────────────────────
  const LD = layer("desk");
  const DW = 1728, DH = 784;
  const DESK = { x: 96, y: 236, w: DW, h: DH };
  const desk = el("div", "desk", LD);
  const inner = el("div", "desk-in", desk);
  const walls = THEMES.map((th) => el("div", `wall wall-${th}`, inner));
  const face = el("div", "desk-face wp0", desk); // what the clock tile looked like, for the hand-over
  const faceImg = el("img", "", face);
  faceImg.src = pic("clock", "instrument");
  style(faceImg, { position: "absolute", left: `${(16 / COLW) * 100}%`, top: `${(16 / flow.dive.th) * 100}%`, width: `${((COLW - 32) / COLW) * 100}%`, filter: "drop-shadow(0 10px 18px rgba(0,0,0,.28))" });
  const deskDim = el("div", "dim", desk);

  // the desktop's widgets (desk units, 1728 × 784)
  const LAY = {
    clock: { x: 88, y: 88, w: 560 },
    weather: { x: 676, y: 88, w: 448 },
    sysinfo: { x: 676, y: 396, w: 448 },
    calendar: { x: 1220, y: 88, w: 420 },
    media: { x: 150, y: 470, w: 476 },
  };
  const SLOT = { x: 1220, y: 536, w: 420, h: 196 };
  const widgets = {};
  for (const [f, p] of Object.entries(LAY)) {
    const w = el("div", "w", inner);
    const sh = el("div", "shadow", w);
    const imgs = f === "clock" ? [] : THEMES.map((th) => { const i = el("img", "", w); i.src = pic(f, th); return i; });
    widgets[f] = { el: w, shadow: sh, imgs, ...p, h: f === "clock" ? 280 : p.w * aspect(f) };
  }
  // the clock is the real widget, running in this page (site/widget-host.js), always 9:41
  const clockFrame = el("iframe", "", widgets.clock.el);
  clockFrame.setAttribute("scrolling", "no");
  let clockHost = null, clockTheme = "instrument";
  const FIXED_TIME = "<script>(function(){var R=Date,T=new R(2026,10,3,9,41,0).getTime();" +
    "class D extends R{constructor(...a){super(...(a.length?a:[T]))}static now(){return T}}window.Date=D;})();<\/script>" +
    "<style>*,*::before,*::after{transition:none!important;animation:none!important}</style>";

  // "早安", the widget written in the code panel
  const hi = el("div", "hi", inner);
  const hiB = el("b", "", hi); hiB.textContent = T.hi.title;
  const hiS = el("span", "", hi); hiS.textContent = T.hi.line;
  // 護眼提醒, installed from the market
  const eye = el("div", "w", inner);
  const eyeImg = el("img", "", eye); eyeImg.src = marketPic("hmgvibe.eyebreak");

  // snapping guides and the cursor
  const LC = layer("cursor");
  // the guides live on screen, snapped to whole pixels, so the dashes stay crisp and still
  const guides = { left: el("div", "guide v", LC), top: el("div", "guide h", LC), right: el("div", "guide v", LC) };
  const cursor = el("div", "cursor", LC,
    `<svg viewBox="0 0 24 24" width="46" height="46"><path d="M4 2.5v17.2l4.6-4.3 2.9 6.6 2.9-1.3-2.9-6.5h6.3z" fill="#fff" stroke="#111316" stroke-width="1.4" stroke-linejoin="round"/></svg>`);
  const ripple = el("div", "ripple", LC);

  function deskRect(t) {
    if (t < 12) return mixRect(tileScreen(flow.dive, t), { ...DESK, r: 28 }, prog(t, 10.6, 1.4, ease.inOut));
    const R4 = { x: 96, y: 236, w: 880, h: DH, r: 28 };
    if (t < 33) return mixRect({ ...DESK, r: 28 }, R4, prog(t, 28.0, 0.9, ease.inOut));
    return mixRect(R4, { ...DESK, r: 28 }, prog(t, 37.1, 0.9, ease.inOut));
  }
  /** Where the desk's content sits in its window: zoom z around a focus point (desk units). */
  function deskView(t, r) {
    const cover = Math.max(r.w / DW, r.h / DH);
    let p = 0;
    if (t >= 28 && t < 33) p = prog(t, 28.0, 0.9, ease.inOut);
    else if (t >= 33) p = 1 - prog(t, 37.1, 0.9, ease.inOut);
    // ④ frames just the right column: the calendar, and the slot where the new widget appears
    const focus = { x: lerp(DW / 2, 1428, p), y: lerp(DH / 2, 512, p) };
    // a slow push-in while the desk is on screen, so the picture never stands still
    const push = 1 + 0.025 * prog(t, 12, 16, ease.soft);
    const z = lerp(cover * push, 1.45 + 0.06 * prog(t, 29, 8.5, ease.soft), p); // ④ keeps drifting in too
    return { z, ox: r.w / 2 - focus.x * z, oy: r.h / 2 - focus.y * z };
  }
  const themeAt = (t) => (t < 14 ? 0 : t < 16 ? 1 : t < 18 ? 2 : 0);
  const SWITCH = [14, 16, 18];

  function placeW(w, x, y, wd, extra = {}) {
    const h = wd * (w.h / w.w);
    style(w.el, { left: px(x), top: px(y), width: px(wd), height: px(h), ...extra });
  }

  function renderDesk(t) {
    const on = t >= 10.6 && t < 46;
    show(LD, on);
    show(LC, on);
    if (!on) return;
    const r = deskRect(t);
    style(desk, { left: px(r.x), top: px(r.y), width: px(r.w), height: px(r.h), borderRadius: px(r.r) });
    const v = deskView(t, r);
    style(inner, { transform: `translate(${px(v.ox)}, ${px(v.oy)}) scale(${v.z.toFixed(5)})` });
    style(face, { opacity: (1 - prog(t, 10.75, 0.6, ease.soft)).toFixed(4) });
    style(deskDim, { opacity: (0.6 * (prog(t, 38.7, 0.7, ease.soft) - prog(t, 42.9, 0.7, ease.soft))).toFixed(4) });

    // walls and widgets follow the theme; each switch lands on a downbeat
    const th = themeAt(t);
    walls.forEach((w, i) => {
      let o = 0;
      if (i === th) o = 1;
      for (const s of SWITCH) { if (t >= s && t < s + 0.6) { const p = prog(t, s, 0.6, ease.soft); const prev = themeAt(s - 0.01); o = i === th ? p : i === prev ? 1 : 0; } }
      style(w, { opacity: o.toFixed(4) });
    });
    const order = ["clock", "weather", "calendar", "sysinfo", "media"];
    order.forEach((f, k) => {
      const w = widgets[f];
      let flip = 1;
      for (const s of SWITCH) flip -= 0.05 * bump(prog(t, s + k * 0.05 - 0.1, 0.45, ease.linear));
      w.imgs.forEach((img, i) => {
        let o = i === th ? 1 : 0;
        for (const s of SWITCH) {
          const p = prog(t, s + k * 0.05, 0.22, ease.soft);
          if (t >= s + k * 0.05 && t < s + k * 0.05 + 0.22) { const prev = themeAt(s - 0.01); o = i === th ? p : i === prev ? 1 - p : 0; }
        }
        style(img, { opacity: o.toFixed(4) });
      });
      w.flip = flip;
    });
    // the clock dips out for a moment while its theme changes (it's the real widget)
    let dip = 0;
    for (const s of SWITCH) dip = Math.max(dip, bump(prog(t, s - 0.08, 0.36, ease.linear)));
    style(clockFrame, { opacity: (1 - dip).toFixed(4) });

    placeW(widgets.clock, 88, 88, 560, { transform: `scale(${widgets.clock.flip.toFixed(4)})` });
    placeW(widgets.weather, 676, 88, 448, { transform: `scale(${widgets.weather.flip.toFixed(4)})` });
    placeW(widgets.sysinfo, 676, 396, 448, { transform: `scale(${widgets.sysinfo.flip.toFixed(4)})` });
    placeW(widgets.calendar, 1220, 88, 420, { transform: `scale(${widgets.calendar.flip.toFixed(4)})` });
    renderDrag(t);
    renderHi(t);
    renderEye(t);
  }

  // ③ dragging and resizing the media widget (20–28 s), desk units
  const DRAG = {
    pos: [[21.4, [150, 470]], [21.95, [250, 432], ease.inOut], [22.35, [95, 402], ease.inOut], [22.6, [88, 396], ease.click]],
    lift: [[21.35, 0], [21.6, 1, ease.out], [22.9, 1], [23.2, 0, ease.click]],
    width: [[24.05, 476], [24.85, 552, ease.inOut], [25.1, 560, ease.click]],
    grab: [300, 110],
  };
  function renderDrag(t) {
    const m = widgets.media;
    const [x, y] = keys(t, DRAG.pos);
    const wd = keys(t, DRAG.width);
    const lift = Math.max(keys(t, DRAG.lift), keys(t, [[24.0, 0], [24.15, 0.6, ease.out], [25.3, 0.6], [25.55, 0, ease.click]]));
    placeW(m, x, y, wd, { transform: `scale(${(m.flip * (1 + 0.03 * lift)).toFixed(4)})`, zIndex: lift > 0 ? 3 : 1 });
    style(m.shadow, { opacity: lift.toFixed(4) });
    // guides: half a gap outside the edges that lined up
    const g1 = prog(t, 22.38, 0.12, ease.linear) * (1 - prog(t, 25.6, 0.4, ease.soft));
    const g2 = prog(t, 22.38, 0.12, ease.linear) * (1 - prog(t, 23.5, 0.4, ease.soft));
    const g3 = prog(t, 24.88, 0.12, ease.linear) * (1 - prog(t, 25.7, 0.4, ease.soft));
    const mh = wd * aspect("media");
    const S = (x, y) => deskToScreen(t, x, y).map(Math.round);
    const [lx, ty] = S(82, 70), [, by] = S(0, y + mh + 18), [rx] = S(654, 0), [gx0, gy] = S(70, 390), [gx1] = S(1124 + 18, 0);
    style(guides.left, { left: `${lx - 1}px`, top: `${ty}px`, height: `${by - ty}px`, opacity: g1.toFixed(4) });
    style(guides.top, { left: `${gx0}px`, top: `${gy - 1}px`, width: `${gx1 - gx0}px`, opacity: g2.toFixed(4) });
    style(guides.right, { left: `${rx - 1}px`, top: `${ty}px`, height: `${by - ty}px`, opacity: g3.toFixed(4) });
  }

  // ④ "早安": typed in the code panel, then shown on the desk
  const CODE_HTML = [["t", '<div class="hi">'], ["", "\n  "], ["t", "<b>"], ["", "早安"], ["t", "</b>"], ["", "\n  "], ["t", "<span>"], ["", "今天也要好好的"], ["t", "</span>"], ["", "\n"], ["t", "</div>"]];
  const CODE_CSS = [["k", ".hi b"], ["", " {\n  "], ["p", "font-size"], ["", ": "], ["s", "64px"], ["", ";\n  "], ["p", "color"], ["", ": "], ["s", "var(--w-accent)"], ["", ";\n}\n"],
    ["k", ":root"], ["", " {\n  "], ["p", "--w-accent"], ["", ": "], ["s", "#ffd166"], ["", ";\n  "], ["p", "--w-radius"], ["", ": "], ["s", "32px"], ["", ";\n}"]];
  const CPS = 34, HTML_AT = 29.1, CSS_AT = 31.9;
  const flat = (tokens) => tokens.map((x) => x[1]).join("");
  const doneAt = (tokens, start, upto) => start + (flat(tokens).indexOf(upto) + upto.length) / CPS;
  const HI_SHOW = doneAt(CODE_HTML, HTML_AT, "</div>") + 0.15;
  const HI_SIZE = doneAt(CODE_CSS, CSS_AT, "64px;");
  const HI_ACCENT = doneAt(CODE_CSS, CSS_AT, "var(--w-accent);");
  const HI_YELLOW = doneAt(CODE_CSS, CSS_AT, "#ffd166;");
  const HI_RADIUS = doneAt(CODE_CSS, CSS_AT, "32px;\n}");

  function renderHi(t) {
    const visible = t >= HI_SHOW - 0.01 && t < 39.45;
    // opacity, not visibility: a child set "visible" would show through the hidden desk layer
    if (!visible) { style(hi, { opacity: "0" }); return; }
    const pop = prog(t, HI_SHOW, 0.45, ease.click);
    const fs = lerp(40, 64, prog(t, HI_SIZE, 0.45, ease.click));
    const toMint = prog(t, HI_ACCENT, 0.35, ease.soft), toYellow = prog(t, HI_YELLOW, 0.45, ease.soft);
    const col = [0, 1, 2].map((i) => lerp(lerp(rgb("#e9ecef")[i], rgb("#7de2c4")[i], toMint), rgb("#ffd166")[i], toYellow));
    const rad = lerp(22, 32, prog(t, HI_RADIUS, 0.45, ease.click));
    style(hi, {
      left: px(SLOT.x), top: px(SLOT.y), width: px(SLOT.w), height: px(SLOT.h), borderRadius: px(rad),
      opacity: clamp(pop * 2).toFixed(4), transform: `scale(${lerp(0.9, 1, pop).toFixed(4)})`,
    });
    style(hiB, { fontSize: px(fs), color: css(col) });
  }

  // ⑤ 護眼提醒 lands where 早安 was
  const EYE_LAND = 44.1;
  function renderEye(t) {
    const on = t >= EYE_LAND;
    if (!on) { style(eye, { opacity: "0" }); return; }
    const s = 1 + 0.04 * (1 - prog(t, EYE_LAND, 0.35, ease.click));
    style(eye, { opacity: "1", left: px(SLOT.x), top: px(SLOT.y), width: px(SLOT.w), height: px(SLOT.w / 2), transform: `scale(${s.toFixed(4)})` });
  }

  // ───────────────────────── ② the theme switcher (12–20 s) ─────────────────────────
  const seg = el("div", "seg", LH);
  const segKnob = el("div", "knob", seg);
  const segItems = T.s2.themes.map((n) => { const s = el("span", "", seg); s.textContent = n; return s; });
  function renderSeg(t) {
    const on = t >= 12.6 && t < 20.3;
    show(seg, on);
    if (!on) return;
    const a = prog(t, 12.75, 0.7, ease.slide);
    const out = prog(t, 19.85, 0.35, ease.in);
    style(seg, { right: "96px", top: "106px", opacity: (a * (1 - out)).toFixed(4), transform: `translateY(${px(18 * (1 - a) - 18 * out)})` });
    // the knob slides to each theme on the beat, with the control center's "click"
    const xs = segItems.map((s) => [s.offsetLeft, s.offsetWidth]);
    const k = keys(t, [[13.9, 0], [14.2, 1, ease.click], [15.9, 1], [16.2, 2, ease.click], [17.9, 2], [18.2, 0, ease.click]]);
    const base = Math.min(1, Math.floor(clamp(k, 0, 2))), f = k - base; // f may overshoot a little: the "click"
    const lo = xs[base], hiX = xs[base + 1];
    const x = lerp(lo[0], hiX[0], f), w = lerp(lo[1], hiX[1], f);
    style(segKnob, { left: px(x), width: px(w) });
    segItems.forEach((s, j) => style(s, { color: Math.round(clamp(k, 0, 2)) === j ? "#111316" : "#5b6168" }));
  }

  // ───────────────────────── ④ the code panel (28–38 s) ─────────────────────────
  const code = el("div", "code", LD);
  const tabs = el("div", "tabs", code);
  const tabEls = ["widget.json", "index.html", "style.css"].map((n) => { const s = el("span", "", tabs); s.textContent = n; return s; });
  const pre = el("pre", "", code);
  const CODE_RECT = { x: 1008, y: 236, w: 816, h: DH };
  const escape = (s) => s.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
  function codeHtml(tokens, n, caret) {
    let out = "", left = n;
    for (const [cls, text] of tokens) {
      if (left <= 0) break;
      const part = text.slice(0, left);
      left -= part.length;
      out += cls ? `<span class="${cls}">${escape(part)}</span>` : escape(part);
    }
    return out + (caret ? '<span class="caret"></span>' : "");
  }
  function renderCode(t) {
    const on = t >= 28 && t < 38.2;
    show(code, on);
    if (!on) return;
    const a = prog(t, 28.15, 0.9, ease.slide), out = prog(t, 37.05, 0.8, ease.inOut);
    style(code, {
      left: px(CODE_RECT.x + 900 * (1 - a) + 980 * out), top: px(CODE_RECT.y - 10 * prog(t, 29, 8.5, ease.soft)), width: px(CODE_RECT.w), height: px(CODE_RECT.h),
      opacity: clamp(a * 1.5).toFixed(4),
    });
    const isCss = t >= CSS_AT - 0.35;
    tabEls.forEach((s, i) => s.classList.toggle("on", i === (isCss ? 2 : 1)));
    const tokens = isCss ? CODE_CSS : CODE_HTML;
    const start = isCss ? CSS_AT : HTML_AT;
    const total = flat(tokens).length;
    const n = Math.max(0, Math.min(total, Math.floor((t - start) * CPS)));
    const blink = n >= total ? Math.floor((t - start) * 2) % 2 === 0 : true; // steady while typing
    const html = codeHtml(tokens, n, blink);
    if (pre.__h !== html) { pre.__h = html; pre.innerHTML = html; }
  }

  // ───────────────────────── ⑤ the control center and the market (38–46 s) ─────────────────────────
  const cc = el("div", "cc", LD);
  const ccBar = el("div", "cc-bar", cc);
  el("img", "", ccBar).src = "../build/logo/logo.svg";
  el("span", "", ccBar).textContent = T.cc.title;
  const ccBody = el("div", "cc-body", cc);
  const side = el("div", "cc-side", ccBody);
  T.cc.nav.forEach((n, i) => { const s = el("span", i === 1 ? "on" : "", side); s.textContent = n; });
  const main = el("div", "cc-main", ccBody);
  const h = el("div", "cc-h", main);
  el("span", "", h).textContent = T.cc.heading;
  const ccTabs = el("div", "cc-tabs", h);
  T.cc.tabs.forEach((n, i) => { const s = el("span", i === 1 ? "on" : "", ccTabs); s.textContent = n; });
  const cards = el("div", "cards", main);
  const cardEls = T.market.map((m) => {
    const c = el("div", "card", cards);
    const p = el("div", "pic", c);
    el("img", "", p).src = marketPic(m.folder);
    const info = el("div", "info", c);
    el("b", "", info).textContent = m.name;
    el("span", "by", info).textContent = m.by;
    const row = el("div", "row", info);
    el("span", "", row).textContent = m.note;
    const btn = el("span", "btn", row); btn.textContent = T.cc.install;
    return { c, p, btn };
  });
  // the third card: 早安, just submitted
  const mine = el("div", "card", cards);
  const minePic = el("div", "pic", mine);
  const mineHi = el("div", "hi", minePic);
  el("b", "", mineHi).textContent = T.hi.title;
  el("span", "", mineHi).textContent = T.hi.line;
  const mineInfo = el("div", "info", mine);
  el("b", "", mineInfo).textContent = T.hi.title;
  el("span", "by", mineInfo).textContent = T.cc.you;
  const mineRow = el("div", "row", mineInfo);
  el("span", "", mineRow).textContent = "";
  const pill = el("span", "", mineRow); pill.textContent = T.cc.reviewing;
  style(pill, { color: "#ffd166", fontWeight: "600" });
  const ccDim = el("div", "dim", cc);
  const sheet = el("div", "sheet", cc);
  sheet.innerHTML = `<div class="sheet-head"><div class="sheet-icon"></div><div><b>${T.sheet.title}</b><span>${T.sheet.by}</span></div></div>
    <p>${T.sheet.needs}</p><ul>${T.sheet.items.map((i) => `<li><i></i>${i}</li>`).join("")}</ul>
    <p class="no">${T.sheet.no}</p><div class="sheet-btns"><span>${T.sheet.cancel}</span><b>${T.sheet.ok}</b></div>`;
  const sheetOk = sheet.querySelector(".sheet-btns b");
  const CC = { x: 330, y: 262, w: 1260, h: 730 };
  const flyHi = el("div", "hi", LD); // 早安 on its way into the market
  el("b", "", flyHi).textContent = T.hi.title;
  el("span", "", flyHi).textContent = T.hi.line;
  const flyEye = el("img", "abs", LD);
  flyEye.src = marketPic("hmgvibe.eyebreak");

  /** An element's box inside the control center window (layout, unaffected by any scaling). */
  function within(node, root) {
    let x = 0, y = 0, n = node;
    while (n && n !== root) { x += n.offsetLeft; y += n.offsetTop; n = n.offsetParent; }
    return { x, y, w: node.offsetWidth, h: node.offsetHeight };
  }
  let geo = null; // measured once
  function measure() {
    style(cc, { left: px(CC.x), top: px(CC.y), width: px(CC.w), height: px(CC.h) });
    const pic1 = within(cardEls[0].p, cc), btn1 = within(cardEls[0].btn, cc), pic3 = within(minePic, cc);
    style(sheet, { left: px((CC.w + 250 - 640) / 2), top: px(150) });
    const ok = within(sheetOk, cc);
    style(mineHi, { left: "50%", top: "50%", width: px(SLOT.w), height: px(SLOT.h), margin: `${px(-SLOT.h / 2)} 0 0 ${px(-SLOT.w / 2)}`, transform: `scale(${((pic3.w * 0.8) / SLOT.w).toFixed(4)})`, color: "#e9ecef", borderRadius: "32px" });
    mineHi.querySelector("b").style.cssText = "font-size:64px;color:#ffd166";
    geo = { pic1, btn1, pic3, ok };
  }
  function renderCC(t) {
    const on = t >= 38.4 && t < 44;
    show(cc, on);
    if (!geo) measure();
    if (!on) {
      // children that were set visible would show through the hidden window: reset them
      show(flyHi, false); show(flyEye, false); show(sheet, false); style(mineHi, { opacity: "0" });
      return;
    }
    const up = prog(t, 38.55, 0.95, ease.slide), down = prog(t, 42.9, 0.8, ease.in);
    style(cc, { left: px(CC.x), top: px(CC.y + 820 * (1 - up) + 900 * down), width: px(CC.w), height: px(CC.h) });
    // 早安 flies from the desk into its card
    const fly = prog(t, 39.45, 0.85, ease.inOut);
    const ccTop = CC.y + 820 * (1 - up) + 900 * down;
    const [fx, fy] = deskToScreen(t, SLOT.x, SLOT.y), fz = deskView(t, deskRect(t)).z;
    const from = { x: fx, y: fy, w: SLOT.w * fz };
    const toW = geo.pic3.w * 0.8;
    const to = { x: CC.x + geo.pic3.x + (geo.pic3.w - toW) / 2, y: ccTop + geo.pic3.y + (geo.pic3.h - (toW * SLOT.h) / SLOT.w) / 2, w: toW };
    const flying = t >= 39.45 && t < 40.3;
    show(flyHi, flying);
    if (flying) {
      const k = lerp(from.w / SLOT.w, to.w / SLOT.w, fly);
      style(flyHi, {
        left: px(lerp(from.x, to.x, fly)), top: px(lerp(from.y, to.y, fly) - 90 * bump(fly)), width: px(SLOT.w), height: px(SLOT.h),
        transform: `scale(${k.toFixed(4)})`, transformOrigin: "0 0", borderRadius: "32px", zIndex: 5,
      });
      flyHi.querySelector("b").style.cssText = "font-size:64px;color:#ffd166";
    }
    style(mine, { opacity: prog(t, 40.1, 0.35, ease.soft).toFixed(4) });
    style(minePic, { opacity: "1" });
    style(mineHi, { opacity: t >= 40.3 ? "1" : "0" });
    // install: button press, the sheet, then the window goes and the widget flies home
    style(cardEls[0].btn, { transform: `scale(${(1 - 0.05 * bump(prog(t, 41.3, 0.22, ease.linear))).toFixed(4)})`, display: "inline-block" });
    const sheetIn = prog(t, 41.45, 0.42, ease.click), sheetOut = prog(t, 42.72, 0.25, ease.in);
    style(ccDim, { opacity: (prog(t, 41.4, 0.3, ease.soft) * (1 - sheetOut)).toFixed(4) });
    style(sheet, { opacity: (clamp(sheetIn * 2) * (1 - sheetOut)).toFixed(4), transform: `scale(${(lerp(0.92, 1, sheetIn) - 0.04 * sheetOut).toFixed(4)})`, visibility: t >= 41.45 && t < 43 ? "visible" : "hidden" });
    style(sheetOk, { transform: `scale(${(1 - 0.05 * bump(prog(t, 42.6, 0.22, ease.linear))).toFixed(4)})`, display: "inline-block" });
    const eyeFly = prog(t, 43.1, 1.0, ease.inOut);
    const eyeOn = t >= 43.1 && t < EYE_LAND;
    show(flyEye, eyeOn);
    if (eyeOn) {
      const src = { x: CC.x + geo.pic1.x + geo.pic1.w * 0.13, y: ccTop + geo.pic1.y + (geo.pic1.h - geo.pic1.w * 0.74 / 2) / 2, w: geo.pic1.w * 0.74 };
      const [dx, dy] = deskToScreen(t, SLOT.x, SLOT.y);
      const dst = { x: dx, y: dy, w: SLOT.w * deskView(t, deskRect(t)).z };
      style(flyEye, { left: px(lerp(src.x, dst.x, eyeFly)), top: px(lerp(src.y, dst.y, eyeFly) - 120 * bump(eyeFly)), width: px(lerp(src.w, dst.w, eyeFly)), zIndex: 5, filter: "drop-shadow(0 20px 30px rgba(0,0,0,.35))" });
    }
    style(cardEls[0].p.querySelector("img"), { opacity: t >= 43.1 ? "0.25" : "1" });
  }

  // ───────────────────────── the cursor (screen units) ─────────────────────────
  /** Desk units → screen, through the desk's window and zoom at time t. */
  function deskToScreen(t, x, y) {
    const r = deskRect(t), v = deskView(t, r);
    return [r.x + v.ox + x * v.z, r.y + v.oy + y * v.z];
  }
  function cursorPath(t) {
    const D = (x, y) => deskToScreen(t, x, y);
    if (t < 27) {
      const grip = () => { const wd = keys(t, DRAG.width); const [x, y] = keys(t, DRAG.pos); return D(x + wd - 14, y + wd * aspect("media") - 14); };
      if (t < 21.4) return { p: keys(t, [[20.55, D(980, 760)], [21.3, D(150 + DRAG.grab[0], 470 + DRAG.grab[1]), ease.inOut]]), o: prog(t, 20.55, 0.3) * 1 };
      if (t < 23.0) { const [x, y] = keys(t, DRAG.pos); return { p: D(x + DRAG.grab[0], y + DRAG.grab[1]), o: 1 }; }
      if (t < 24.0) return { p: keys(t, [[23.05, D(88 + DRAG.grab[0], 396 + DRAG.grab[1])], [23.9, D(88 + 476 - 14, 396 + 190.4 - 14), ease.inOut]]), o: 1 };
      if (t < 25.4) return { p: grip(), o: 1 };
      return { p: keys(t, [[25.6, D(88 + 560 - 14, 396 + 224 - 14)], [26.6, D(760, 760), ease.inOut]]), o: 1 - prog(t, 26.2, 0.5) };
    }
    if (!geo) return { p: [0, 0], o: 0 };
    const ccTop = CC.y; // the window is up while the cursor is around
    const btn = [CC.x + geo.btn1.x + geo.btn1.w / 2, ccTop + geo.btn1.y + geo.btn1.h / 2];
    const ok = [CC.x + geo.ok.x + geo.ok.w / 2, ccTop + geo.ok.y + geo.ok.h / 2];
    return {
      p: keys(t, [[40.4, [1180, 960]], [41.2, btn, ease.inOut], [41.9, btn], [42.5, ok, ease.inOut]]),
      o: prog(t, 40.4, 0.3) * (1 - prog(t, 42.95, 0.4)),
    };
  }
  const CLICKS = [21.35, 24.0, 41.3, 42.6];
  function renderCursor(t) {
    const { p, o } = cursorPath(t);
    let press = 0;
    for (const c of CLICKS) press = Math.max(press, bump(prog(t, c - 0.05, 0.2, ease.linear)));
    const held = (t >= 21.35 && t < 22.9) || (t >= 24.0 && t < 25.3);
    style(cursor, { left: px(p[0]), top: px(p[1]), opacity: o.toFixed(4), transform: `scale(${(1 - 0.12 * Math.max(press, held ? 1 : 0)).toFixed(4)})` });
    let rp = -1;
    for (const c of CLICKS) if (t >= c && t < c + 0.5) rp = (t - c) / 0.5;
    style(ripple, { left: px(p[0]), top: px(p[1]), opacity: rp < 0 ? "0" : (0.9 * (1 - rp)).toFixed(4), transform: `scale(${rp < 0 ? 0.3 : lerp(0.3, 1.2, ease.out(rp)).toFixed(4)})` });
  }

  // ───────────────────────── ⑥ free, open source (46–52 s) ─────────────────────────
  const L6 = layer("open", "dark");
  const says = T.s6.lines.map((s, i) => { const d = el("div", "say", L6); style(d, { top: px(150 + i * 186) }); return { d, ls: letters(d, s) }; });
  const zeros = T.s6.zeros.map((s, i) => { const z = el("div", "zero", L6, `<b>0</b><span>${s}</span>`); style(z, { left: px(140 + i * 150), top: px(770) }); return z; });
  const gpl = el("div", "gpl", L6); gpl.textContent = T.s6.gpl;
  const gplS = el("div", "gpl-s", L6); gplS.textContent = T.s6.repo;
  style(gpl, { left: px(760), top: px(784) });
  style(gplS, { left: px(764), top: px(926) });
  function renderOpen(t) {
    const on = t >= 46 && t < 52;
    show(L6, on);
    if (!on) return;
    const drift = -14 * prog(t, 46, 6, ease.linear);
    says.forEach((s, i) => { reveal(s.ls, t, 46.05 + i * 0.5, { stagger: 0.045, dur: 0.6, rise: 0.3, blur: 14 }); style(s.d, { transform: `translateY(${px(drift)})` }); });
    zeros.forEach((z, i) => {
      const p = prog(t, 48.0 + i * 0.25, 0.5, ease.click);
      style(z, { opacity: clamp(p * 2).toFixed(4), transform: `translateY(${px(drift + 30 * (1 - p))}) scale(${lerp(0.85, 1, p).toFixed(4)})` });
    });
    const g = prog(t, 49.0, 0.7, ease.slide), gs = prog(t, 49.35, 0.7, ease.slide);
    style(gpl, { opacity: g.toFixed(4), transform: `translateY(${px(drift + 26 * (1 - g))})`, filter: g >= 1 ? "none" : `blur(${(10 * (1 - g)).toFixed(2)}px)` });
    style(gplS, { opacity: gs.toFixed(4), transform: `translateY(${px(drift + 20 * (1 - gs))})` });
  }

  // ───────────────────────── ⑦ ending (52–60 s) ─────────────────────────
  const L7 = layer("end");
  const endWrap = el("div", "abs", L7);
  style(endWrap, { width: px(W), height: px(H), transformOrigin: "50% 45%" });
  const END_TILES = [
    { folder: "clock", wp: 0, shape: "stem", at: [110, 120, 380], rot: -5 },
    { folder: "weather", wp: 1, shape: "top", at: [1430, 96, 360], rot: 4 },
    { folder: "sysinfo", wp: 2, shape: "mid", at: [180, 640, 330], rot: 3 },
    { folder: "calendar", wp: 3, shape: "dot", at: [1560, 600, 270], rot: -4 },
    { folder: "media", wp: 5, at: [690, 880, 380], rot: 2 },
    { folder: "flipclock", wp: 4, at: [720, -20, 320], rot: -3 },
    { folder: "pomodoro", wp: 1, at: [1180, 800, 300], rot: 5 },
  ].map((e) => {
    const tile = { folder: e.folder, theme: "instrument", wp: e.wp, tw: e.at[2], th: (e.at[2] - 32) * aspect(e.folder) + 32 };
    if (e.shape) return { ...e, tile, sh: makeShape(endWrap, e.shape, tile) };
    const d = el("div", `tile wp${e.wp}`, endWrap);
    const img = el("img", "", d); img.src = pic(e.folder);
    return { ...e, tile, d };
  });
  const word7 = el("div", "wordmark", endWrap);
  const word7Ls = letters(word7, T.wordmark);
  const tag = el("div", "tagline", endWrap);
  const tagLs = letters(tag, T.s7.tagline);
  const cta = el("div", "cta", endWrap, `<b>${T.s7.cta}</b><span>${T.s7.url}</span><i>${T.s7.req}</i>`);

  function renderEnd(t) {
    const on = t >= 52;
    show(L7, on);
    if (!on) return;
    const size = 64 * LOGO_K;
    const ox = W / 2 - groupW / 2, oy = 430 - size / 2;
    const cx = W / 2, cy = 470;
    END_TILES.forEach((e, i) => {
      const [x0, y0, w0] = e.at;
      const h0 = e.tile.th;
      // drift in towards the middle, a little smaller and straighter
      const d = prog(t, 52.0, 1.6, ease.out);
      const rx = lerp(x0, cx - w0 / 2 + (x0 + w0 / 2 - cx) * 0.62, d), ry = lerp(y0, cy - h0 / 2 + (y0 + h0 / 2 - cy) * 0.62, d);
      const sc = lerp(1, 0.86, d), rot = e.rot * (1 - d);
      const tileR = { x: rx + (w0 * (1 - sc)) / 2, y: ry + (h0 * (1 - sc)) / 2, w: w0 * sc, h: h0 * sc, r: 18 * sc };
      const m = prog(t, 53.4 + i * 0.05, 1.05 - i * 0.05 + 0.05, ease.inOut);
      const appear = prog(t, 51.96 + i * 0.02, 0.12, ease.linear);
      if (e.sh) {
        const r = mixRect(tileR, logoRect(e.shape, ox, oy), m);
        const pulse = 1 + 0.035 * bump(prog(t, 54.5, 0.32, ease.linear));
        drawShape(e.sh, r, { tex: 1 - prog(t, 53.6, 0.7, ease.soft), img: 1 - prog(t, 53.55, 0.45, ease.soft), opacity: appear, transform: `rotate(${(rot * (1 - m)).toFixed(3)}deg) scale(${pulse.toFixed(4)})` });
      } else {
        // the rest fold into the middle of the logo and disappear
        const k = prog(t, 53.35, 0.85, ease.in);
        const lx = ox + size / 2, ly = oy + size / 2;
        const x = lerp(tileR.x, lx - tileR.w * 0.1, k), y = lerp(tileR.y, ly - tileR.h * 0.1, k);
        style(e.d, { left: px(x), top: px(y), width: px(tileR.w), height: px(tileR.h), opacity: (appear * (1 - k)).toFixed(4), transform: `rotate(${rot.toFixed(3)}deg) scale(${lerp(1, 0.2, k).toFixed(4)})`, borderRadius: px(tileR.r) });
      }
    });
    style(word7, { left: px(ox + size + 56), top: px(430 - 66) });
    reveal(word7Ls, t, 54.65, { stagger: 0.035, dur: 0.6, rise: 0.25, blur: 12 });
    style(tag, { top: px(590) });
    reveal(tagLs, t, 55.5, { stagger: 0.022, dur: 0.6, rise: 0.4, blur: 8 });
    const c = prog(t, 56.5, 0.8, ease.slide);
    style(cta, { top: px(690), opacity: c.toFixed(4), transform: `translateY(${px(24 * (1 - c))})` });
    style(endWrap, { transform: `scale(${(1 + 0.014 * prog(t, 56.5, 3.5, ease.soft)).toFixed(5)})` });
  }

  // ───────────────────────── setup and the frame ─────────────────────────
  async function ready() {
    groupW = 64 * LOGO_K + 56 + word0.offsetWidth;
    measure();
    const imgs = [...stage.querySelectorAll("img")];
    await Promise.all(imgs.map((i) => (i.complete ? Promise.resolve() : new Promise((r) => { i.onload = i.onerror = r; })).then(() => i.decode().catch(() => {}))));
    clockHost = await FWHost.mount(clockFrame, {
      folder: "clock", root: "../", locale: LANG, theme: "instrument", head: FIXED_TIME,
      settings: { showSeconds: false, showDate: true, use24h: true },
    });
    await document.fonts.ready;
  }

  /** Brings the live clock's theme in line with t; resolves once it has been restyled. */
  async function syncClock(t) {
    if (!clockHost) return;
    const want = THEMES[themeAt(t - 0.1)];
    if (want === clockTheme) return;
    clockTheme = want;
    await clockHost.setTheme(want);
    const doc = clockFrame.contentDocument;
    const link = doc && doc.getElementById("__fw_theme_link");
    for (let i = 0; i < 60 && link && !(link.sheet && link.href.includes(`/themes/${want}/`)); i++) await new Promise((r) => setTimeout(r, 16));
  }

  function render(t) {
    renderIntro(t);
    renderFlow(t);
    renderDesk(t);
    renderCode(t);
    renderCC(t);
    renderCursor(t);
    renderHeads(t);
    renderSeg(t);
    renderOpen(t);
    renderEnd(t);
    // ① and ⑦ are on the warm white page; ⑥ is graphite
    style(stage, { background: t >= 46 && t < 52 ? "#0e1013" : "#f5f4f0" });
  }

  window.SCENES = {
    ready, render, syncClock, duration: 60,
    marks: [[0, "logo"], [4, "38 個組件"], [12, "主題"], [20, "對齊"], [28, "寫組件"], [38, "市場"], [46, "開源"], [52, "結尾"]],
  };
})();
