const $ = (id) => document.getElementById(id);
const t = widget.t;
let opts = widget.settings.get();
let s = { available: false }; // what Windows reports, see widget.d.ts MediaState
let tick = 0;
const BARS = 44;

const PLAY = '<path d="M7.5 4.8v14.4L19.5 12z"/>';
const PAUSE = '<path d="M6.5 5h4v14h-4zM13.5 5h4v14h-4z"/>';

$("wave").innerHTML = "<i></i>".repeat(BARS);

/** "3:07", or "1:02:45" past an hour. */
function clock(ms) {
  const sec = Math.max(0, Math.floor(ms / 1000));
  const h = Math.floor(sec / 3600), m = Math.floor((sec % 3600) / 60), x = String(sec % 60).padStart(2, "0");
  return h ? `${h}:${String(m).padStart(2, "0")}:${x}` : `${m}:${x}`;
}

/** Bar heights for the instrument theme's waveform, the same for the same track. */
function shape(seed) {
  let h = 2166136261;
  for (const c of seed) h = Math.imul(h ^ c.charCodeAt(0), 16777619);
  const out = [];
  for (let i = 0; i < BARS; i++) {
    h = Math.imul(h ^ (h >>> 15), 2246822507) >>> 0;
    const env = 0.55 + 0.45 * Math.sin((i / BARS) * Math.PI); // fuller in the middle, like a song
    out.push(Math.round((0.25 + (h % 1000) / 1000 * 0.75) * env * 100));
  }
  return out;
}
let shaped = "";

function render() {
  const live = !!s.available && s.status !== "closed";
  document.body.classList.toggle("none", !live);
  document.body.classList.toggle("playing", live && !!s.playing);
  document.body.classList.toggle("no-app", !opts.app || !live || !s.app || !s.app.name);

  const title = live ? s.title || (s.app && s.app.name) || t({ "zh-TW": "正在播放", "zh-CN": "正在播放", en: "Playing" }) : t({ "zh-TW": "沒有正在播放的媒體", "zh-CN": "没有正在播放的媒体", en: "Nothing playing" });
  const artist = live ? [s.artist || s.albumArtist, s.album && s.album !== s.title ? s.album : ""].filter(Boolean).join(" · ") || s.subtitle || ""
    : t({ "zh-TW": "在音樂或影片程式裏播放，就會出現在這裏", "zh-CN": "在音乐或视频程序里播放，就会出现在这里", en: "Play something in a music or video app" });
  $("title").textContent = title;
  $("title").title = title;
  $("artist").textContent = artist;
  $("artist").title = artist;

  if (live && s.app) {
    $("app-name").textContent = s.app.name;
    $("app-icon").hidden = $("badge").hidden = !s.app.icon;
    if (s.app.icon && $("app-icon").getAttribute("src") !== s.app.icon) $("app-icon").src = $("badge").src = s.app.icon;
    $("badge").title = s.app.name;
  }

  setCover(live ? s.cover : null);

  const can = (live && s.can) || {};
  $("prev").disabled = !can.previous;
  $("next").disabled = !can.next;
  $("play").disabled = !(can.toggle || (s.playing ? can.pause : can.play));
  $("play-icon").innerHTML = live && s.playing ? PAUSE : PLAY;
  $("play").title = live && s.playing ? t({ "zh-TW": "暫停", "zh-CN": "暂停", en: "Pause" }) : t({ "zh-TW": "播放", "zh-CN": "播放", en: "Play" });
  $("prev").title = t({ "zh-TW": "上一首", "zh-CN": "上一首", en: "Previous" });
  $("next").title = t({ "zh-TW": "下一首", "zh-CN": "下一首", en: "Next" });

  const seed = `${s.title}|${s.artist}`;
  if (seed !== shaped) {
    shaped = seed;
    shape(seed).forEach((v, i) => { $("wave").children[i].style.height = `${v}%`; });
  }
  document.body.classList.toggle("no-progress", !opts.progress || !live || !(s.duration > 0));
  document.body.classList.toggle("seekable", !!can.seek);
  renderPosition();

  clearInterval(tick);
  if (live && s.playing && s.duration > 0 && opts.progress) tick = setInterval(renderPosition, 1000);
}

function renderPosition() {
  if (!s.available || !(s.duration > 0)) { $("times").textContent = ""; return; }
  const p = widget.media.position(s);
  const f = Math.min(1, p / s.duration);
  $("fill").style.width = `${f * 100}%`;
  $("knob").style.left = `${f * 100}%`;
  const lit = Math.round(f * BARS);
  const bars = $("wave").children;
  for (let i = 0; i < BARS; i++) bars[i].classList.toggle("on", i < lit);
  $("times").textContent = `${clock(p)} / ${clock(s.duration)}`;
}

// The new cover replaces the old one only once it has loaded, so it never flashes empty.
let coverUrl = null;
function setCover(url) {
  if (url === coverUrl) return;
  coverUrl = url;
  if (!url) { $("art").classList.remove("has"); $("cover").removeAttribute("src"); return; }
  const img = new Image();
  img.onload = () => {
    if (coverUrl !== url) return;
    $("cover").src = url;
    $("art").classList.add("has");
  };
  img.onerror = () => { if (coverUrl === url) $("art").classList.remove("has"); };
  img.src = url;
}

function act(fn) {
  fn().catch((e) => widget.log(`media: ${e.message}`));
}
$("play").onclick = () => {
  // answer at once; Windows confirms a moment later
  if (s.available) { s = { ...s, playing: !s.playing, position: widget.media.position(s), updated: Date.now() }; render(); }
  act(() => (s.can && s.can.toggle ? widget.media.toggle() : s.playing ? widget.media.play() : widget.media.pause()));
};
$("prev").onclick = () => act(widget.media.previous);
$("next").onclick = () => act(widget.media.next);
$("progress").addEventListener("click", (e) => {
  if (!s.available || !s.can || !s.can.seek || !(s.duration > 0)) return;
  const r = $("progress").getBoundingClientRect();
  const ms = Math.max(0, Math.min(1, (e.clientX - r.left) / r.width)) * s.duration;
  s = { ...s, position: ms, updated: Date.now() };
  renderPosition();
  act(() => widget.media.seek(ms));
});
document.addEventListener("keydown", (e) => {
  if (e.key === " " || e.key === "MediaPlayPause") { e.preventDefault(); $("play").click(); }
  else if (e.key === "ArrowRight" || e.key === "MediaTrackNext") $("next").click();
  else if (e.key === "ArrowLeft" || e.key === "MediaTrackPrevious") $("prev").click();
});

widget.media.onChange((state) => { s = state || { available: false }; render(); });
widget.settings.onChange((o) => { opts = o; render(); });
// no ticking while the widget is hidden
document.addEventListener("visibilitychange", () => { if (document.hidden) clearInterval(tick); else render(); });
render();
