/* Plays the promo in the browser, or holds still for the recorder (?render).
     Space: play / pause   ← →: one beat back / forward   ?t=20: start at 20 s   ?pause: start paused */
(function () {
  "use strict";
  const q = new URLSearchParams(location.search);
  const rendering = q.has("render");
  if (rendering) document.documentElement.classList.add("render");
  const stage = document.getElementById("stage");
  const D = SCENES.duration;

  let t = Math.min(D, Number(q.get("t")) || 0);
  let playing = !rendering && !q.has("pause");
  let last = 0;

  // the recorder's API: wait for ready(), then seek(t) before each screenshot
  window.PROMO = {
    duration: D,
    fps: E.FPS,
    ready: () => readyP,
    async seek(time) {
      t = time;
      SCENES.render(t);
      await SCENES.sync(t);
      SCENES.render(t);
      await new Promise((r) => requestAnimationFrame(() => requestAnimationFrame(r)));
      return true;
    },
  };

  function fit() {
    if (rendering) { stage.style.transform = "none"; return; }
    const vp = document.getElementById("viewport").getBoundingClientRect();
    const s = Math.min(vp.width / E.W, vp.height / E.H);
    stage.style.transform = `translate(${(vp.width - E.W * s) / 2}px, ${(vp.height - E.H * s) / 2}px) scale(${s})`;
  }
  addEventListener("resize", fit);
  fit();

  // the time line: bar ticks, and a mark at each scene
  const track = document.getElementById("track");
  const fill = document.getElementById("fill");
  const timeEl = document.getElementById("time");
  const playBtn = document.getElementById("play");
  for (let b = 0; b <= D / E.bar(1); b++) {
    const tick = document.createElement("div");
    tick.className = "tick";
    tick.style.left = `${(E.bar(b) / D) * 100}%`;
    track.append(tick);
  }
  for (const [at, name] of SCENES.marks) {
    const tick = document.createElement("div");
    tick.className = "tick scene";
    tick.style.left = `${(at / D) * 100}%`;
    const lbl = document.createElement("div");
    lbl.className = "lbl";
    lbl.style.left = `${(at / D) * 100}%`;
    lbl.textContent = name;
    track.append(tick, lbl);
  }
  const seekTo = (x) => { const r = track.getBoundingClientRect(); t = E.clamp((x - r.left) / r.width) * D; };
  track.addEventListener("pointerdown", (e) => {
    seekTo(e.clientX);
    track.setPointerCapture(e.pointerId);
    const move = (ev) => seekTo(ev.clientX);
    track.addEventListener("pointermove", move);
    track.addEventListener("pointerup", () => track.removeEventListener("pointermove", move), { once: true });
  });
  const setPlaying = (on) => { playing = on; playBtn.textContent = on ? "暫停" : "播放"; };
  playBtn.addEventListener("click", () => setPlaying(!playing));
  addEventListener("keydown", (e) => {
    if (e.code === "Space") { e.preventDefault(); setPlaying(!playing); }
    if (e.code === "ArrowRight") t = Math.min(D, t + E.beat(1));
    if (e.code === "ArrowLeft") t = Math.max(0, t - E.beat(1));
  });
  setPlaying(playing);

  function frame(now) {
    if (playing) { t += Math.min(0.1, (now - last) / 1000); if (t >= D) t = 0; }
    last = now;
    SCENES.render(t);
    SCENES.sync(t);
    fill.style.width = `${(t / D) * 100}%`;
    timeEl.textContent = `${t.toFixed(2)} s · 第 ${Math.floor(t / E.bar(1)) + 1} 小節`;
    requestAnimationFrame(frame);
  }

  const readyP = SCENES.ready().then(() => {
    SCENES.render(t);
    if (!rendering) requestAnimationFrame((now) => { last = now; frame(now); });
    return true;
  });
})();
