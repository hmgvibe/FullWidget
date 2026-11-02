// Builds the website into _site/ (for GitHub Pages, or to look at locally):
//   node site/build.mjs
// - the page itself (site/*), with the version and download links filled in from FullWidget.csproj
// - the widget SDK and the clock widget, which runs live on the page (see widget-host.js)
// - the widget pictures (site/assets/widgets, made by: node build/make-previews.mjs --scale 2 --out site/assets/widgets)
// - widgets.json: name, category and picture size of every built-in widget, for the masonry wall
import fs from "node:fs";
import path from "node:path";

const root = path.resolve(import.meta.dirname, "..");
const site = import.meta.dirname;
const out = path.join(root, "_site");
const repo = "hmgvibe/FullWidget";

const csproj = fs.readFileSync(path.join(root, "src", "FullWidget", "FullWidget.csproj"), "utf8");
const version = csproj.match(/<Version>([^<]+)<\/Version>/)[1];
const release = `https://github.com/${repo}/releases/download/v${version}`;

fs.rmSync(out, { recursive: true, force: true });
fs.mkdirSync(out, { recursive: true });

// the page
for (const entry of fs.readdirSync(site, { withFileTypes: true })) {
  if (entry.name === "build.mjs") continue;
  fs.cpSync(path.join(site, entry.name), path.join(out, entry.name), { recursive: true });
}
const page = path.join(out, "index.html");
fs.writeFileSync(page, fs.readFileSync(page, "utf8")
  .replaceAll("{{VERSION}}", version)
  .replaceAll("{{FULL_URL}}", `${release}/FullWidget-${version}-win-x64-full.zip`)
  .replaceAll("{{SLIM_URL}}", `${release}/FullWidget-${version}-win-x64.zip`)
  .replaceAll("{{RELEASE_URL}}", `https://github.com/${repo}/releases/tag/v${version}`)
  .replaceAll("{{REPO_URL}}", `https://github.com/${repo}`));

// what the live clock needs: the SDK and the widget's own files
fs.cpSync(path.join(root, "sdk"), path.join(out, "sdk"), { recursive: true, filter: (f) => !f.endsWith(".d.ts") });
fs.cpSync(path.join(root, "widgets", "clock"), path.join(out, "widgets", "clock"), { recursive: true, filter: (f) => !f.includes(`${path.sep}preview`) });

// the masonry wall's list
const pictures = path.join(site, "assets", "widgets");
const webpSize = (file) => {
  const b = fs.readFileSync(file);
  const kind = b.toString("ascii", 12, 16);
  if (kind === "VP8X") return [1 + b.readUIntLE(24, 3), 1 + b.readUIntLE(27, 3)];
  if (kind === "VP8L") { const v = b.readUInt32LE(21); return [(v & 0x3fff) + 1, ((v >> 14) & 0x3fff) + 1]; }
  return [b.readUInt16LE(26) & 0x3fff, b.readUInt16LE(28) & 0x3fff];
};
const list = [];
for (const folder of fs.readdirSync(path.join(root, "widgets")).sort()) {
  const manifest = path.join(root, "widgets", folder, "widget.json");
  if (!fs.existsSync(manifest) || !fs.existsSync(path.join(pictures, folder))) continue;
  const m = JSON.parse(fs.readFileSync(manifest, "utf8"));
  const [w, h] = webpSize(path.join(pictures, folder, "instrument.zh-TW.webp"));
  list.push({ folder, name: m.name, description: m.description, category: m.category, w, h });
}
fs.writeFileSync(path.join(out, "widgets.json"), JSON.stringify(list));
fs.writeFileSync(path.join(out, ".nojekyll"), "");
console.log(`FullWidget ${version}: ${list.length} widgets → ${path.relative(root, out)}/`);
