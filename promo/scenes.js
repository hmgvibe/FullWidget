/* The promo: a 74-second tour of FullWidget in quick cuts on a 120 BPM grid (beat = 0.5 s).
   Every shot is a function of the time t; shots hand over on the beat, and things carry from one
   shot into the next (the logo bursts into widgets, the waterfall dives into the categories, the
   written widget becomes a file, the file becomes a widget, everything folds back into the logo).
     0     logo slams together, bursts into widgets     2   kinetic words
     5     the waterfall, 38 counted up                  8   six categories, 2.5 s each (live widgets)
     23    three themes, one per beat                    27  colour, radius, opacity
     31    resize: the content grows                     33  drag and snap
     36    layers                                        39  full screen: widgets step aside
     42    write a widget                                48  pack it as .fwz, double-click, allow
     51    the market                                    54  light: 0.4 % CPU
     57    three languages                               59  free, open source (graphite)
     66    everything folds back into the logo; download (to 74)
   Shots are hidden with display:none (not visibility), so nothing inside can show through. */
(function () {
  "use strict";
  const { W, H, clamp, lerp, ease, prog, keys, rgb, css, style, el, letters, reveal } = E;
  const T = window.COPY;
  const LANG = T.lang;
  const stage = document.getElementById("stage");
  const px = (v) => `${v.toFixed(2)}px`;
  const bump = (p) => Math.sin(Math.PI * clamp(p));
  const THEMES = ["instrument", "paper", "type"];
  const DURATION = 74;

  // widget pictures (2× renders from the website); the clock is always 9:41 (render.mjs --assets)
  const SIZE = {
    airquality: [600, 380], analogclock: [440, 440], battery: [480, 280], calculator: [520, 680], calendar: [600, 600],
    clock: [600, 300], countdown: [560, 360], currency: [600, 660], dailyimage: [800, 500], disks: [600, 360],
    flipclock: [640, 320], forecast: [720, 420], habits: [680, 380], infocard: [560, 340], label: [720, 240],
    launcher: [680, 320], media: [680, 272], netspeed: [600, 340], network: [600, 360], notes: [520, 460],
    onthisday: [680, 380], pcinfo: [640, 440], photoframe: [640, 480], pomodoro: [600, 340], power: [640, 220],
    progress: [600, 400], recyclebin: [480, 300], rss: [720, 480], search: [760, 180], sunmoon: [600, 380],
    sysinfo: [600, 420], timer: [600, 400], todo: [560, 600], usage: [640, 360], volume: [560, 220],
    weather: [640, 400], whitenoise: [600, 460], worldclock: [600, 400], "hmgvibe.eyebreak": [560, 280],
  };
  const BUILTIN = Object.keys(SIZE).filter((f) => !f.startsWith("hmgvibe."));
  const aspect = (f) => SIZE[f][1] / SIZE[f][0];
  const pic = (folder, theme = "instrument", lang = LANG) =>
    folder === "clock" ? `assets/clock-0941.${theme}.png`
      : folder.startsWith("hmgvibe.") ? `assets/market/${folder}/${theme}.${lang}.webp`
        : `../site/assets/widgets/${folder}/${theme}.${lang}.webp`;
  const LOGO = { cyan: "#4cc9f0", violet: "#9b5de5", yellow: "#ffd166", green: "#06d6a0" };
  const F = { stem: [4, 4, 18, 56, 7, LOGO.cyan], top: [26, 4, 34, 18, 7, LOGO.violet], mid: [26, 26, 20, 16, 7, LOGO.yellow], dot: [43, 43, 17, 17, 8.5, LOGO.green] };
  const K = 4.2, LSIZE = 64 * K;
  const logoRect = (n, ox, oy) => { const [x, y, w, h, r] = F[n]; return { x: ox + x * K, y: oy + y * K, w: w * K, h: h * K, r: r * K }; };
  const mixRect = (a, b, p) => ({ x: lerp(a.x, b.x, p), y: lerp(a.y, b.y, p), w: lerp(a.w, b.w, p), h: lerp(a.h, b.h, p), r: lerp(a.r, b.r, p) });

  // ───────────────────────── building blocks ─────────────────────────
  const shots = [];
  /** A shot shows from t0 to t1; render gets the global t and the local u = t − t0. */
  function shot(t0, t1, cls, build) {
    const root = el("div", `layer ${cls || ""}`, stage);
    style(root, { visibility: "visible", display: "none" });
    const s = { t0, t1, root, render: () => {} };
    Object.assign(s, build(root, s) || {});
    shots.push(s);
    return s;
  }
  const full = (parent, cls = "abs") => { const d = el("div", cls, parent); style(d, { left: "0px", top: "0px", width: px(W), height: px(H) }); return d; };
  const wallOf = (parent, theme) => { const d = el("div", `wall wall-${theme}`, parent); style(d, { position: "absolute", inset: "0" }); return d; };
  const box = (parent, cls, x, y, w, h) => { const d = el("div", cls, parent); style(d, { left: px(x), top: px(y), ...(w != null ? { width: px(w) } : {}), ...(h != null ? { height: px(h) } : {}) }); return d; };
  /** A widget picture (transparent, no shadow) at x, y, w wide. */
  function picture(parent, folder, x, y, w, theme = "instrument", lang = LANG) {
    const d = box(parent, "w shadowed", x, y, w, w * aspect(folder));
    const img = el("img", "", d); img.src = pic(folder, theme, lang);
    return { el: d, img, x, y, w, h: w * aspect(folder) };
  }
  /** The same picture in every theme, stacked, so a shot can switch themes. */
  function themed(parent, folder, x, y, w) {
    const d = box(parent, "w shadowed", x, y, w, w * aspect(folder));
    const imgs = THEMES.map((th) => { const i = el("img", "", d); i.src = pic(folder, th); return i; });
    return { el: d, imgs, h: w * aspect(folder) };
  }
  /** A website-style tile: wallpaper gradient with the widget on it. */
  function tile(parent, folder, wp, w, theme = "instrument") {
    const h = (w - 32) * aspect(folder) + 32;
    const d = el("div", `tile wp${wp}`, parent);
    style(d, { width: px(w), height: px(h) });
    const img = el("img", "", d); img.src = pic(folder, theme);
    return { el: d, img, w, h };
  }

  // live widgets (live.js): mounted in ready(), moved along every frame by sync()
  const LIVE = [];
  const LIVE_SIZE = { flipclock: [320, 160], analogclock: [220, 220], pomodoro: [300, 170], sysinfo: [300, 210], usage: [320, 180], netspeed: [300, 170], todo: [280, 300], calculator: [260, 340], media: [340, 136], whitenoise: [300, 230], clock: [300, 150] };
  function live(parent, o, x, y, w, h) {
    const d = box(parent, "w shadowed", x, y, w, h);
    const frame = el("iframe", "", d);
    frame.setAttribute("scrolling", "no");
    const L = { o, el: d, frame, w: null, theme: o.theme || "instrument", look: "" };
    LIVE.push(L);
    return L;
  }

  /** Big words that rise in letter by letter. */
  function words(parent, text, x, y, size, cls = "") {
    const d = el("div", `kin ${cls}`, parent);
    style(d, { left: px(x), top: px(y), fontSize: px(size) });
    return { el: d, ls: letters(d, text) };
  }
  /** The shot's headline at the top left (like the website's section titles); light = on a wallpaper. */
  function headline(parent, text, sub, light = false) {
    const node = el("div", `head${light ? " on-wall" : ""}`, parent);
    const ls = letters(el("div", "head-t", node), text);
    const s = el("div", "head-s", node);
    const lines = (sub == null ? [] : [].concat(sub)).map((x) => { const d = el("div", "", s); d.textContent = x; return d; });
    return (u, at = 0.05) => {
      reveal(ls, u, at, { stagger: 0.022, dur: 0.5, rise: 0.3, blur: 10 });
      reveal(lines, u, at + 0.2, { stagger: 0.08, dur: 0.6, rise: 0.6, blur: 8 });
    };
  }
  /** Shots enter from the right and leave to the left, with a little blur: a whip pan. */
  function whip(node, u, len, { enter = 0.42, leave = 0.24, dist = 300, extra = "" } = {}) {
    const a = enter ? ease.out(clamp(u / enter)) : 1, b = leave ? ease.in(clamp((u - (len - leave)) / leave)) : 0;
    const x = dist * (1 - a) - dist * 1.2 * b, blur = 12 * (1 - a) + 14 * b;
    style(node, { transform: `translateX(${px(x)}) ${extra}`, filter: blur > 0.05 ? `blur(${blur.toFixed(2)}px)` : "none" });
  }
  /** Things pop in: up from below with the "click" overshoot. */
  const pop = (u, at, d = 0.45) => prog(u, at, d, ease.click);
  function popIn(node, u, at, { dy = 40, s0 = 0.9, opacity = 1 } = {}) {
    const p = pop(u, at);
    style(node, { opacity: (clamp(p * 3) * opacity).toFixed(4), transform: `translateY(${px(dy * (1 - p))}) scale(${lerp(s0, 1, p).toFixed(4)})` });
    return p;
  }
  /** An element's box inside an ancestor, from layout (not affected by any scaling). */
  function within(node, root) {
    let x = 0, y = 0, n = node;
    while (n && n !== root) { x += n.offsetLeft; y += n.offsetTop; n = n.offsetParent; }
    return { x, y, w: node.offsetWidth, h: node.offsetHeight };
  }

  // the cursor and click ripples (one for the whole film, in screen units)
  const top = el("div", "layer", stage);
  style(top, { visibility: "visible", zIndex: "50" });
  const cursor = el("div", "cursor", top, `<svg viewBox="0 0 24 24" width="46" height="46"><path d="M4 2.5v17.2l4.6-4.3 2.9 6.6 2.9-1.3-2.9-6.5h6.3z" fill="#fff" stroke="#111316" stroke-width="1.4" stroke-linejoin="round"/></svg>`);
  const ripple = el("div", "ripple", top);
  const CURSOR = []; // [t0, t1, (t) => { p: [x, y], o, down }]
  const CLICKS = [];

  // ───────────────────────── ⓪ logo (0–2), bursting apart ─────────────────────────
  shot(0, 2, "", (root) => {
    const ox = W / 2 - LSIZE / 2, oy = H / 2 - LSIZE / 2;
    const shapes = Object.keys(F).map((n, i) => {
      const s = el("div", "shape", root);
      s.style.background = F[n][5];
      return { n, s, at: i * 0.25, dir: [[-1, -0.25], [0.85, -0.75], [1, 0.25], [0.55, 1]][i] };
    });
    return {
      render(t) {
        const burst = prog(t, 1.25, 0.75, ease.in);
        for (const sh of shapes) {
          const r = logoRect(sh.n, ox, oy);
          const p = prog(t, sh.at, 0.42, ease.click);
          const land = 1 + 0.08 * bump(prog(t, sh.at + 0.28, 0.2));
          const pulse = 1 + 0.04 * bump(prog(t, 1.0, 0.25));
          const g = 1 + 1.6 * burst;
          style(sh.s, {
            left: px(r.x), top: px(r.y), width: px(r.w), height: px(r.h), borderRadius: px(r.r),
            opacity: (clamp(p * 4) * (1 - burst)).toFixed(4),
            transform: `translate(${px(sh.dir[0] * 900 * burst)}, ${px(-460 * (1 - p) + sh.dir[1] * 900 * burst)}) scale(${(land * pulse * g).toFixed(4)}, ${((pulse * g) / land).toFixed(4)})`,
          });
        }
      },
    };
  });

  // ───────────────────────── ① widgets fly past; 桌面 / 也可以 / 很好看。 (1.25–5) ─────────────────────────
  const STREAM = ["clock", "weather", "sysinfo", "todo", "media", "calendar", "flipclock", "usage", "pomodoro", "forecast", "notes", "currency", "analogclock", "netspeed", "worldclock", "habits", "airquality", "whitenoise", "rss", "photoframe", "timer", "label"];
  shot(1.25, 5, "", (root) => {
    const field = full(root);
    const fly = STREAM.map((f, i) => {
      const t = tile(field, f, i % 6, 300);
      const a = i * 2.39996; // golden angle: an even spread
      return { ...t, start: 1.25 + (i < 6 ? i * 0.02 : 0.25 + (i - 6) * 0.17), dir: [Math.cos(a), Math.sin(a) * 0.72], spin: ((i * 37) % 11) - 5 };
    });
    const ws = T.words.map((w) => { const x = words(root, w, 0, H / 2 - 135, 230, "center"); style(x.el, { left: "0px", right: "0px" }); return x; });
    const AT = [2.0, 2.5, 3.0], LIFE = 2.3;
    const glow = el("div", "glow", root); // keeps the words readable over the stream
    return {
      render(t) {
        const warp = prog(t, 4.3, 0.7, ease.in); // the stream speeds up into the waterfall
        for (const f of fly) {
          const life = (t - f.start) / (LIFE * (1 - 0.45 * warp));
          if (life < 0 || life > 1) { style(f.el, { opacity: "0" }); continue; }
          // start around the words, not under them, and fly out past the camera
          const k = ease.in(life), dist = 420 + 1300 * k, sc = 0.3 + 1.6 * k;
          style(f.el, {
            left: px(W / 2 - f.w / 2 + f.dir[0] * dist), top: px(H / 2 - f.h / 2 + f.dir[1] * dist),
            transform: `scale(${sc.toFixed(4)}) rotate(${(f.spin * k).toFixed(2)}deg)`,
            opacity: (clamp(life * 8) * (1 - clamp((life - 0.82) / 0.18))).toFixed(4),
            filter: k > 0.55 ? `blur(${((k - 0.55) * 10).toFixed(2)}px)` : "none", zIndex: String(Math.round(k * 100)),
          });
        }
        style(glow, { opacity: (prog(t, 1.9, 0.3) * (1 - prog(t, 4.3, 0.4))).toFixed(4), zIndex: "150" });
        ws.forEach((w, i) => {
          const on = t >= AT[i] && t < (AT[i + 1] ?? 4.7);
          if (!on) { style(w.el, { opacity: "0" }); return; }
          reveal(w.ls, t, AT[i], { stagger: 0.03, dur: 0.38, rise: 0.22, blur: 16 });
          const out = i < 2 ? 0 : prog(t, 4.25, 0.35, ease.in);
          style(w.el, { opacity: (1 - out).toFixed(4), transform: `scale(${(1 + 0.04 * prog(t, AT[i], 1.2, ease.out) + 0.3 * out).toFixed(4)})`, zIndex: "200", filter: out > 0 ? `blur(${(out * 12).toFixed(2)}px)` : "none" });
        });
      },
    };
  });

  // ───────────────────────── ② the waterfall; 38 counted up; a dive into a time tile (5–8) ─────────────────────────
  const COLS = 6, PAD = 24, GAP = 20, COLW = (W - 2 * PAD - (COLS - 1) * GAP) / COLS;
  const hash = (s) => { let h = 2166136261; for (const ch of s) { h ^= ch.charCodeAt(0); h = Math.imul(h, 16777619); } return h >>> 0; };
  shot(5, 8, "", (root) => {
    const world = full(root);
    style(world, { transformOrigin: "0 0" });
    const order = [...BUILTIN].sort((a, b) => hash(a) - hash(b));
    const heights = new Array(COLS).fill(0), lists = Array.from({ length: COLS }, () => []);
    const themes = ["instrument", "paper", "instrument", "type"];
    order.forEach((f, i) => { const c = heights.indexOf(Math.min(...heights)); lists[c].push({ f, th: themes[i % 4], wp: i % 6 }); heights[c] += (COLW - 32) * aspect(f) + 32 + GAP; });
    let dive = null;
    const cols = lists.map((l, c) => {
      const col = el("div", "abs", world);
      let y = -900 - ((c * 173) % 400);
      for (const it of [...l, ...l, ...l]) {
        const t = tile(col, it.f, it.wp, COLW, it.th);
        style(t.el, { left: px(PAD + c * (COLW + GAP)), top: px(y) });
        if (!dive && c >= 2 && c <= 3 && y > 150 && y < 450) dive = { c, x: PAD + c * (COLW + GAP), y, w: COLW, h: t.h };
        y += t.h + GAP;
      }
      return col;
    });
    const fadeTop = el("div", "fade-top", root), fadeBottom = el("div", "fade-bottom", root);
    const num = el("div", "count", root);
    const unit = el("div", "count-u", root); unit.textContent = T.count.unit;
    const sub = el("div", "count-s", root); sub.textContent = T.count.sub;
    const SPEED = [70, 58, 82, 64, 76, 54];
    const drift = (c, u) => SPEED[c] * u + 640 * (1 - Math.exp(-2.6 * u)); // a rush that settles
    return {
      render(t, u) {
        cols.forEach((c, i) => style(c, { transform: `translateY(${px(drift(i, u))})` }));
        const dx = dive.x + dive.w / 2, dy = dive.y + drift(dive.c, u) + dive.h / 2;
        const z = keys(u, [[0, 1.18], [1.0, 1, ease.out], [2.3, 1.02, ease.linear], [3.0, 3.6, ease.in]]);
        const pd = prog(u, 2.3, 0.7, ease.in);
        const fx = lerp(W / 2, dx, pd), fy = lerp(H / 2, dy, pd);
        style(world, { transform: `translate(${px(W / 2 - fx * z)}, ${px(H / 2 - fy * z)}) scale(${z.toFixed(4)})`, filter: pd > 0.4 ? `blur(${((pd - 0.4) * 8).toFixed(2)}px)` : "none" });
        const band = prog(u, 0.2, 0.5, ease.soft) * (1 - prog(u, 2.25, 0.3, ease.soft));
        style(fadeTop, { opacity: band.toFixed(4) });
        style(fadeBottom, { opacity: band.toFixed(4) });
        num.textContent = String(Math.round(38 * prog(u, 0.3, 1.1, ease.out)));
        const a = prog(u, 0.25, 0.4, ease.out), out = prog(u, 2.2, 0.3, ease.in);
        [[num, 0], [unit, 0.06], [sub, 0.15]].forEach(([e, d0]) => style(e, { opacity: (prog(u, 0.25 + d0, 0.4, ease.out) * (1 - out)).toFixed(4), transform: `translateY(${px(30 * (1 - a) - 30 * out)})` }));
      },
    };
  });

  // ───────────────────────── ③ six categories, 2.5 s each, with live widgets (8–23) ─────────────────────────
  const PANEL = { x: 640, y: 110, w: 1184, h: 860 };
  const pomo = (remaining) => ({ state: { phase: "focus", running: true, endsAt: Live.BASE + remaining, remaining, done: 2, day: "2026-11-03", count: 2 } });
  const press = (k) => (w, d) => [...d.querySelectorAll("button")].find((b) => b.textContent.trim() === k)?.click();
  // [folder, x, y, w, live options or null] in panel units
  const CAT_ITEMS = [
    [["flipclock", 56, 70, 560, { speed: 60 }], ["analogclock", 660, 70, 470, { speed: 24, h: 470 }], ["pomodoro", 56, 400, 560, { speed: 90, storage: pomo(872000) }], ["countdown", 660, 545, 470, null]],
    [["weather", 56, 90, 540, null], ["forecast", 620, 90, 510, null], ["airquality", 56, 470, 540, null], ["sunmoon", 620, 450, 510, null]],
    [["sysinfo", 56, 70, 500, {}], ["usage", 600, 70, 528, { settings: { span: 60 } }], ["netspeed", 56, 470, 500, {}], ["disks", 600, 420, 528, null]],
    [["todo", 56, 70, 470, { h: 400, storage: { items: [{ id: "a", text: "回覆客戶郵件", done: true }, { id: "b", text: "整理會議記錄", done: false }, { id: "c", text: "預約牙醫", done: false }, { id: "d", text: "讀完第三章", done: false }] },
      actions: [[16.55, (w, d) => Live.click(d, '[data-id="b"] .w-check')], [17.05, (w, d) => Live.click(d, '[data-id="c"] .w-check')]] }],
      ["calculator", 620, 70, 420, { actions: ["1", "2", "8", "0", "×", "0", ".", "8", "5", "="].map((k, i) => [15.95 + i * 0.12, press(k)]) }],
      ["habits", 56, 500, 470, null]],
    [["rss", 56, 70, 560, null], ["currency", 660, 70, 470, null], ["onthisday", 56, 470, 560, null], ["dailyimage", 660, 590, 470, null]],
    [["media", 56, 90, 600, {}], ["whitenoise", 700, 90, 430, { script: "levels = { rain: 70, waves: 45, wind: 0, fire: 30, noise: 0 }; playing = true; render();" }],
      ["photoframe", 56, 400, 560, null], ["label", 700, 560, 430, null]],
  ];
  T.cats.forEach((cat, ci) => {
    const t0 = 8 + ci * 2.5;
    shot(t0, t0 + 2.5, "", (root) => {
      const wrap = full(root);
      const name = words(wrap, cat.name, 96, 250, 170, "cat");
      const cnt = box(wrap, "cat-n", 100, 470);
      cnt.innerHTML = `<b>${cat.n}</b> ${T.catUnit}`;
      const list = cat.list.map((n, i) => { const d = box(wrap, "cat-l", 100 + (i >= 5 ? 190 : 0), 560 + (i % 5) * 44); d.textContent = n; return d; });
      const panel = box(wrap, `panel wp${cat.wp}`, PANEL.x, PANEL.y, PANEL.w, PANEL.h);
      const items = CAT_ITEMS[ci].map(([f, x, y, w, o]) => {
        if (!o) return { f, item: picture(panel, f, x, y, w) };
        const [cw, ch] = LIVE_SIZE[f];
        return { f, item: live(panel, { folder: f, t0, ...o }, x, y, w, o.h || (w * ch) / cw) };
      });
      return {
        render(t, u) {
          whip(wrap, u, 2.5, { enter: 0.34 });
          reveal(name.ls, u, 0, { stagger: 0.05, dur: 0.38, rise: 0.25, blur: 14 });
          popIn(cnt, u, 0.12, { dy: 20, s0: 1 });
          list.forEach((d, i) => popIn(d, u, 0.18 + i * 0.03, { dy: 14, s0: 1 }));
          items.forEach(({ f, item }, k) => {
            const p = pop(u, 0.02 + k * 0.07, 0.45);
            let tf = `translateY(${px(46 * (1 - p) - 8 * Math.sin((u + k) * 1.3))}) scale(${lerp(0.88, 1, p).toFixed(4)})`;
            if (f === "dailyimage" || f === "photoframe") tf += ` scale(${(1 + 0.03 * u).toFixed(4)})`;
            style(item.el, { opacity: clamp(p * 3).toFixed(4), transform: tf });
            // the forecast's curve draws itself in
            if (f === "forecast") style(item.img, { clipPath: `inset(0 ${((1 - prog(u, 0.3, 1.1, ease.inOut)) * 100).toFixed(2)}% 0 0)` });
          });
        },
      };
    });
  });

  // ───────────────────────── ④ three themes, one per beat (23–27) ─────────────────────────
  const themeAt = (t) => (t < 23.5 || t >= 26 ? 0 : Math.floor((t - 23.5) / 0.5 + 1) % 3);
  shot(23, 27, "", (root) => {
    const walls = THEMES.map((th) => wallOf(root, th));
    const desk = full(root);
    style(desk, { transformOrigin: "50% 55%" });
    const clockL = live(desk, { folder: "clock", t0: 23, settings: { showSeconds: false }, themeAt: (t) => THEMES[themeAt(t - 0.04)] }, 150, 300, 680, 340);
    const ws = [themed(desk, "weather", 870, 300, 520), themed(desk, "calendar", 1430, 300, 360), themed(desk, "sysinfo", 870, 660, 520), themed(desk, "media", 150, 700, 620)];
    const hd = headline(root, T.themes.title, null, true);
    const names = T.themes.names.map((n) => { const d = el("div", "theme-name", root); d.textContent = n; return d; });
    return {
      render(t, u) {
        const th = themeAt(t);
        const beat = t >= 23.5 && t < 26.5 ? (t - 23.5) % 0.5 : 1; // time since the last switch
        walls.forEach((w, i) => style(w, { opacity: i === th ? "1" : "0" }));
        style(desk, { transform: `scale(${(1.06 - 0.06 * prog(u, 0, 4, ease.out)).toFixed(4)})` });
        ws.forEach((w, k) => {
          w.imgs.forEach((img, i) => style(img, { opacity: i === th ? "1" : "0" }));
          style(w.el, { transform: `scale(${(1 - 0.05 * bump(prog(beat, k * 0.03, 0.22, ease.linear))).toFixed(4)})` });
        });
        style(clockL.el, { transform: `scale(${(1 - 0.05 * bump(prog(beat, 0, 0.22, ease.linear))).toFixed(4)})` });
        hd(u);
        names.forEach((d, i) => {
          const p = i === th ? ease.out(clamp((t < 23.5 ? u : (t - 23.5) % 0.5) / 0.25)) : 0;
          style(d, { opacity: p.toFixed(4), transform: `translateY(${px(30 * (1 - p))})` });
        });
      },
    };
  });

  // ───────────────────────── ⑤ colour, radius, opacity (27–31) ─────────────────────────
  const ACCENTS = ["#7de2c4", "#ffd166", "#ff8fa3", "#4cc9f0", "#b69cff"];
  const lookAt = (t) => {
    const i = t < 27.6 ? 0 : t < 28.1 ? 1 : t < 28.6 ? 2 : t < 29.1 ? 3 : 4;
    const r = Math.round(keys(t, [[29.2, 22], [29.6, 6, ease.inOut], [30.1, 32, ease.inOut]]));
    const a = Math.round(keys(t, [[30.1, 74], [30.45, 30, ease.inOut], [30.8, 90, ease.inOut]]));
    return { i, r, a, css: `:root{--w-accent:${ACCENTS[i]};--w-radius:${r}px;--w-bg:rgba(16,18,22,${(a / 100).toFixed(2)});}` };
  };
  shot(27, 31, "", (root) => {
    wallOf(root, "instrument");
    const wrap = full(root);
    const panel = box(wrap, "look", 110, 300, 560);
    panel.innerHTML = `<div class="look-h">${T.look.panel}</div>` +
      `<div class="look-r"><span>${T.look.rows[0]}</span><div class="sw">${ACCENTS.map((c) => `<i style="background:${c}"></i>`).join("")}</div></div>` +
      `<div class="look-r"><span>${T.look.rows[1]}</span><div class="sl"><b></b><em></em></div><code></code></div>` +
      `<div class="look-r"><span>${T.look.rows[2]}</span><div class="sl"><b></b><em></em></div><code></code></div>`;
    const sw = [...panel.querySelectorAll(".sw i")];
    const [sl1, sl2] = [...panel.querySelectorAll(".sl")];
    const [c1, c2] = [...panel.querySelectorAll("code")];
    const look = (t) => lookAt(t).css;
    const ws = [
      live(wrap, { folder: "clock", t0: 27, settings: { showSeconds: false }, lookAt: look }, 760, 300, 600, 300),
      live(wrap, { folder: "pomodoro", t0: 27, speed: 60, storage: pomo(600000), lookAt: look }, 760, 640, 520, 295),
      live(wrap, { folder: "sysinfo", t0: 27, lookAt: look }, 1400, 300, 420, 294),
    ];
    const hd = headline(root, T.look.title, null, true);
    return {
      render(t, u) {
        whip(wrap, u, 4);
        hd(u);
        popIn(panel, u, 0, { dy: 30 });
        ws.forEach((w, k) => popIn(w.el, u, 0.06 + k * 0.07));
        const L = lookAt(t);
        sw.forEach((s, i) => style(s, { transform: `scale(${i === L.i ? 1.18 : 1})`, boxShadow: i === L.i ? "0 0 0 4px #14171c, 0 0 0 7px #e9ecef" : "none" }));
        style(sl1.querySelector("b"), { width: `${(L.r / 40) * 100}%` });
        style(sl1.querySelector("em"), { left: `${(L.r / 40) * 100}%` });
        c1.textContent = `${L.r}px`;
        style(sl2.querySelector("b"), { width: `${L.a}%` });
        style(sl2.querySelector("em"), { left: `${L.a}%` });
        c2.textContent = `${L.a}%`;
      },
    };
  });

  // ───────────────────────── ⑥ resize: the content grows (31–33) ─────────────────────────
  shot(31, 33, "", (root) => {
    const wrap = full(root);
    const panel = box(wrap, "panel wp0", 96, 236, 1728, 784);
    const L = live(panel, { folder: "clock", t0: 31, settings: { showSeconds: true } }, 140, 130, 360, 180);
    const grip = el("div", "grip", panel);
    const size = el("div", "size", panel);
    const hd = headline(root, T.grow.title);
    const WH = [[31.35, [360, 180]], [32.55, [920, 460], ease.inOut]];
    CURSOR.push([31.0, 33.0, (t) => {
      const [w, h] = keys(t, WH);
      return { p: t < 31.32 ? keys(t, [[31.0, [1200, 900]], [31.3, [96 + 140 + 352, 236 + 130 + 172], ease.inOut]]) : [96 + 140 + w - 8, 236 + 130 + h - 8], o: prog(t, 31.0, 0.2) * (1 - prog(t, 32.75, 0.2)), down: t >= 31.3 && t < 32.6 };
    }]);
    CLICKS.push(31.3);
    return {
      render(t, u) {
        whip(wrap, u, 2);
        hd(u);
        const [w, h] = keys(t, WH);
        style(L.el, { width: px(w), height: px(h) });
        style(grip, { left: px(140 + w - 22), top: px(130 + h - 22), opacity: (1 - prog(t, 32.6, 0.2)).toFixed(4) });
        size.textContent = `${Math.round(w / 1.2)} × ${Math.round(h / 1.2)}`;
        style(size, { left: "140px", top: px(130 + h + 20) });
      },
    };
  });

  // ───────────────────────── ⑦ drag and snap (33–36) ─────────────────────────
  shot(33, 36, "", (root) => {
    wallOf(root, "instrument");
    const wrap = full(root);
    picture(wrap, "clock", 200, 300, 560);
    picture(wrap, "weather", 788, 300, 448);
    picture(wrap, "sysinfo", 788, 608, 448);
    const med = picture(wrap, "media", 330, 700, 476);
    const cal = picture(wrap, "calendar", 1420, 470, 380);
    const g = { v1: el("div", "guide v", wrap), h1: el("div", "guide h", wrap), h2: el("div", "guide h", wrap) };
    const hd = headline(root, T.snap.title, null, true);
    const MED = [[33.45, [330, 700]], [33.95, [260, 640], ease.inOut], [34.3, [205, 612], ease.inOut], [34.5, [200, 608], ease.click]];
    const CAL = [[34.75, [1420, 470]], [35.25, [1300, 330], ease.inOut], [35.45, [1264, 304], ease.inOut], [35.65, [1264, 300], ease.click]];
    const gm = [240, 90], gc = [190, 120];
    CURSOR.push([33.0, 36.0, (t) => {
      if (t < 33.45) return { p: keys(t, [[33.0, [1000, 980]], [33.4, [330 + gm[0], 700 + gm[1]], ease.inOut]]), o: prog(t, 33.0, 0.15), down: false };
      if (t < 34.55) { const [x, y] = keys(t, MED); return { p: [x + gm[0], y + gm[1]], o: 1, down: true }; }
      if (t < 34.75) return { p: keys(t, [[34.55, [200 + gm[0], 608 + gm[1]]], [34.75, [1420 + gc[0], 470 + gc[1]], ease.inOut]]), o: 1, down: false };
      const [x, y] = keys(t, CAL);
      return { p: [x + gc[0], y + gc[1]], o: 1 - prog(t, 35.8, 0.2), down: t < 35.7 };
    }]);
    CLICKS.push(33.45, 34.75);
    return {
      render(t, u) {
        hd(u);
        const ap = prog(u, 0, 0.35, ease.out);
        style(wrap, { opacity: lerp(0.5, 1, ap).toFixed(4), transform: `scale(${lerp(1.06, 1, ap).toFixed(4)})` });
        const [mx, my] = keys(t, MED), [cx, cy] = keys(t, CAL);
        style(med.el, { left: px(mx), top: px(my), transform: `scale(${t >= 33.45 && t < 34.55 ? 1.03 : 1})`, zIndex: "3" });
        style(cal.el, { left: px(cx), top: px(cy), transform: `scale(${t >= 34.75 && t < 35.7 ? 1.03 : 1})`, zIndex: "3" });
        const s1 = prog(t, 34.3, 0.08) * (1 - prog(t, 35.0, 0.25)), s2 = prog(t, 35.45, 0.08) * (1 - prog(t, 35.85, 0.15));
        style(g.v1, { left: "193px", top: "282px", height: px(608 + med.h - 282 + 18), opacity: s1.toFixed(4) });
        style(g.h1, { left: "182px", top: "601px", width: px(1236 - 182 + 18), opacity: s1.toFixed(4) });
        style(g.h2, { left: "182px", top: "293px", width: px(1264 + 380 - 182 + 18), opacity: s2.toFixed(4) });
      },
    };
  });

  // ───────────────────────── ⑧ layers (36–39) ─────────────────────────
  shot(36, 39, "", (root) => {
    wallOf(root, "type");
    const wrap = full(root);
    const back = picture(wrap, "clock", 250, 330, 560, "type");
    const doc = box(wrap, "doc", 620, 260, 860, 600);
    doc.innerHTML = `<div class="doc-bar"><i></i>${T.layers.doc}</div><div class="doc-body">${[92, 78, 88, 64, 95, 70, 84, 58, 76, 90, 66, 82, 72].map((w) => `<p style="width:${w}%"></p>`).join("")}</div>`;
    const sel = el("div", "doc-sel", doc);
    const front = picture(wrap, "sysinfo", 1280, 560, 440);
    const through = picture(wrap, "media", 760, 700, 520);
    const tags = T.layers.tags.map((n, i) => { const d = box(wrap, "tag", [260, 1290, 770][i], [290, 520, 660][i]); d.textContent = n; return d; });
    const hd = headline(root, T.layers.title, null, true);
    CURSOR.push([37.3, 39.0, (t) => ({ p: keys(t, [[37.3, [1500, 1000]], [37.95, [900, 770], ease.inOut]]), o: prog(t, 37.3, 0.15) * (1 - prog(t, 38.7, 0.2)), down: false })]);
    CLICKS.push(38.05);
    return {
      render(t, u) {
        hd(u);
        whip(wrap, u, 3, { enter: 0.4 });
        popIn(back.el, u, 0);
        const win = prog(u, 0.2, 0.5, ease.slide);
        style(doc, { opacity: win.toFixed(4), transform: `translateY(${px(120 * (1 - win))})` });
        popIn(front.el, u, 0.55);
        popIn(through.el, u, 0.75, { opacity: t > 37.8 ? 0.72 : 1 });
        tags.forEach((d, i) => popIn(d, u, [0.4, 0.9, 1.35][i], { dy: 16, s0: 0.8 }));
        style(sel, { opacity: prog(t, 38.05, 0.15).toFixed(4) });
      },
    };
  });

  // ───────────────────────── ⑨ full screen: the widgets step aside (39–42) ─────────────────────────
  shot(39, 42, "", (root) => {
    wallOf(root, "instrument");
    const desk = full(root);
    const ws = [picture(desk, "clock", 160, 300, 560), picture(desk, "sysinfo", 1180, 300, 520), picture(desk, "media", 160, 680, 560), picture(desk, "weather", 1180, 690, 520)];
    const game = el("div", "game", root);
    game.innerHTML = `<div class="sky"></div><div class="sun"></div><div class="hills"></div><div class="road"></div>` + "<i></i>".repeat(18);
    const streaks = [...game.querySelectorAll("i")];
    const chip = el("div", "chip", root); chip.textContent = T.full.chip;
    const hd = headline(root, T.full.title, [T.full.sub], true);
    return {
      render(t, u) {
        const g = prog(t, 39.55, 0.45, ease.inOut) * (1 - prog(t, 41.3, 0.45, ease.inOut));
        const r = mixRect({ x: 660, y: 380, w: 600, h: 340, r: 16 }, { x: 0, y: 0, w: W, h: H, r: 0 }, g);
        style(game, { left: px(r.x), top: px(r.y), width: px(r.w), height: px(r.h), borderRadius: px(r.r), opacity: clamp(prog(t, 39.35, 0.2) * (1 - prog(t, 41.6, 0.2))).toFixed(4) });
        streaks.forEach((s, i) => {
          const k = (t * 1.7 + i * 0.137) % 1;
          style(s, { left: `${(((i * 53) % 100) + 0.5).toFixed(2)}%`, top: `${(52 + k * 50).toFixed(2)}%`, opacity: (k * (1 - k) * 3).toFixed(4), height: `${(3 + k * 14).toFixed(2)}%` });
        });
        const hide = prog(t, 39.6, 0.3, ease.in) * (1 - prog(t, 41.35, 0.35, ease.click));
        ws.forEach((w, k) => { const p = pop(u, k * 0.05); style(w.el, { opacity: (clamp(p * 3) * (1 - hide)).toFixed(4), transform: `scale(${(lerp(0.9, 1, p) * (1 - 0.15 * hide)).toFixed(4)})` }); });
        const c = prog(t, 40.1, 0.35, ease.click) * (1 - prog(t, 41.2, 0.2));
        style(chip, { opacity: clamp(c * 2).toFixed(4), transform: `translateY(${px(-20 * (1 - c))})` });
        hd(u);
      },
    };
  });

  // ───────────────────────── ⑩ write a widget (42–48) ─────────────────────────
  const CPS = 62;
  const CODE = [
    { tab: 1, at: 42.35, tokens: [["t", "<div"], ["", " "], ["p", "class"], ["", "="], ["s", '"card"'], ["t", ">"], ["", "\n  "], ["t", "<span>"], ["", "CPU"], ["t", "</span>"], ["", "\n  "], ["t", "<b"], ["", " "], ["p", "id"], ["", "="], ["s", '"v"'], ["t", ">"], ["", "--"], ["t", "</b>"], ["", "\n"], ["t", "</div>"]] },
    { tab: 2, at: 43.75, tokens: [["k", "widget"], ["", ".system.subscribe("], ["s", '"cpu"'], ["", ", (s) => {\n  v.textContent =\n    s.cpu.usage + "], ["s", '"%"'], ["", ";\n});"]] },
    { tab: 3, at: 45.45, tokens: [["k", "b"], ["", " { "], ["p", "color"], ["", ": "], ["s", "var(--w-accent)"], ["", "; }\n"], ["k", ":root"], ["", " {\n  "], ["p", "--w-accent"], ["", ": "], ["s", "#ffd166"], ["", ";\n}"]] },
  ];
  const flat = (tk) => tk.map((x) => x[1]).join("");
  const doneAt = (c) => c.at + flat(c.tokens).length / CPS;
  const CARD_SHOW = doneAt(CODE[0]) + 0.1, CARD_LIVE = doneAt(CODE[1]), CARD_YELLOW = doneAt(CODE[2]);
  const cpuAt = (t) => Math.round(Live.sys(Live.BASE + (t - 42) * 3000).cpu.usage);
  /** The card the code makes: "CPU" and a live figure, turning yellow with the last line. */
  function cpuCard(parent) {
    const c = el("div", "cpu", parent);
    el("span", "", c).textContent = "CPU";
    const b = el("b", "", c);
    return { el: c, b };
  }
  function drawCpu(card, t) {
    card.b.textContent = t >= CARD_LIVE ? `${cpuAt(t)}%` : "--";
    const y = prog(t, CARD_YELLOW, 0.4, ease.soft);
    card.b.style.color = css([0, 1, 2].map((i) => lerp(rgb("#e9ecef")[i], rgb("#ffd166")[i], y)));
  }
  const escape = (s) => s.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
  shot(42, 48, "", (root) => {
    const wrap = full(root);
    const view = box(wrap, "panel wall-instrument", 96, 236, 880, 784);
    const card = cpuCard(view);
    style(card.el, { left: "230px", top: "282px" });
    const code = box(wrap, "code", 1008, 236, 816, 784);
    const tabs = el("div", "tabs", code);
    const tabEls = ["widget.json", "index.html", "main.js", "style.css"].map((n) => { const s = el("span", "", tabs); s.textContent = n; return s; });
    const pre = el("pre", "", code);
    const hd = headline(root, T.code.title, T.code.sub);
    return {
      render(t, u) {
        hd(u);
        whip(wrap, u, 6, { leave: 0 });
        let c = CODE[0];
        for (const x of CODE) if (t >= x.at - 0.2) c = x;
        tabEls.forEach((s, i) => s.classList.toggle("on", i === c.tab));
        const n = Math.max(0, Math.min(flat(c.tokens).length, Math.floor((t - c.at) * CPS)));
        let out = "", left = n;
        for (const [cls, text] of c.tokens) { if (left <= 0) break; const part = text.slice(0, left); left -= part.length; out += cls ? `<span class="${cls}">${escape(part)}</span>` : escape(part); }
        out += '<span class="caret"></span>';
        if (pre.__h !== out) { pre.__h = out; pre.innerHTML = out; }
        const p = pop(t, CARD_SHOW);
        const tick = t >= CARD_LIVE ? bump(prog(t, CARD_LIVE, 0.3)) : 0;
        const away = prog(t, 47.55, 0.45, ease.in); // shrinks into a file (next shot)
        style(card.el, { opacity: (clamp(p * 3) * (1 - away)).toFixed(4), transform: `translate(${px(-300 * away)}, ${px(-120 * away)}) scale(${(lerp(0.85, 1, p) * (1 + 0.05 * tick) * (1 - 0.6 * away)).toFixed(4)})` });
        drawCpu(card, t);
        style(code, { opacity: (1 - prog(t, 47.6, 0.4)).toFixed(4), transform: `translateX(${px(160 * prog(t, 47.6, 0.4, ease.in))})` });
      },
    };
  });

  // ───────────────────────── ⑪ pack, double-click, allow (48–51) and ⑫ the market (51–54) ─────────────────────────
  const SHEET_AT = 49.45, OK_AT = 50.15, INSTALLED = 50.35, MARKET_CLICK = 52.35;
  shot(48, 54, "", (root) => {
    wallOf(root, "instrument");
    const desk = full(root);
    const exe = box(desk, "file", 70, 290);
    exe.innerHTML = `<div class="ico"><img src="../build/logo/logo.svg"></div><span>FullWidget.exe</span>`;
    const fwz = box(desk, "file", 70, 470);
    fwz.innerHTML = `<div class="ico"><img src="../build/logo/logo.svg"><em>.fwz</em></div><span>${T.pack.file}</span>`;
    const ws = [picture(desk, "clock", 300, 300, 500), picture(desk, "weather", 840, 300, 420), picture(desk, "media", 300, 610, 500)];
    const card = cpuCard(desk);
    style(card.el, { left: "1310px", top: "320px" });
    const eye = picture(desk, "hmgvibe.eyebreak", 1310, 600, 420);
    const dim = el("div", "dim", root);
    const sheet = box(root, "sheet", W / 2 - 320, 330);
    const S = T.pack.sheet;
    sheet.innerHTML = `<div class="sheet-head"><div class="sheet-icon"></div><div><b>${S.title}</b><span>${S.by}</span></div></div>
      <p>${S.needs}</p><ul>${S.items.map((i) => `<li><i></i>${i}</li>`).join("")}</ul>
      <p class="no">${S.no}</p><div class="sheet-btns"><span>${S.cancel}</span><b>${S.ok}</b></div>`;
    const okBtn = sheet.querySelector(".sheet-btns b");
    const M = T.market, CC = { x: 330, y: 250, w: 1260, h: 740 };
    const cc = box(root, "cc", CC.x, CC.y, CC.w, CC.h);
    cc.innerHTML = `<div class="cc-bar"><img src="../build/logo/logo.svg"><span>${M.cc.title}</span></div><div class="cc-body"><div class="cc-side">${M.cc.nav.map((n, i) => `<span class="${i === 1 ? "on" : ""}">${n}</span>`).join("")}</div>
      <div class="cc-main"><div class="cc-h"><span>${M.cc.heading}</span><div class="cc-tabs">${M.cc.tabs.map((n, i) => `<span class="${i === 1 ? "on" : ""}">${n}</span>`).join("")}</div></div><div class="cards">
      ${M.cards.map((c) => `<div class="card"><div class="pic"><img src="${pic(c.folder)}"></div><div class="info"><b>${c.name}</b><span class="by">${c.by}</span><div class="row"><span>${c.note}</span><span class="btn">${M.cc.install}</span></div></div></div>`).join("")}
      <div class="card"><div class="pic mine"></div><div class="info"><b>${M.mine.name}</b><span class="by">${M.mine.by}</span><div class="row"><span></span><span class="pill">${M.mine.status}</span></div></div></div></div></div></div>`;
    const mini = cpuCard(cc.querySelector(".pic.mine"));
    style(mini.el, { left: "50%", top: "50%", margin: "-105px 0 0 -210px", transform: "scale(0.62)" });
    mini.b.textContent = "21%";
    mini.b.style.color = "#ffd166";
    const cardEls = [...cc.querySelectorAll(".card")];
    const btn = cc.querySelector(".btn");
    const pic1 = cc.querySelector(".card .pic");
    const hdPack = headline(root, T.pack.title, null, true);
    const hdMarket = headline(root, M.title, null, true);
    let geo = null;
    CURSOR.push([48.6, 50.6, (t) => {
      const ok = geo ? [W / 2 - 320 + geo.ok.x + geo.ok.w / 2, 330 + geo.ok.y + geo.ok.h / 2] : [W / 2, 700];
      return { p: keys(t, [[48.6, [700, 900]], [49.0, [140, 520], ease.inOut], [49.6, [140, 520]], [50.05, ok, ease.inOut]]), o: prog(t, 48.6, 0.15) * (1 - prog(t, 50.4, 0.2)), down: false };
    }]);
    CLICKS.push(49.05, 49.22, OK_AT);
    CURSOR.push([51.6, 53.2, (t) => {
      const b = geo ? [CC.x + geo.btn.x + geo.btn.w / 2, CC.y + geo.btn.y + geo.btn.h / 2] : [W / 2, 700];
      return { p: keys(t, [[51.6, [1300, 1000]], [52.25, b, ease.inOut]]), o: prog(t, 51.6, 0.15) * (1 - prog(t, 52.9, 0.2)), down: false };
    }]);
    CLICKS.push(MARKET_CLICK);
    return {
      ready() { geo = { ok: within(okBtn, sheet), btn: within(btn, cc), pic1: within(pic1, cc) }; },
      render(t, u) {
        // the card from the code panel arrives as a file
        const f = prog(t, 48.0, 0.55, ease.inOut);
        style(fwz, { opacity: prog(t, 48.25, 0.3).toFixed(4), transform: `scale(${lerp(1.4, 1, f).toFixed(4)})` });
        style(exe, { opacity: prog(t, 48.05, 0.3).toFixed(4) });
        ws.forEach((w, k) => popIn(w.el, u, k * 0.06));
        const sIn = prog(t, SHEET_AT, 0.4, ease.click), sOut = prog(t, INSTALLED - 0.1, 0.25, ease.in);
        style(sheet, { opacity: (t >= SHEET_AT ? clamp(sIn * 2) * (1 - sOut) : 0).toFixed(4), transform: `scale(${(lerp(0.9, 1, sIn) - 0.05 * sOut).toFixed(4)})` });
        style(okBtn, { transform: `scale(${(1 - 0.06 * bump(prog(t, OK_AT, 0.2, ease.linear))).toFixed(4)})`, display: "inline-block" });
        // installed: the card lands on the desk, its figure live
        const land = pop(t, INSTALLED + 0.05, 0.5);
        style(card.el, { opacity: (t >= INSTALLED ? clamp(land * 3) : 0).toFixed(4), transform: `scale(${lerp(0.6, 1, land).toFixed(4)})` });
        drawCpu(card, t);
        // the market comes up, a widget is installed and flies home
        const up = prog(t, 51.0, 0.5, ease.slide), down = prog(t, 52.85, 0.45, ease.in);
        style(cc, { opacity: (t >= 50.95 && t < 53.4 ? 1 : 0).toFixed(4), transform: `translateY(${px(900 * (1 - up) + 950 * down)})` });
        cardEls.forEach((c, i) => popIn(c, t - 51, 0.2 + i * 0.1, { dy: 30 }));
        const pressed = t >= MARKET_CLICK;
        btn.textContent = pressed ? M.cc.added : M.cc.install;
        style(btn, { background: pressed ? "#2b3038" : "#7de2c4", color: pressed ? "#9aa2ac" : "#0b1512", transform: `scale(${(1 - 0.06 * bump(prog(t, MARKET_CLICK, 0.2, ease.linear))).toFixed(4)})`, display: "inline-block" });
        style(dim, { opacity: (0.5 * Math.max(t >= SHEET_AT ? clamp(sIn) * (1 - sOut) : 0, t >= 50.95 ? up * (1 - down) : 0)).toFixed(4) });
        const e = prog(t, 52.95, 0.8, ease.inOut);
        const sx = geo ? CC.x + geo.pic1.x + geo.pic1.w * 0.13 : 400, sy = geo ? CC.y + geo.pic1.y + 40 : 400, sw0 = geo ? geo.pic1.w * 0.74 : 260;
        style(eye.el, {
          opacity: (t >= 52.95 ? 1 : 0).toFixed(4),
          left: px(lerp(sx, 1310, e)), top: px(lerp(sy, 600, e) - 120 * bump(e)), width: px(lerp(sw0, 420, e)), height: px(lerp(sw0, 420, e) / 2),
          transform: `scale(${(1 + 0.04 * (1 - prog(t, 53.75, 0.3, ease.click))).toFixed(4)})`,
        });
        hdPack(t < 50.95 ? u : -1);
        hdMarket(t >= 50.95 ? t - 50.95 : -1);
      },
    };
  });

  // ───────────────────────── ⑬ light: 0.4 % (54–57) ─────────────────────────
  shot(54, 57, "", (root) => {
    const wrap = full(root);
    const grid = BUILTIN.map((f, i) => {
      const t = tile(wrap, f, i % 6, 132, THEMES[i % 3]);
      style(t.el, { left: px(96 + (i % 7) * 146), top: px(250 + Math.floor(i / 7) * 128), height: "116px", borderRadius: "14px" });
      t.img.style.cssText = "left:10px;top:10px;width:calc(100% - 20px)";
      return t;
    });
    const big = box(wrap, "big-num", 1150, 290);
    const lbl = box(wrap, "big-l", 1158, 560); lbl.textContent = T.light.label;
    const sub = box(wrap, "big-s", 1158, 620); sub.textContent = T.light.sub;
    const bar = box(wrap, "membar", 1158, 690, 560);
    bar.innerHTML = "<b></b>";
    return {
      render(t, u) {
        whip(wrap, u, 3, { extra: `scale(${(1 + 0.04 * prog(u, 0, 3, ease.soft)).toFixed(4)})` }); // keeps moving after the figure settles
        grid.forEach((g, i) => popIn(g.el, u, i * 0.014, { dy: 18, s0: 0.7 }));
        big.textContent = `${(0.4 * prog(u, 0.35, 1.0, ease.out)).toFixed(1)}%`;
        popIn(big, u, 0.3, { dy: 30, s0: 0.95 });
        popIn(lbl, u, 0.55, { dy: 20, s0: 1 });
        popIn(sub, u, 0.7, { dy: 20, s0: 1 });
        style(bar.querySelector("b"), { width: `${(prog(u, 0.9, 0.9, ease.slide) * 22).toFixed(2)}%` });
        style(bar, { opacity: prog(u, 0.8, 0.3).toFixed(4) });
      },
    };
  });

  // ───────────────────────── ⑭ three languages (57–59) ─────────────────────────
  const LANGS = ["zh-TW", "zh-CN", "en"];
  shot(57, 59, "", (root) => {
    const wrap = full(root);
    const titles = T.langs.titles.map((x) => { const w = words(wrap, x, 0, 200, 120, "center"); style(w.el, { left: "0px", right: "0px" }); return w; });
    const names = T.langs.names.map((x, i) => { const d = box(wrap, "lang-n", 560 + i * 280, 380, 240); d.textContent = x; return d; });
    const xs = [250, 760, 1270];
    const ws = ["calendar", "weather", "todo"].map((f, i) => LANGS.map((lg) => picture(wrap, f, xs[i], 480, 400, "instrument", lg)));
    const which = (t) => (t < 57.5 ? 0 : t < 58.0 ? 1 : t < 58.5 ? 2 : 0);
    return {
      render(t, u) {
        whip(wrap, u, 2, { enter: 0.35 });
        const k = which(t), since = t < 57.5 ? u : (t - 57) % 0.5;
        titles.forEach((w, i) => { if (i !== k) { style(w.el, { opacity: "0" }); return; } style(w.el, { opacity: "1" }); reveal(w.ls, since, 0, { stagger: 0.02, dur: 0.3, rise: 0.2, blur: 10 }); });
        names.forEach((d, i) => style(d, { opacity: i === k ? "1" : "0.28", transform: `scale(${i === k ? 1.06 : 1})` }));
        const p = ease.out(clamp(since / 0.22));
        ws.forEach((set, i) => set.forEach((w, j) => style(w.el, { opacity: j === k ? "1" : "0", transform: `perspective(900px) rotateX(${(j === k ? (1 - p) * 70 : 0).toFixed(2)}deg) translateY(${px(Math.sin((u + i) * 2) * 6)})` })));
      },
    };
  });

  // ───────────────────────── ⑮ free, open source (59–66, graphite) ─────────────────────────
  shot(59, 66, "dark", (root) => {
    const says = T.open.lines.map((s, i) => { const d = box(root, "say", 140, 140 + i * 186); return { d, ls: letters(d, s) }; });
    const zeros = T.open.zeros.map((s, i) => { const z = box(root, "zero", 140 + i * 150, 770); z.innerHTML = `<b>0</b><span>${s}</span>`; return z; });
    const gpl = box(root, "gpl", 760, 784); gpl.textContent = T.open.gpl;
    const repo = box(root, "gpl-s", 764, 926); repo.textContent = T.open.repo;
    return {
      render(t, u) {
        const drift = -16 * prog(u, 0, 7, ease.linear);
        says.forEach((s, i) => { reveal(s.ls, u, 0.04 + i * 0.5, { stagger: 0.04, dur: 0.45, rise: 0.3, blur: 16 }); style(s.d, { transform: `translateY(${px(drift)})` }); });
        zeros.forEach((z, i) => { const p = pop(u, 1.5 + i * 0.25, 0.45); style(z, { opacity: clamp(p * 2).toFixed(4), transform: `translateY(${px(drift + 30 * (1 - p))}) scale(${lerp(0.8, 1, p).toFixed(4)})` }); });
        const g = prog(u, 2.25, 0.6, ease.slide), r = prog(u, 2.6, 0.6, ease.slide);
        style(gpl, { opacity: g.toFixed(4), transform: `translateY(${px(drift + 26 * (1 - g))})`, filter: g >= 1 ? "none" : `blur(${(10 * (1 - g)).toFixed(2)}px)` });
        style(repo, { opacity: r.toFixed(4), transform: `translateY(${px(drift + 20 * (1 - r))})` });
      },
    };
  });

  // ───────────────────────── ⑯ everything folds back into the logo (66–74) ─────────────────────────
  shot(66, DURATION + 1, "", (root) => {
    const wrap = full(root);
    style(wrap, { transformOrigin: "50% 45%" });
    const END = [
      { folder: "clock", wp: 0, shape: "stem", at: [110, 120, 380], rot: -5 },
      { folder: "weather", wp: 1, shape: "top", at: [1430, 96, 360], rot: 4 },
      { folder: "sysinfo", wp: 2, shape: "mid", at: [180, 640, 330], rot: 3 },
      { folder: "calendar", wp: 3, shape: "dot", at: [1560, 600, 270], rot: -4 },
      { folder: "media", wp: 5, at: [690, 880, 380], rot: 2 },
      { folder: "flipclock", wp: 4, at: [720, -20, 320], rot: -3 },
      { folder: "pomodoro", wp: 1, at: [1180, 800, 300], rot: 5 },
    ].map((e) => {
      const t = tile(wrap, e.folder, e.wp, e.at[2]);
      const fill = el("div", "", t.el);
      style(fill, { position: "absolute", inset: "0", background: e.shape ? F[e.shape][5] : "transparent", opacity: "0" });
      return { ...e, ...t, fill };
    });
    const word = el("div", "wordmark", wrap);
    const wls = letters(word, T.wordmark);
    const tag = el("div", "tagline", wrap);
    const tls = letters(tag, T.end.tagline);
    const cta = el("div", "cta", wrap, `<b>${T.end.cta}</b><span>${T.end.url}</span><i>${T.end.req}</i>`);
    let groupW = 0;
    return {
      ready() { groupW = LSIZE + 56 + word.offsetWidth; },
      render(t, u) {
        const ox = W / 2 - groupW / 2, oy = 430 - LSIZE / 2, cx = W / 2, cy = 470;
        END.forEach((e, i) => {
          const [x0, y0, w0] = e.at, h0 = e.h;
          const d = prog(u, 0, 1.4, ease.out);
          const rx = lerp(x0, cx - w0 / 2 + (x0 + w0 / 2 - cx) * 0.62, d), ry = lerp(y0, cy - h0 / 2 + (y0 + h0 / 2 - cy) * 0.62, d);
          const sc = lerp(1, 0.86, d);
          const tileR = { x: rx + (w0 * (1 - sc)) / 2, y: ry + (h0 * (1 - sc)) / 2, w: w0 * sc, h: h0 * sc, r: 18 * sc };
          const appear = prog(u, -0.04 + i * 0.02, 0.12, ease.linear);
          if (e.shape) {
            const m = prog(u, 1.4 + i * 0.05, 1.1 - i * 0.05, ease.inOut);
            const r = mixRect(tileR, logoRect(e.shape, ox, oy), m);
            const pulse = 1 + 0.035 * bump(prog(u, 2.5, 0.32));
            style(e.el, { left: px(r.x), top: px(r.y), width: px(r.w), height: px(r.h), borderRadius: px(r.r), opacity: appear.toFixed(4), transform: `rotate(${(e.rot * (1 - d) * (1 - m)).toFixed(3)}deg) scale(${pulse.toFixed(4)})` });
            style(e.fill, { opacity: prog(u, 1.55, 0.7, ease.soft).toFixed(4) });
            style(e.img, { opacity: (1 - prog(u, 1.5, 0.45, ease.soft)).toFixed(4) });
          } else {
            const k = prog(u, 1.35, 0.85, ease.in);
            const lx = ox + LSIZE / 2, ly = oy + LSIZE / 2;
            style(e.el, { left: px(lerp(tileR.x, lx - tileR.w * 0.1, k)), top: px(lerp(tileR.y, ly - tileR.h * 0.1, k)), width: px(tileR.w), height: px(tileR.h), borderRadius: px(tileR.r), opacity: (appear * (1 - k)).toFixed(4), transform: `rotate(${(e.rot * (1 - d)).toFixed(3)}deg) scale(${lerp(1, 0.2, k).toFixed(4)})` });
          }
        });
        style(word, { left: px(ox + LSIZE + 56), top: px(430 - 66) });
        reveal(wls, u, 2.65, { stagger: 0.035, dur: 0.6, rise: 0.25, blur: 12 });
        style(tag, { top: "590px" });
        reveal(tls, u, 3.5, { stagger: 0.022, dur: 0.6, rise: 0.4, blur: 8 });
        const c = prog(u, 4.5, 0.8, ease.slide);
        style(cta, { top: "690px", opacity: c.toFixed(4), transform: `translateY(${px(24 * (1 - c))})` });
        style(wrap, { transform: `scale(${(1 + 0.016 * prog(u, 4.5, 3.5, ease.soft)).toFixed(5)})` });
      },
    };
  });

  // ───────────────────────── the frame ─────────────────────────
  function renderCursor(t) {
    let st = null;
    for (const [a, b, f] of CURSOR) if (t >= a && t < b) st = f(t);
    if (!st) { style(cursor, { opacity: "0" }); style(ripple, { opacity: "0" }); return; }
    let pr = 0;
    for (const c of CLICKS) pr = Math.max(pr, bump(prog(t, c - 0.05, 0.18, ease.linear)));
    style(cursor, { left: px(st.p[0]), top: px(st.p[1]), opacity: st.o.toFixed(4), transform: `scale(${(1 - 0.12 * Math.max(pr, st.down ? 1 : 0)).toFixed(4)})` });
    let rp = -1;
    for (const c of CLICKS) if (t >= c && t < c + 0.45) rp = (t - c) / 0.45;
    style(ripple, { left: px(st.p[0]), top: px(st.p[1]), opacity: rp < 0 ? "0" : (0.9 * (1 - rp) * st.o).toFixed(4), transform: `scale(${rp < 0 ? 0.3 : lerp(0.3, 1.2, ease.out(rp)).toFixed(4)})` });
  }

  function render(t) {
    for (const s of shots) {
      const on = t >= s.t0 && t < s.t1;
      style(s.root, { display: on ? "block" : "none" });
      if (on) s.render(t, t - s.t0);
    }
    renderCursor(t);
    style(stage, { background: t >= 59 && t < 66 ? "#0e1013" : "#f5f4f0" });
  }

  async function ready() {
    // measure with every shot laid out, then hide them again
    for (const s of shots) style(s.root, { display: "block" });
    for (const s of shots) if (s.ready) s.ready();
    const imgs = [...stage.querySelectorAll("img")];
    await Promise.all(imgs.map((i) => (i.complete ? Promise.resolve() : new Promise((r) => { i.onload = i.onerror = r; })).then(() => i.decode().catch(() => {}))));
    for (const L of LIVE) L.w = await Live.mount(L.frame, L.o);
    await document.fonts.ready;
    for (const s of shots) if (s.ready) s.ready(); // again, now that fonts are in
  }

  /** Moves every live widget to t: theme and look first (they redraw), then its clock. */
  async function sync(t) {
    for (const L of LIVE) {
      if (!L.w) continue;
      if (L.o.themeAt) {
        const th = L.o.themeAt(t);
        if (th !== L.theme) {
          L.theme = th;
          await L.w.setTheme(th);
          const link = L.frame.contentDocument && L.frame.contentDocument.getElementById("__fw_theme_link");
          for (let i = 0; i < 60 && link && !(link.sheet && link.href.includes(`/themes/${th}/`)); i++) await new Promise((r) => setTimeout(r, 16));
        }
      }
      if (L.o.lookAt) {
        const look = L.o.lookAt(t);
        if (look !== L.look) { L.look = look; await L.w.setLook(look); }
      }
      L.w.sync(t);
    }
  }

  window.SCENES = {
    ready, render, sync, duration: DURATION,
    marks: [[0, "logo"], [2, "字"], [5, "38"], [8, "時間"], [10.5, "天氣"], [13, "系統"], [15.5, "效率"], [18, "資訊"], [20.5, "媒體"], [23, "主題"], [27, "外觀"], [31, "拉大"], [33, "對齊"], [36, "圖層"], [39, "全螢幕"], [42, "寫組件"], [48, "打包"], [51, "市場"], [54, "輕"], [57, "語言"], [59, "開源"], [66, "結尾"]],
  };
})();
