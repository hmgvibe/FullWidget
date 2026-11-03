// Records the promo (promo/index.html) frame by frame with headless Edge.
//
//   node promo/render.mjs                         the whole video → promo/out/FullWidget-promo.mp4
//   node promo/render.mjs --stills 2,6.5,14       single frames → promo/out/stills/
//   node promo/render.mjs --sheet 0:60:0.5        a contact sheet, one small frame every 0.5 s → promo/out/sheet-*.png
//   node promo/render.mjs --serve                 just serve the page: http://127.0.0.1:8080/promo/index.html
//   node promo/render.mjs --assets                the 9:41 clock at 2× (promo/assets/clock-0941.*.png)
//   options: --fps 60  --workers 4  --from 0 --to 60 (part of the video)  --out file.mp4
//
// Every frame is drawn from its time alone (PROMO.seek(t)), so recording is exact and repeatable.
// The video needs ffmpeg: .tools/ffmpeg/bin/ffmpeg.exe in the repository, or ffmpeg on PATH.
// No npm packages: Node's own http, WebSocket and child_process.
import { spawn } from "node:child_process";
import fs from "node:fs";
import http from "node:http";
import os from "node:os";
import path from "node:path";

const root = path.resolve(import.meta.dirname, "..");
const outDir = path.join(import.meta.dirname, "out");
const args = process.argv.slice(2);
const opt = (name, def) => { const i = args.indexOf(name); return i >= 0 ? args[i + 1] : def; };
const EDGE = ["C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe", "C:\\Program Files\\Microsoft\\Edge\\Application\\msedge.exe"].find((p) => fs.existsSync(p));
const FFMPEG = [path.join(root, ".tools", "ffmpeg", "bin", "ffmpeg.exe")].find((p) => fs.existsSync(p)) || "ffmpeg";
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

// ── a static server for the repository (the page loads ../site and ../widgets) ──
const TYPES = { ".html": "text/html; charset=utf-8", ".js": "text/javascript", ".mjs": "text/javascript", ".css": "text/css", ".json": "application/json", ".svg": "image/svg+xml", ".webp": "image/webp", ".png": "image/png", ".jpg": "image/jpeg" };
function serve(port = 0) {
  const server = http.createServer((req, res) => {
    const p = decodeURIComponent(new URL(req.url, "http://x").pathname);
    const f = path.join(root, p.endsWith("/") ? p + "index.html" : p);
    if (!f.startsWith(root + path.sep) || !fs.existsSync(f) || fs.statSync(f).isDirectory()) { res.writeHead(404); res.end(); return; }
    res.writeHead(200, { "Content-Type": TYPES[path.extname(f)] || "application/octet-stream", "Cache-Control": "max-age=600" });
    fs.createReadStream(f).pipe(res);
  });
  return new Promise((r) => server.listen(port, "127.0.0.1", () => r(server)));
}

// ── one headless Edge with a page, driven over the DevTools protocol ──
async function openBrowser(port, url, readyExpr = "PROMO.ready()") {
  const profile = fs.mkdtempSync(path.join(os.tmpdir(), "fw-promo-"));
  const proc = spawn(EDGE, [
    "--headless=new", `--remote-debugging-port=${port}`, `--user-data-dir=${profile}`, "--window-size=1920,1080",
    "--hide-scrollbars", "--force-device-scale-factor=1", "--force-color-profile=srgb", "--mute-audio",
    "--disable-background-timer-throttling", "--disable-renderer-backgrounding", "--disable-backgrounding-occluded-windows",
    "--no-first-run", "--no-default-browser-check",
    // this PC's Edge installs an extension (AdGuard) whose welcome tab would push ours into the
    // background, where nothing is drawn: no extensions, and other tabs are closed (see keepFront)
    "--disable-extensions", "--disable-component-extensions-with-background-pages", "--disable-default-apps", "--disable-sync",
    "about:blank",
  ], { stdio: "ignore" });
  let target;
  for (let i = 0; i < 100 && !target; i++) {
    await sleep(150);
    try { const l = (await (await fetch(`http://127.0.0.1:${port}/json/list`)).json()).filter((x) => x.type === "page"); target = l.find((x) => x.url === "about:blank") || l[0]; } catch { /* not up yet */ }
  }
  if (!target) throw new Error("Edge didn't start");
  const ws = new WebSocket(target.webSocketDebuggerUrl);
  await new Promise((r, j) => { ws.onopen = r; ws.onerror = j; });
  let id = 0;
  const waiting = new Map();
  ws.onmessage = (m) => { const msg = JSON.parse(m.data); if (msg.id && waiting.has(msg.id)) { waiting.get(msg.id)(msg); waiting.delete(msg.id); } };
  const send = (method, params = {}) => new Promise((resolve, reject) => {
    const n = ++id;
    waiting.set(n, (msg) => (msg.error ? reject(new Error(`${method}: ${msg.error.message}`)) : resolve(msg.result)));
    ws.send(JSON.stringify({ id: n, method, params }));
  });
  const evaluate = async (expression) => {
    const r = await send("Runtime.evaluate", { expression, awaitPromise: true, returnByValue: true });
    if (r.exceptionDetails) throw new Error(r.exceptionDetails.exception?.description || r.exceptionDetails.text);
    return r.result.value;
  };
  /** Closes any other tab (e.g. an extension's welcome page) and keeps ours in front. */
  const keepFront = async () => {
    try {
      const all = await (await fetch(`http://127.0.0.1:${port}/json/list`)).json();
      for (const x of all) if (x.type === "page" && x.id !== target.id) await fetch(`http://127.0.0.1:${port}/json/close/${x.id}`).catch(() => {});
    } catch { /* fine */ }
    await send("Page.bringToFront").catch(() => {});
  };
  await send("Page.enable");
  await send("Emulation.setDeviceMetricsOverride", { width: 1920, height: 1080, deviceScaleFactor: 1, mobile: false });
  await send("Page.navigate", { url });
  const global = readyExpr.split(/[.(]/)[0];
  for (let i = 0; i < 200; i++) { await sleep(100); try { if (await evaluate(`!!window.${global}`)) break; } catch { /* loading */ } }
  await keepFront();
  await evaluate(readyExpr);
  let count = 0;
  const once = async (t, scale, format) => {
    await evaluate(`PROMO.seek(${t.toFixed(6)})`);
    const r = await send("Page.captureScreenshot", { format, fromSurface: true, captureBeyondViewport: false, clip: { x: 0, y: 0, width: 1920, height: 1080, scale } });
    return Buffer.from(r.data, "base64");
  };
  /** One frame; if it takes more than 15 s, bring the tab back to the front and try again. */
  const shot = async (t, scale = 1, format = "png") => {
    if (count++ % 120 === 0) await keepFront();
    for (let attempt = 1; ; attempt++) {
      const r = await Promise.race([once(t, scale, format), sleep(15000).then(() => null)]);
      if (r) return r;
      if (attempt >= 4) throw new Error(`frame at ${t.toFixed(3)} s didn't render`);
      console.warn(`\nframe at ${t.toFixed(3)} s stuck, retrying`);
      await keepFront();
    }
  };
  const close = async () => { try { await send("Browser.close"); } catch { /* gone */ } ws.close(); await sleep(300); try { proc.kill(); } catch { /* gone */ } fs.rmSync(profile, { recursive: true, force: true }); };
  return { send, evaluate, shot, close };
}

function ffmpeg(argv) {
  const p = spawn(FFMPEG, argv, { stdio: ["pipe", "inherit", "inherit"] });
  const done = new Promise((r, j) => p.on("exit", (c) => (c === 0 ? r() : j(new Error(`ffmpeg exited with ${c}`)))));
  p.on("error", (e) => { throw new Error(`ffmpeg not found (${e.message}): put it in .tools/ffmpeg/bin`); });
  return { stdin: p.stdin, done };
}
const write = (stream, buf) => new Promise((r) => (stream.write(buf) ? r() : stream.once("drain", r)));

if (args.includes("--serve")) {
  await serve(8080);
  console.log("http://127.0.0.1:8080/promo/index.html  (Ctrl+C to stop)");
  await new Promise(() => {});
}
const server = await serve();
const base = `http://127.0.0.1:${server.address().port}/promo/index.html?render`;
fs.mkdirSync(outDir, { recursive: true });
const started = Date.now();
let port = 9400 + Math.floor(Math.random() * 400);

try {
  if (args.includes("--assets")) {
    // the clock widget at 9:41, 2× and transparent, in each theme (used on the tiles)
    for (const theme of ["instrument", "paper", "type"]) {
      const b = await openBrowser(port++, `http://127.0.0.1:${server.address().port}/promo/clock.html?theme=${theme}`, "PROMO_ASSET");
      await b.send("Emulation.setDefaultBackgroundColorOverride", { color: { r: 0, g: 0, b: 0, a: 0 } });
      const r = await b.send("Page.captureScreenshot", { format: "png", fromSurface: true, clip: { x: 0, y: 0, width: 300, height: 150, scale: 2 } });
      const file = path.join(import.meta.dirname, "assets", `clock-0941.${theme}.png`);
      fs.writeFileSync(file, Buffer.from(r.data, "base64"));
      console.log(path.relative(root, file));
      await b.close();
    }
  } else if (opt("--stills")) {
    const dir = path.join(outDir, "stills");
    fs.mkdirSync(dir, { recursive: true });
    const b = await openBrowser(port, base);
    for (const s of opt("--stills").split(",").map(Number)) {
      const file = path.join(dir, `t${s.toFixed(2).padStart(5, "0")}.png`);
      fs.writeFileSync(file, await b.shot(s));
      console.log(path.relative(root, file));
    }
    await b.close();
  } else if (opt("--sheet")) {
    // small frames in a grid, to see the motion's flow at a glance
    const [a, z, step] = opt("--sheet").split(":").map(Number);
    const b = await openBrowser(port, base);
    const frames = [];
    for (let s = a; s <= z + 1e-9; s += step) frames.push({ s, png: (await b.shot(s, 0.25, "jpeg")).toString("base64") });
    const cols = 6, rows = Math.ceil(frames.length / cols);
    const html = `<body style="margin:0;background:#1b1d21;display:grid;grid-template-columns:repeat(${cols},320px);gap:6px;padding:6px;font:12px Consolas;color:#9aa2ac">` +
      frames.map((f) => `<div><img src="data:image/jpeg;base64,${f.png}" style="width:320px;display:block"><div>${f.s.toFixed(2)} s</div></div>`).join("") + "</body>";
    await b.send("Emulation.setDeviceMetricsOverride", { width: 6 + cols * 326, height: 6 + rows * 206, deviceScaleFactor: 1, mobile: false });
    const frameTree = await b.send("Page.getFrameTree");
    await b.send("Page.setDocumentContent", { frameId: frameTree.frameTree.frame.id, html });
    await sleep(600);
    const r = await b.send("Page.captureScreenshot", { format: "png" });
    const file = path.join(outDir, `sheet-${a}-${z}.png`);
    fs.writeFileSync(file, Buffer.from(r.data, "base64"));
    console.log(path.relative(root, file), `(${frames.length} frames)`);
    await b.close();
  } else {
    const fps = Number(opt("--fps", 60));
    const from = Number(opt("--from", 0)), to = Number(opt("--to", 60));
    const workers = Number(opt("--workers", Math.max(1, Math.min(4, Math.floor(os.cpus().length / 4)))));
    const out = path.resolve(opt("--out", path.join(outDir, "FullWidget-promo.mp4")));
    const first = Math.round(from * fps), last = Math.round(to * fps); // [first, last)
    const total = last - first;
    const work = fs.mkdtempSync(path.join(outDir, "work-"));
    console.log(`${total} frames at ${fps} fps, ${workers} workers`);
    let doneFrames = 0;
    const tick = setInterval(() => process.stdout.write(`\r${doneFrames}/${total} frames, ${((Date.now() - started) / 1000).toFixed(0)} s`), 2000);
    const parts = [];
    await Promise.all(Array.from({ length: workers }, async (_, w) => {
      const a = first + Math.floor((total * w) / workers), z = first + Math.floor((total * (w + 1)) / workers);
      const file = path.join(work, `part${w}.mp4`);
      parts[w] = file;
      const b = await openBrowser(port + w, base);
      const enc = ffmpeg(["-y", "-loglevel", "error", "-f", "image2pipe", "-framerate", String(fps), "-c:v", "png", "-i", "-",
        "-vf", "scale=out_color_matrix=bt709:out_range=tv,format=yuv420p", "-c:v", "libx264", "-preset", "slow", "-crf", "15", "-tune", "animation",
        "-colorspace", "bt709", "-color_primaries", "bt709", "-color_trc", "bt709", "-color_range", "tv", "-g", String(fps), file]);
      for (let i = a; i < z; i++) { await write(enc.stdin, await b.shot(i / fps)); doneFrames++; }
      enc.stdin.end();
      await enc.done;
      await b.close();
    }));
    clearInterval(tick);
    fs.writeFileSync(path.join(work, "list.txt"), parts.map((p) => `file '${p.replace(/\\/g, "/")}'`).join("\n"));
    const join = ffmpeg(["-y", "-loglevel", "error", "-f", "concat", "-safe", "0", "-i", path.join(work, "list.txt"), "-c", "copy", "-movflags", "+faststart", out]);
    join.stdin.end();
    await join.done;
    fs.rmSync(work, { recursive: true, force: true });
    console.log(`\n${path.relative(root, out)}: ${(fs.statSync(out).size / 1048576).toFixed(1)} MB, ${((Date.now() - started) / 60000).toFixed(1)} min`);
  }
} finally {
  server.close();
}
