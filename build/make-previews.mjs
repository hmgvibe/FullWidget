// Draws the widget library pictures: every built-in widget whose widget.json has a "preview",
// in every theme and language, saved as widgets/<folder>/preview/<theme>.<lang>.webp.
//
//   dotnet build src/FullWidget            (the Debug build is used)
//   node build/make-previews.mjs [widget-id ...]
//   node build/make-previews.mjs --scale 2 --out site/assets/widgets   pictures for the website: 2 device
//                                                         pixels per CSS pixel, written to <out>/<folder>/
//   node build/make-previews.mjs --from market/widgets   widgets in another folder (e.g. the market's),
//                                                         with sample content from that folder's ../previews.json
//
// It runs FullWidget with a throwaway profile, puts each widget on screen with the sample content
// from build/previews.json, and photographs its card through the WebView2 debugging port.
// Weather widgets show live data from the network; system widgets show this PC (its name hidden).
// Don't use the mouse while it runs.
import { execFileSync, spawn } from "node:child_process";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";

const root = path.resolve(import.meta.dirname, "..");
const exe = path.join(root, "src", "FullWidget", "bin", "Debug", "net9.0-windows", "FullWidget.exe");
const PORT = 9339;
const LANGS = ["zh-TW", "zh-CN", "en"];
const SCALE = 0.6;
const argv = process.argv.slice(2);
const opt = (name) => { const i = argv.indexOf(name); return i >= 0 ? argv[i + 1] : null; };
const fromDir = opt("--from") ? path.resolve(opt("--from")) : null;
// --scale N: N device pixels per CSS pixel, whatever the screen's scaling (default: 0.6 of the screen's pixels)
const outScale = opt("--scale") ? Number(opt("--scale")) : null;
const outDir = opt("--out") ? path.resolve(opt("--out")) : null;
const valued = new Set(["--from", "--scale", "--out"]);
const only = argv.filter((a, i) => !valued.has(a) && !valued.has(argv[i - 1]));
const widgetsDir = fromDir || path.join(root, "widgets");

const themes = fs.readdirSync(path.join(root, "sdk", "themes")).filter((d) => fs.existsSync(path.join(root, "sdk", "themes", d, "theme.json")));
const samplesFile = fromDir ? path.join(fromDir, "..", "previews.json") : path.join(import.meta.dirname, "previews.json");
const samples = fs.existsSync(samplesFile) ? JSON.parse(fs.readFileSync(samplesFile, "utf8")) : {};
const widgets = fs.readdirSync(widgetsDir, { withFileTypes: true }).filter((e) => e.isDirectory() && fs.existsSync(path.join(widgetsDir, e.name, "widget.json"))).map((e) => e.name)
  .map((folder) => ({ folder, m: JSON.parse(fs.readFileSync(path.join(widgetsDir, folder, "widget.json"), "utf8").replace(/^\s*\/\/.*$/gm, "")) }))
  .filter(({ m }) => m.preview && (!only.length || only.includes(m.id)));

const data = fs.mkdtempSync(path.join(os.tmpdir(), "fw-previews-"));
if (fromDir) {
  // the profile gets its own copy of those widgets, trusted like ones the user wrote
  for (const { folder } of widgets) fs.cpSync(path.join(fromDir, folder), path.join(data, "widgets", folder), { recursive: true });
  fs.writeFileSync(path.join(data, "config.json"), JSON.stringify({ firstRunDone: true, ownWidgets: widgets.map((w) => w.m.id) }));
}
const env = { ...process.env, FULLWIDGET_DEV_ROOT: root, WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS: `--remote-debugging-port=${PORT} --process-per-site` };
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const cmd = (c) => execFileSync(exe, [`--data-dir=${data}`, `--cmd=${c}`], { env });
const config = () => JSON.parse(fs.readFileSync(path.join(data, "config.json"), "utf8"));
const pick = (v, lang) => (v && typeof v === "object" && !Array.isArray(v) && ("zh-TW" in v || "en" in v) ? v[lang] ?? v.en : v);
const now = new Date();
const today = `${now.getFullYear()}-${now.getMonth() + 1}-${now.getDate()}`;
// "{photo:NAME}" in a sample script: that picture from build/preview-photos as a data: URL, so
// previews never show other people's photos (e.g. Bing's picture of the day)
const photos = (js) => js.replace(/\{photo:([\w.-]+)\}/g, (_, name) =>
  "data:image/jpeg;base64," + fs.readFileSync(path.join(import.meta.dirname, "preview-photos", name)).toString("base64"));
const fill = (v) => JSON.parse(JSON.stringify(v).replaceAll('"{today}"', JSON.stringify(today)));

// ── a little CDP client for one page ──
async function page(host, instanceId) {
  for (let tries = 0; tries < 40; tries++) {
    const targets = await (await fetch(`http://127.0.0.1:${PORT}/json/list`)).json();
    for (const t of targets.filter((x) => x.type === "page" && x.url.includes(host))) {
      const p = await connect(t.webSocketDebuggerUrl);
      const id = await p.eval("window.widget && widget.instanceId").catch(() => null);
      if (id === instanceId) return p;
      p.close();
    }
    await sleep(250);
  }
  throw new Error(`no page for ${host} ${instanceId}`);
}

async function connect(url) {
  const ws = new WebSocket(url);
  let n = 0;
  const waiting = new Map();
  ws.onmessage = (m) => { const msg = JSON.parse(m.data); if (msg.id && waiting.has(msg.id)) { waiting.get(msg.id)(msg); waiting.delete(msg.id); } };
  await new Promise((r, j) => { ws.onopen = r; ws.onerror = j; });
  const send = (method, params = {}) => new Promise((r) => { const id = ++n; waiting.set(id, r); ws.send(JSON.stringify({ id, method, params })); });
  return {
    send,
    async eval(expression) {
      const r = await send("Runtime.evaluate", { expression, returnByValue: true, awaitPromise: true });
      if (r.result.exceptionDetails) throw new Error(r.result.exceptionDetails.exception?.description || r.result.exceptionDetails.text);
      return r.result.result.value;
    },
    close: () => ws.close(),
  };
}

/** Waits until the page has drawn: fonts loaded, and for network widgets the data has arrived. */
async function settle(p, network) {
  await p.eval("document.fonts.ready.then(() => true)");
  if (network) {
    for (let i = 0; i < 40; i++) {
      if (await p.eval("(() => { const m = document.getElementById('msg'); return !m || m.hidden; })()")) break;
      await sleep(250);
    }
  }
  await sleep(900); // transitions and the first tick
}

async function capture(p, file) {
  const b = JSON.parse(await p.eval("JSON.stringify(document.body.getBoundingClientRect())"));
  const scale = outScale ? outScale / (await p.eval("window.devicePixelRatio")) : SCALE;
  await p.send("Emulation.setDefaultBackgroundColorOverride", { color: { r: 0, g: 0, b: 0, a: 0 } });
  const shot = await p.send("Page.captureScreenshot", {
    format: "webp", quality: 84, fromSurface: true,
    clip: { x: b.x, y: b.y, width: b.width, height: b.height, scale },
  });
  fs.mkdirSync(path.dirname(file), { recursive: true });
  fs.writeFileSync(file, Buffer.from(shot.result.data, "base64"));
  return fs.statSync(file).size;
}

// ── run ──
console.log(`${widgets.length} widgets × ${themes.length} themes × ${LANGS.length} languages`);
spawn(exe, [`--data-dir=${data}`], { env, detached: true, stdio: "ignore" }).unref();
await sleep(6000);
try {
  for (const i of config().instances || []) cmd(`remove ${i.id}`);
  // a system monitor stays on (out of the way) all along, so FullWidget records CPU, memory and
  // network history and the graphs have something to show
  cmd("add fullwidget.sysinfo");
  await sleep(1500);
  const warmer = config().instances.find((i) => i.widget === "fullwidget.sysinfo").id;
  cmd(`move ${warmer} 1900 1150`);
  let total = 0, count = 0;
  for (const lang of LANGS) {
    cmd(`lang ${lang}`);
    await sleep(800);
    for (const { folder, m } of widgets) {
      const sample = samples[m.id] || {};
      cmd(`add ${m.id}`);
      await sleep(1500);
      const inst = config().instances.filter((i) => i.widget === m.id && i.id !== warmer).pop();
      // "{root}" in a sample value is this folder, e.g. the sample photos in build/preview-photos
      const val = (v) => (typeof v === "string" ? v.replaceAll("{root}", root) : v);
      const set = Object.entries(sample.settings || {}).map(([k, v]) => `setting ${inst.id} ${k} ${JSON.stringify(val(pick(v, lang)))}`);
      cmd([`layer ${inst.id} top`, `move ${inst.id} ${process.env.PREVIEW_AT || "300 300"}`, ...(outScale ? [`shadow ${inst.id} off`] : []), ...set].join("; "));
      const host = `${m.id.replace(/[^a-z0-9-]/gi, "-").toLowerCase()}.widgets.local`;
      let p = await page(host, inst.id);
      if (sample.storage) {
        for (const [k, v] of Object.entries(sample.storage)) await p.eval(`widget.storage.set(${JSON.stringify(k)}, ${JSON.stringify(fill(pick(v, lang)))})`);
        p.close();
        cmd(`reload ${inst.id}`);
        await sleep(600);
        p = await page(host, inst.id);
      }
      if (sample.warmup) await sleep(sample.warmup * 1000); // e.g. a few pings first
      for (const theme of themes) {
        cmd(`theme-of ${inst.id} ${theme}`);
        await settle(p, sample.network);
        if (sample.script) await p.eval(photos(pick(sample.script, lang)));
        // e.g. a picture that must finish loading
        if (sample.ready) for (let k = 0; k < 40 && !(await p.eval(sample.ready).catch(() => false)); k++) await sleep(250);
        if (sample.script || sample.ready) await sleep(500);
        const file = outDir ? path.join(outDir, folder, `${theme}.${lang}.webp`) : path.join(widgetsDir, folder, "preview", `${theme}.${lang}.webp`);
        total += await capture(p, file);
        count++;
      }
      p.close();
      cmd(`remove ${inst.id}`);
      process.stdout.write(`${lang} ${m.id}\n`);
    }
  }
  console.log(`${count} pictures, ${(total / 1024).toFixed(0)} KB`);
} finally {
  try { cmd("exit"); } catch { /* already gone */ }
  await sleep(2000);
  fs.rmSync(data, { recursive: true, force: true });
}
