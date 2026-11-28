/* The 1.5 update video: how widget cards look. Same format as 1.3 and 1.4 (number, title and one
   line on the left, the thing itself on the right). 120 BPM, beat = 0.5 s.
     0   FullWidget 1.5                 4   01 snapshot cards (experimental)
     17  02 shadows that listen         27  03 the stacking order stays
     34  04 the right-click menu        38  download (to 45)
   Every picture is a function of the time t (see engine.js). */
(function () {
  "use strict";
  const { W, clamp, lerp, ease, prog, keys, style, el, letters, reveal, conceal } = E;
  const stage = document.getElementById("stage");
  const px = (v) => `${v.toFixed(2)}px`;
  const bump = (p) => Math.sin(Math.PI * clamp(p));
  const DURATION = 45;

  // ───────────────────────── building blocks ─────────────────────────
  const shots = [];
  function shot(t0, t1, build) {
    const root = el("div", "layer", stage);
    style(root, { display: "none" });
    const s = { t0, t1, root, render: () => {} };
    Object.assign(s, build(root, s) || {});
    shots.push(s);
    return s;
  }
  const box = (parent, cls, x, y, w, h) => {
    const d = el("div", cls, parent);
    style(d, { left: px(x), top: px(y), ...(w != null ? { width: px(w) } : {}), ...(h != null ? { height: px(h) } : {}) });
    return d;
  };
  const pop = (u, at, d = 0.45) => prog(u, at, d, ease.click);
  function popIn(node, u, at, { dy = 30, s0 = 0.92 } = {}) {
    const p = pop(u, at);
    style(node, { opacity: clamp(p * 3).toFixed(4), transform: `translateY(${px(dy * (1 - p))}) scale(${lerp(s0, 1, p).toFixed(4)})` });
    return p;
  }
  const fadeIn = (node, u, at, d = 0.4, dy = 10) => { const p = prog(u, at, d, ease.out); style(node, { opacity: p.toFixed(4), transform: `translateY(${px(dy * (1 - p))})` }); return p; };
  /** Shown from a to b (video time), fading in and out. */
  const during = (node, t, a, b, d = 0.3) => { const o = prog(t, a, d, ease.out) * (1 - prog(t, b - d, d, ease.in)); style(node, { opacity: o.toFixed(4), transform: `translateY(${px(10 * (1 - prog(t, a, d, ease.out)))})`, display: o > 0.001 ? "" : "none" }); return o; };
  const hexA = (hex, a) => { const n = parseInt(hex.slice(1), 16); return `rgba(${(n >> 16) & 255},${(n >> 8) & 255},${n & 255},${a})`; };
  /** A value that moves to each new setting in 0.3 s: steps = [[time, value], …] after `start`. */
  const level = (t, start, steps, d = 0.3) => steps.reduce((v, [at, to]) => lerp(v, to, prog(t, at, d, ease.inOut)), start);
  /** Where a point of a node is on the stage (offsets only, so transforms in flight don't matter). */
  function at(node, fx = 0.5, fy = 0.5) {
    let x = node.offsetWidth * fx, y = node.offsetHeight * fy;
    for (let n = node; n && n !== stage; n = n.offsetParent) { x += n.offsetLeft; y += n.offsetTop; }
    return [x, y];
  }

  /** The wallpaper with an app window on it; drawn again, blurred, inside a snapshot card. */
  function wallpaper(parent, { win = true } = {}) {
    const w = el("div", "wall", parent);
    if (!win) return w;
    const a = el("div", "appwin", w);
    style(a, { left: "420px", top: "90px", width: "340px", height: "250px" });
    a.innerHTML = `<div class="bar"><i></i><i></i><i></i></div><p></p><p class="short"></p><p class="hl"></p><p></p><p class="short"></p>`;
    return w;
  }

  /** A widget card on a little desktop. set() takes its place, corners, shadow and snapshot. */
  function card(parent, { small = false, face, solid = "", win = true } = {}) {
    const c = el("div", `ccard${small ? " small" : ""}`, parent);
    const base = el("div", "base", c);
    if (solid) style(base, { background: solid });
    const snap = el("div", "snap", c);
    wallpaper(snap, { win });
    const tint = el("div", "tintl", c);
    const f = el("div", "face", c);
    f.innerHTML = face || `<small>11月3日 周二</small><b>09:41</b><div class="row"><span>第 307 天</span><span>第 45 周</span></div>`;
    const flash = el("div", "flash", c);
    const dot = el("div", "dot", c);
    return {
      el: c,
      set({ x, y, w, h, r = 18, sh = 1, snapA = 0, capX = x, capY = y, flashA = 0, dotA = 0, z }) {
        style(c, { left: px(x), top: px(y), width: px(w), height: px(h), "--r": px(r), zIndex: z == null ? "" : String(z),
          boxShadow: `0 18px 40px -8px rgba(0,0,0,${(0.62 * sh).toFixed(3)}), 0 4px 10px rgba(0,0,0,${(0.25 * sh).toFixed(3)})` });
        style(base, { opacity: (1 - snapA).toFixed(4) });
        style(snap, { opacity: snapA.toFixed(4), left: px(-capX), top: px(-capY) });
        style(tint, { opacity: snapA.toFixed(4) });
        style(flash, { opacity: flashA.toFixed(4) });
        style(dot, { opacity: dotA.toFixed(4) });
      },
    };
  }

  /** The widget's right-click menu (no drop shadow, as in 1.5). */
  function menu(parent) {
    const m = el("div", "pmenu", parent);
    m.innerHTML = `<div class="it hd"><div><b>时钟</b><small>桌面层 · 跟随全局</small></div></div><div class="sep"></div>
      <div class="it" data-k="card"><span>卡片</span><span class="seg"><i data-v="transparent">透明</i><i data-v="frosted">毛玻璃</i><i data-v="snapshot">快照</i></span></div>
      <div class="it"><span>阴影</span><span class="sw" style="--k:1"></span></div>
      <div class="it new" data-k="retake"><span>重新截取背景</span></div>
      <div class="sep"></div><div class="it"><span>设置…</span></div>`;
    const segs = [...m.querySelectorAll(".seg i")];
    const rows = { card: m.querySelector('[data-k="card"]'), retake: m.querySelector('[data-k="retake"]') };
    let last = "";
    return {
      el: m, rows, seg: (v) => segs.find((s) => s.dataset.v === v),
      set({ style: st = "transparent", retake = false, hl = "" }) {
        const key = `${st}|${retake}|${hl}`;
        if (key === last) return;
        last = key;
        segs.forEach((s) => s.classList.toggle("on", s.dataset.v === st));
        rows.retake.style.display = retake ? "" : "none";
        Object.entries(rows).forEach(([k, r]) => r.classList.toggle("hl", k === hl));
      },
    };
  }

  // the cursor and click ripples
  const top = el("div", "layer", stage);
  style(top, { zIndex: "50" });
  const cursor = el("div", "cursor", top, `<svg viewBox="0 0 24 24" width="46" height="46"><path d="M4 2.5v17.2l4.6-4.3 2.9 6.6 2.9-1.3-2.9-6.5h6.3z" fill="#fff" stroke="#111316" stroke-width="1.4" stroke-linejoin="round"/></svg>`);
  const ripple = el("div", "ripple", top);
  const CURSOR = []; // [t0, t1, (t) => { p: [x, y], o, down }]
  const CLICKS = [];
  // The part on the old frosted card was put in later, at 4 s for 5 s: the shots after it keep
  // their own times ("old" time) and are played 5 s later. The inserted shot and its cursor use
  // the video's time.
  const INS = 4, GAP = 5;
  const toOld = (t) => (t < INS ? t : t < INS + GAP ? null : t - GAP);
  const CURSOR_R = [], CLICKS_R = [];

  // ───────────────────────── chapters along the bottom ─────────────────────────
  // video time
  const CHAPTERS = [["快照卡片", 4, 22], ["阴影设置", 22, 32], ["叠放顺序", 32, 39], ["右键菜单", 39, 43]];
  const chap = el("div", "chap", top);
  const chapEls = CHAPTERS.map(([name]) => { const d = el("div", "", chap); const s = el("span", "", d); s.textContent = name; const u = el("u", "", d); const i = el("i", "", u); return { s, i }; });
  function renderChapters(t) {
    style(chap, { opacity: (prog(t, 4.1, 0.5) * (1 - prog(t, 42.7, 0.4))).toFixed(4) });
    CHAPTERS.forEach(([, a, b], k) => {
      const p = clamp((t - a) / (b - a));
      chapEls[k].s.classList.toggle("on", t >= a && t < b);
      style(chapEls[k].i, { width: `${(p * 100).toFixed(2)}%` });
    });
  }

  // ───────────────────────── a feature: text left, demo right ─────────────────────────
  const STAGE = { x: 860, y: 140, w: 920, h: 780 };
  function feature(t0, t1, { n, accent, title, desc, chips = [] }, demo) {
    return shot(t0, t1, (root) => {
      const len = t1 - t0;
      const glow = el("div", "glow", root);
      style(glow, { background: `radial-gradient(45% 55% at 72% 46%, ${hexA(accent, 0.2)}, transparent 72%), radial-gradient(40% 50% at 12% 30%, ${hexA(accent, 0.06)}, transparent 70%)` });
      const wrap = el("div", "layer", root);
      const num = box(wrap, "num", 150, 292);
      num.innerHTML = `<b style="color:${accent}">${n}</b><span> / 04　新版本</span>`;
      const tt = box(wrap, "title", 145, 340);
      const tl = letters(tt, title);
      const ds = box(wrap, "desc", 150, 474);
      ds.innerHTML = desc.split("|").map((l) => l.replace(/[&<>]/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;" })[c])).join("<br>");
      const cs = box(wrap, "chips", 150, 690);
      style(cs, { "--accent": accent });
      const chipEls = chips.map((c) => el("b", "", cs, c));
      const sb = box(wrap, "stagebox", STAGE.x, STAGE.y, STAGE.w, STAGE.h);
      const tint = el("div", "tint", sb);
      style(tint, { background: `radial-gradient(80% 70% at 50% 0%, ${hexA(accent, 0.16)}, transparent 70%)` });
      const d = demo(sb, { chips: chipEls, t0 }) || {};
      return {
        render(t, u) {
          style(glow, { opacity: (prog(u, 0, 0.6) * (1 - prog(u, len - 0.3, 0.3))).toFixed(4) });
          reveal([num], u, 0.05, { dur: 0.5, rise: 0.4, blur: 6 });
          reveal(tl, u, 0.1, { stagger: 0.035, dur: 0.55, rise: 0.25, blur: 12 });
          reveal([ds], u, 0.35, { dur: 0.7, rise: 0.25, blur: 8 });
          chipEls.forEach((c, i) => popIn(c, u, 0.55 + i * 0.08, { dy: 14, s0: 0.96 }));
          const s = prog(u, 0.05, 0.7, ease.slide);
          style(sb, { opacity: s.toFixed(4), transform: s < 1 ? `translateY(${px(50 * (1 - s))}) scale(${lerp(0.97, 1, s).toFixed(4)})` : "none" });
          if (d.render) d.render(t, u);
          conceal(wrap, u, len - 0.32, { dur: 0.32, lift: 16 });
        },
      };
    });
  }

  // ───────────────────────── intro (0–4) ─────────────────────────
  shot(0, 4, (root) => {
    const logo = el("img", "logo", root); logo.src = "../build/logo/logo.svg";
    style(logo, { left: px(W / 2 - 66), top: "250px" });
    const big = box(root, "big", 0, 410);
    const bl = letters(big, "FullWidget ");
    const em = el("em", "", big); em.textContent = "1.5";
    const sub = box(root, "sub", 0, 600);
    sub.textContent = "卡片的样子，由你决定";
    const m = box(root, "menu", 0, 720);
    const items = CHAPTERS.map(([name], i) => el("b", i === 0 ? "hot" : "", m, name));
    return {
      render(t, u) {
        popIn(logo, u, 0.0, { dy: 40, s0: 0.6 });
        reveal(bl, u, 0.45, { stagger: 0.04, dur: 0.5, rise: 0.25, blur: 14 });
        const e = prog(u, 0.95, 0.5, ease.click);
        style(em, { display: "inline-block", opacity: clamp(e * 3).toFixed(4), transform: `translateY(${px(30 * (1 - e))}) scale(${lerp(0.7, 1, e).toFixed(4)})` });
        reveal([sub], u, 1.5, { dur: 0.6, rise: 0.3, blur: 8 });
        items.forEach((b, i) => popIn(b, u, 2.0 + i * 0.12, { dy: 18 }));
        conceal(root, u, 3.6, { dur: 0.4, lift: 24 });
      },
    };
  });

  // ───────────────────────── 01a the old frosted card (video time 4–9) ─────────────────────────
  feature(INS, INS + GAP, {
    n: "01", accent: "#ff8a7d", title: "毛玻璃的问题",
    desc: "以前的毛玻璃是 Windows 画的：|圆角固定，阴影关不掉，|还会压到旁边的组件上。",
    chips: ["圆角调不了", "阴影关不掉", "压暗旁边的组件"],
  }, (sb, { chips }) => {
    const desk = box(sb, "mdesk", 40, 40, 840, 620);
    wallpaper(desk);
    const frost = (face, x, y, z) => {
      const c = el("div", "ccard frost", desk);
      c.innerHTML = `<div class="face">${face}</div>`;
      style(c, { left: px(x), top: px(y), width: "300px", height: "180px", zIndex: String(z) });
      return c;
    };
    frost(`<small>11月3日 周二</small><b>09:41</b><div class="row"><span>第 307 天</span><span>第 45 周</span></div>`, 300, 150, 2);
    frost(`<small>台北 · 晴</small><b>24°</b><div class="row"><span>体感 26°</span><span>降雨 10%</span></div>`, 250, 300, 1);
    const ctl = box(sb, "ctl off", 40, 682);
    ctl.innerHTML = `<span class="lab">圆角</span><span class="rail"><i style="width:25%"></i><b style="left:25%"></b></span><span class="val">系统</span><span class="gap"></span><span class="lab">阴影</span><span class="tgl" style="--k:1"></span>`;
    const tgl = ctl.querySelector(".tgl");
    const no = box(sb, "verdict bad", 630, 690); no.textContent = "关不掉";
    const v = box(desk, "verdict bad", 250, 500); v.textContent = "上面组件的阴影压在它上面";
    CURSOR_R.push([5.5, 8.7, (t) => ({ p: keys(t, [[5.5, [STAGE.x + 760, STAGE.y + 600]], [6.25, at(tgl), ease.inOut]]), o: prog(t, 5.5, 0.2) * (1 - prog(t, 8.3, 0.3)), down: false })]);
    CLICKS_R.push(6.45);
    return {
      render(t, u) {
        popIn(desk, u, 0.1, { dy: 30, s0: 0.98 });
        popIn(ctl, u, 0.4, { dy: 16 });
        const k = t - 6.45, shake = k > 0 && k < 0.5 ? Math.sin(k * 42) * 7 * (1 - k / 0.5) : 0;
        style(tgl, { transform: `translateX(${px(shake)})` });
        during(no, t, 6.55, 8.95);
        during(v, t, 7.3, 8.95);
        chips.forEach((ch, i) => ch.classList.toggle("on", (i === 0 && t >= 4.9 && t < 6.45) || (i === 1 && t >= 6.45 && t < 7.3) || (i === 2 && t >= 7.3)));
      },
    };
  }).real = true;

  // ───────────────────────── 01b snapshot cards (old time 4–17, video 9–22) ─────────────────────────
  feature(4, 17, {
    n: "01", accent: "#7dc8ff", title: "快照卡片",
    desc: "改用快照：截一次组件后面的画面，|模糊后当作背景。圆角和阴影|都由 FullWidget 画，随你调。",
    chips: ["实验功能", "圆角可调", "阴影可关", "手动重新截取"],
  }, (sb, { chips }) => {
    const desk = box(sb, "mdesk", 40, 40, 840, 620);
    wallpaper(desk);
    const c = card(desk);
    const m = menu(desk);
    const hint = box(desk, "cap", 24, 24); hint.textContent = "移动之后：右键 → 重新截取背景";
    const ctl = box(sb, "ctl", 40, 682);
    ctl.innerHTML = `<span class="lab">圆角</span><span class="rail"><i></i><b></b></span><span class="val">18px</span><span class="gap"></span><span class="lab">阴影</span><span class="tgl" style="--k:1"></span>`;
    const fill = ctl.querySelector(".rail i"), knob = ctl.querySelector(".rail b"), val = ctl.querySelector(".val"), tgl = ctl.querySelector(".tgl");
    // where things are on the desktop (desk coordinates)
    const P0 = [300, 210], P1 = [130, 380], CW = 300, CH = 180;
    const posAt = (t) => keys(t, [[14.0, P0], [14.85, P1, ease.inOut]]);
    const D = { x: STAGE.x + 40, y: STAGE.y + 40 };
    const onCard = (t) => { const [x, y] = posAt(t); return [D.x + x + CW * 0.5, D.y + y + CH * 0.55]; };
    CURSOR.push([5.0, 7.8, (t) => ({
      p: keys(t, [[5.0, [D.x + 700, D.y + 600]], [5.7, onCard(t), ease.inOut], [6.25, onCard(t)], [6.85, at(m.seg("snapshot"), 0.5, 0.6), ease.inOut]]),
      o: prog(t, 5.0, 0.2) * (1 - prog(t, 7.4, 0.35)), down: false,
    })]);
    CURSOR.push([13.2, 16.9, (t) => ({
      p: t < 15.0 ? keys(t, [[13.2, [D.x + 720, D.y + 600]], [13.85, onCard(13.85), ease.inOut], [14.0, onCard(14.0)], [14.85, onCard(14.85), ease.inOut]])
        : keys(t, [[15.0, onCard(15.0)], [15.45, onCard(15.45)], [16.0, at(m.rows.retake, 0.3, 0.55), ease.inOut]]),
      o: prog(t, 13.2, 0.2) * (1 - prog(t, 16.5, 0.35)), down: t > 13.95 && t < 14.9,
    })]);
    CLICKS.push(5.85, 6.95, 15.5, 16.15);
    // the menu: two times, next to the card where it was right-clicked
    const MENU = [[5.9, 7.25, [470, 150]], [15.55, 16.3, [320, 300]]];
    return {
      render(t, u) {
        popIn(desk, u, 0.1, { dy: 30, s0: 0.98 });
        popIn(ctl, u, 0.4, { dy: 16 });
        const [x, y] = posAt(t);
        const r = level(t, 18, [[9.0, 32], [9.9, 6], [10.7, 20]], 0.55);
        const sh = level(t, 1, [[11.6, 0], [12.7, 1]]);
        const snapA = prog(t, 7.5, 0.5, ease.out);
        const cap = t < 16.4 ? P0 : P1;
        const flashA = 0.6 * (bump(prog(t, 7.45, 0.4)) + bump(prog(t, 16.35, 0.4)));
        c.set({ x, y, w: CW, h: CH, r, sh, snapA, capX: cap[0], capY: cap[1], flashA });
        // the controls follow the card
        const f = r / 32;
        style(fill, { width: `${(f * 100).toFixed(2)}%` }); style(knob, { left: `${(f * 100).toFixed(2)}%` });
        val.textContent = `${Math.round(r)}px`;
        style(tgl, { "--k": sh.toFixed(4) });
        // the menu
        const mm = MENU.find(([a, b]) => t >= a - 0.05 && t < b + 0.3);
        if (mm) {
          const [a, b, [mx, my]] = mm;
          const o = prog(t, a, 0.2, ease.click) * (1 - prog(t, b, 0.25, ease.in));
          style(m.el, { left: px(mx), top: px(my), display: o > 0.001 ? "" : "none", opacity: o.toFixed(4), transform: `translateY(${px(-6 * (1 - o))}) scale(${lerp(0.98, 1, o).toFixed(4)})`, transformOrigin: "0 0" });
          const first = a < 10;
          m.set({ style: first && t < 6.95 ? "transparent" : "snapshot", retake: !first, hl: first ? (t > 6.6 ? "card" : "") : (t > 15.9 ? "retake" : "") });
        } else style(m.el, { display: "none" });
        during(hint, t, 15.0, 16.9);
        chips.forEach((ch, i) => ch.classList.toggle("on", (i === 0 && t >= 7.5 && t < 9) || (i === 1 && t >= 9 && t < 11.3) || (i === 2 && t >= 11.3 && t < 13.2) || (i === 3 && t >= 15.0)));
      },
    };
  });

  // ───────────────────────── 02 shadows (17–27) ─────────────────────────
  feature(17, 27, {
    n: "02", accent: "#f2b45c", title: "阴影听你的",
    desc: "全局开关管所有组件。|单个组件可以单独改，|下次改全局，全部跟上。",
    chips: ["全局开关", "单个组件", "跟上全局"],
  }, (sb, { chips }) => {
    const gp = box(sb, "gpanel", 40, 40, 840, 120);
    gp.innerHTML = `<div class="lab">全局微调 · 阴影<small>套用到所有跟随全局主题的组件</small></div><span class="tgl" style="--k:1"></span>`;
    const gt = gp.querySelector(".tgl");
    const desk = box(sb, "mdesk", 40, 190, 840, 470);
    wallpaper(desk, { win: false });
    const faces = [
      `<small>11月3日</small><b>09:41</b>`,
      `<small>台北 · 晴</small><b>24°</b>`,
      `<small>CPU</small><b>18%</b>`,
    ];
    const cards = faces.map((face) => card(desk, { small: true, face, win: false }));
    const own = box(desk, "own", 312, 330);
    own.innerHTML = `<span>这个组件 · 阴影</span><span class="tgl" style="--k:0"></span>`;
    const ot = own.querySelector(".tgl");
    const caps = [[18.8, 20.4, "全局关掉：所有组件一起没有阴影"], [20.6, 22.4, "单个组件打开：只改它自己"], [22.6, 24.2, "改全局：所有组件都跟上"], [24.4, 26.9, "单个组件改过的，也一起跟上"]]
      .map(([a, b, text]) => { const d = box(sb, "cap", 40, 690); d.textContent = text; return { a, b, d }; });
    const G = [[18.6, 0], [22.4, 1], [24.2, 0]];
    const OWN0 = 20.4, OWN1 = 22.4;
    const gOn = (t) => level(t, 1, G);
    CURSOR.push([18.0, 25.0, (t) => ({
      p: keys(t, [[18.0, [STAGE.x + 700, STAGE.y + 740]], [18.5, at(gt), ease.inOut], [19.6, at(gt)], [20.3, at(ot), ease.inOut], [21.5, at(ot)], [22.3, at(gt), ease.inOut]]),
      o: prog(t, 18.0, 0.2) * (1 - prog(t, 24.6, 0.35)), down: false,
    })]);
    CLICKS.push(18.6, 20.4, 22.4, 24.2);
    return {
      render(t, u) {
        popIn(gp, u, 0.15, { dy: 24 });
        popIn(desk, u, 0.3, { dy: 30, s0: 0.98 });
        const g = gOn(t);
        style(gt, { "--k": g.toFixed(4) });
        const ownSet = t >= OWN0 && t < OWN1;
        const mid = level(t, 1, [[18.6, 0], [OWN0, 1], [24.2, 0]]);
        style(ot, { "--k": (t < OWN0 ? g : mid).toFixed(4) });
        const dotA = prog(t, OWN0, 0.25) * (1 - prog(t, OWN1, 0.25));
        cards.forEach((c, i) => c.set({ x: 45 + i * 265, y: 130, w: 230, h: 150, r: 18, sh: i === 1 ? (t < OWN0 ? g : mid) : g, dotA: i === 1 ? dotA : 0 }));
        fadeIn(own, u, 1.0);
        caps.forEach(({ a, b, d }) => during(d, t, a, b, 0.25));
        chips.forEach((ch, i) => ch.classList.toggle("on", (i === 0 && t >= 18.6 && t < 20.4) || (i === 1 && ownSet) || (i === 2 && t >= 22.4)));
      },
    };
  });

  // ───────────────────────── 03 the stacking order (27–34) ─────────────────────────
  feature(27, 34, {
    n: "03", accent: "#b69cff", title: "叠放顺序不变",
    desc: "点一下桌面空白处，|组件之间的上下顺序不再变，|旁边的阴影也不会压暗它。",
    chips: ["修复问题 #11"],
  }, (sb) => {
    const sides = [{ x: 40, tag: "1.4 之前", now: false, click: 28.9 }, { x: 470, tag: "1.5 现在", now: true, click: 30.4 }].map((s) => {
      const desk = box(sb, "mdesk", s.x, 100, 410, 540);
      wallpaper(desk, { win: false });
      const a = card(desk, { small: true, win: false, solid: "#d9dde3", face: `<small>11月3日</small><b>09:41</b>` });
      const b = card(desk, { small: true, win: false, solid: "#2a3140", face: `<small>台北 · 晴</small><b>24°</b>` });
      a.el.classList.add("light");
      const tag = box(sb, `vtag${s.now ? " now" : ""}`, s.x, 40); tag.textContent = s.tag;
      const v = box(desk, `verdict ${s.now ? "good" : "bad"}`, 24, 466); v.textContent = s.now ? "✓ 顺序不变" : "旁边的阴影压在上面";
      return { ...s, desk, a, b, tag, v };
    });
    const cap = box(sb, "cap", 40, 690); cap.textContent = "两个组件叠在一起时，看得最清楚";
    CURSOR.push([28.2, 32.0, (t) => ({
      p: keys(t, [[28.2, [STAGE.x + 440, STAGE.y + 740]], [28.8, [STAGE.x + 40 + 330, STAGE.y + 100 + 70], ease.inOut], [29.7, [STAGE.x + 40 + 330, STAGE.y + 100 + 70]], [30.3, [STAGE.x + 470 + 330, STAGE.y + 100 + 70], ease.inOut]]),
      o: prog(t, 28.2, 0.2) * (1 - prog(t, 31.6, 0.35)), down: false,
    })]);
    CLICKS.push(28.9, 30.4);
    return {
      render(t, u) {
        sides.forEach((s, i) => {
          popIn(s.desk, u, 0.15 + i * 0.12, { dy: 30, s0: 0.98 });
          fadeIn(s.tag, u, 0.3 + i * 0.12);
          // A is on top of B; in 1.4 a click on the desktop put B on top, and its shadow fell on A
          const flipped = !s.now && t >= s.click + 0.08;
          s.a.set({ x: 36, y: 110, w: 250, h: 160, z: flipped ? 1 : 2, sh: 1.25 });
          s.b.set({ x: 124, y: 214, w: 250, h: 160, z: flipped ? 2 : 1, sh: 1.25 });
          during(s.v, t, s.click + 0.25, 33.9, 0.3);
        });
        fadeIn(cap, u, 4.4);
      },
    };
  });

  // ───────────────────────── 04 the right-click menu (34–38) ─────────────────────────
  feature(34, 38, {
    n: "04", accent: "#7de2c4", title: "右键菜单更清爽",
    desc: "去掉了投影，只留一圈细边框。|打开菜单时，下面的组件不会被压暗。",
  }, (sb) => {
    const desk = box(sb, "mdesk", 40, 40, 840, 600);
    wallpaper(desk);
    const c1 = card(desk), c2 = card(desk, { face: `<small>台北 · 晴</small><b>24°</b><div class="row"><span>体感 26°</span><span>降雨 10%</span></div>` });
    const m = menu(desk);
    m.set({ style: "transparent" });
    const tag = box(sb, "vtag", 40, 682);
    const cap = box(sb, "cap", 200, 682); cap.textContent = "去掉投影，只留细边框";
    return {
      render(t, u) {
        popIn(desk, u, 0.1, { dy: 30, s0: 0.98 });
        c1.set({ x: 80, y: 90, w: 300, h: 180 });
        c2.set({ x: 80, y: 310, w: 300, h: 180 });
        const h = 1 - prog(t, 35.7, 0.6, ease.inOut);
        style(m.el, { left: "300px", top: "150px", display: "", opacity: prog(u, 0.35, 0.3).toFixed(4),
          boxShadow: `0 0 0 1px rgba(255,255,255,0.09), 0 18px 40px rgba(0,0,0,${(0.6 * h).toFixed(3)}), 0 3px 8px rgba(0,0,0,${(0.35 * h).toFixed(3)})` });
        const now = t >= 35.95;
        tag.textContent = now ? "1.5" : "1.4";
        tag.classList.toggle("now", now);
        fadeIn(tag, u, 0.4);
        during(cap, t, 36.1, 37.9);
      },
    };
  });

  // ───────────────────────── outro (38–45) ─────────────────────────
  shot(38, DURATION + 1, (root) => {
    const logo = el("img", "logo", root); logo.src = "../build/logo/logo.svg";
    style(logo, { left: px(W / 2 - 66), top: "250px" });
    const big = box(root, "big", 0, 410);
    const bl = letters(big, "FullWidget ");
    const em = el("em", "", big); em.textContent = "1.5";
    const sub = box(root, "sub", 0, 600); sub.textContent = "快照卡片 · 阴影 · 叠放顺序";
    const url = box(root, "url", 0, 720); url.innerHTML = `<b>免费下载</b><span>hmgvibe.github.io/FullWidget</span>`;
    const nl = box(root, "note-line", 0, 850); nl.textContent = "也可以在 Microsoft Store 搜索 FullWidget；已安装的会在软件里收到更新";
    return {
      render(t, u) {
        popIn(logo, u, 0.0, { dy: 40, s0: 0.6 });
        reveal(bl, u, 0.3, { stagger: 0.04, dur: 0.5, rise: 0.25, blur: 14 });
        const e = prog(u, 0.75, 0.5, ease.click);
        style(em, { display: "inline-block", opacity: clamp(e * 3).toFixed(4), transform: `translateY(${px(30 * (1 - e))}) scale(${lerp(0.7, 1, e).toFixed(4)})` });
        reveal([sub], u, 1.2, { dur: 0.6, rise: 0.3, blur: 8 });
        popIn(url, u, 1.7, { dy: 24 });
        reveal([nl], u, 2.3, { dur: 0.6, rise: 0.3, blur: 6 });
        style(root, { opacity: (1 - prog(t, DURATION - 0.8, 0.8, ease.in)).toFixed(4) });
      },
    };
  });

  // ───────────────────────── the frame ─────────────────────────
  function renderCursor(t, CURSOR, CLICKS) {
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
    const old = toOld(t);
    for (const s of shots) {
      const tt = s.real ? t : old;
      const on = tt != null && tt >= s.t0 && tt < s.t1;
      style(s.root, { display: on ? "block" : "none" });
      if (on) s.render(tt, tt - s.t0);
    }
    renderChapters(t);
    if (old == null) renderCursor(t, CURSOR_R, CLICKS_R);
    else renderCursor(old, CURSOR, CLICKS);
  }

  async function ready() {
    for (const s of shots) style(s.root, { display: "block" });
    const imgs = [...stage.querySelectorAll("img")];
    await Promise.all(imgs.map((i) => (i.complete ? Promise.resolve() : new Promise((r) => { i.onload = i.onerror = r; })).then(() => i.decode().catch(() => {}))));
    await document.fonts.ready;
  }

  window.SCENES = {
    ready, render, sync: async () => {}, duration: DURATION + GAP,
    marks: [[0, "开场"], [4, "毛玻璃的问题"], [9, "快照卡片"], ...CHAPTERS.slice(1).map(([n, a]) => [a, n]), [43, "结尾"]],
  };
})();
