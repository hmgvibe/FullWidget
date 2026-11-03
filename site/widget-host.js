/* A small stand-in for the FullWidget app, so a real built-in widget can run on this page.
   The widget's own HTML, CSS and JS are loaded unchanged into an iframe; this file plays the
   part of the app: it injects the SDK and theme like the app does, answers the widget's calls
   (settings, storage in this browser, nothing that touches the network or the PC), and can change
   the theme and look on the fly. */
(function () {
  "use strict";

  // What the app injects for each built-in theme (read from FullWidget 1.2.0).
  const THEMES = {
    instrument: ":root{--w-system-accent:#4cc2ff;--w-bg:rgba(16,18,22,0.74);--w-bg-solid:rgb(16,18,22);--w-fg:#e9ecef;--w-accent:#7de2c4;--w-radius:22px;--w-padding:18px;}",
    type: ":root{--w-system-accent:#4cc2ff;--w-bg:rgba(10,15,36,0);--w-bg-solid:rgb(10,15,36);--w-fg:#ffffff;--w-accent:#4cc2ff;--w-radius:18px;--w-padding:12px;--w-shadow:0 0 0 transparent;}",
    paper: ":root{--w-system-accent:#4cc2ff;--w-bg:rgba(243,238,227,1);--w-bg-solid:rgb(243,238,227);--w-fg:#1f1c18;--w-accent:#c23b2b;--w-radius:4px;--w-padding:18px;}",
  };

  let root = document.baseURI;
  const abs = (p) => new URL(p, root).href;
  let seq = 0;
  const themeCss = async (theme) => THEMES[theme] || THEMES.instrument;

  /**
   * Runs a built-in widget in `frame` (an <iframe>).
   * options: folder ("clock"), locale, theme, settings (overrides), size {width, height} in CSS px,
   * root (where widgets/ and sdk/ are; default: this page's folder), head (extra HTML placed in
   * the widget's <head> before anything else, e.g. a fixed clock for the promo video),
   * storage (starting contents, kept in memory only), calls (more host calls: { "system.subscribe": fn }).
   * Returns { setTheme(theme), setLook(css), send(event, data) }.
   */
  async function mount(frame, options) {
    if (options.root) root = new URL(options.root, document.baseURI).href;
    const id = "w" + ++seq;
    const base = abs(`widgets/${options.folder}/`);
    const manifest = await (await fetch(base + "widget.json")).json();
    const html = await (await fetch(base + (manifest.entry || "index.html"))).text();

    const settings = {};
    // a default may be given per language ({ "zh-TW": …, "en": … }), as the app resolves it
    const loc = options.locale || "zh-TW";
    const pickLang = (v) => (v && typeof v === "object" && !Array.isArray(v) && ("zh-TW" in v || "en" in v) ? v[loc] ?? v.en ?? v["zh-TW"] : v);
    for (const s of manifest.settings || []) if (s.key) settings[s.key] = pickLang(s.default);
    Object.assign(settings, options.settings || {});
    const storeKey = `fw-site:${options.folder}`;
    const keep = !options.storage; // given contents (the promo) aren't saved in this browser
    const store = options.storage ? JSON.parse(JSON.stringify(options.storage)) : JSON.parse(localStorage.getItem(storeKey) || "{}");
    const save = () => { if (keep) localStorage.setItem(storeKey, JSON.stringify(store)); };

    let theme = options.theme || "instrument";
    let look = "";
    const appearance = async () => ({
      theme, themeHref: abs(`sdk/themes/${theme}/theme.css`), style: "transparent", shadow: false, margin: 0,
      themeCss: await themeCss(theme), userCss: "html:root{--w-margin:0px;--w-shadow:0 0 0 transparent;}" + look, values: {},
    });
    const boot = {
      hostVersion: "site", instanceId: id, widgetId: manifest.id, widgetVersion: manifest.version,
      locale: options.locale || "zh-TW", settings, appearance: await appearance(),
      state: { locked: true, layer: "desktop", clickThrough: false, edit: false, label: "" },
      locked: true, layer: "desktop", clickThrough: false, resizable: false,
      size: { width: manifest.size.width, height: manifest.size.height, grow: manifest.size.grow || 0 },
      developer: false, permissions: [],
    };

    // the page's side of the message channel
    const deliver = (msg) => frame.contentWindow && frame.contentWindow.__fwDeliver && frame.contentWindow.__fwDeliver(msg);
    const calls = {
      "storage.get": (p) => store[p.key] ?? null,
      "storage.set": (p) => { store[p.key] = p.value; save(); return null; },
      "storage.remove": (p) => { delete store[p.key]; save(); return null; },
      "storage.keys": () => Object.keys(store),
      "settings.set": (p) => { Object.assign(settings, p.values || { [p.key]: p.value }); deliver({ ev: "settings", d: { ...settings } }); return null; },
      "text.chinese": (p) => p.text,
      "window.getSize": () => options.size || boot.size,
      ...(options.calls || {}),
    };
    window.__fwHosts = window.__fwHosts || {};
    window.__fwHosts[id] = (msg) => {
      if (msg.id === undefined) return; // "ready", "log", "window.drag": nothing to do on a web page
      const fn = calls[msg.m];
      Promise.resolve().then(() => {
        if (!fn) throw new Error("not available on the website");
        return fn(msg.p || {});
      }).then((r) => deliver({ id: msg.id, r }), (e) => deliver({ id: msg.id, e: e.message }));
    };

    // the widget's side: chrome.webview, the way WebView2 provides it in the app
    const shim = `<script>(function(){var host=parent.__fwHosts[${JSON.stringify(id)}],ls=[];
      window.chrome=window.chrome||{};window.chrome.webview={postMessage:function(m){host(JSON.parse(JSON.stringify(m)))},
      addEventListener:function(t,f){if(t==="message")ls.push(f)},removeEventListener:function(t,f){ls=ls.filter(function(x){return x!==f})}};
      window.__fwDeliver=function(m){ls.forEach(function(f){f({data:m})})};})();<\/script>`;
    const a = boot.appearance;
    const head = (options.head || "") + `<base href="${base}">` +
      `<link rel="stylesheet" href="${abs("sdk/base.css")}">` +
      `<link id="__fw_theme_link" rel="stylesheet" href="${a.themeHref}">` +
      `<style id="__fw_theme">${a.themeCss}</style>` +
      `<script>window.__FW_BOOT__=${JSON.stringify(boot).replace(/</g, "\\u003c")};<\/script>` + shim +
      `<script src="${abs("sdk/widget-sdk.js")}"><\/script>`;
    const tail = `<style id="__fw_user">${a.userCss}</style>`;
    let doc = /<head[^>]*>/i.test(html) ? html.replace(/<head[^>]*>/i, (m) => m + head) : head + html;
    doc = /<\/body>/i.test(doc) ? doc.replace(/<\/body>/i, tail + "</body>") : doc + tail;
    frame.srcdoc = doc;
    await new Promise((r) => frame.addEventListener("load", r, { once: true }));

    const push = async () => deliver({ ev: "appearance", d: await appearance() });
    return {
      async setTheme(t) { if (t !== theme) { theme = t; await push(); } },
      async setLook(css) { if (css !== look) { look = css; await push(); } },
      get theme() { return theme; },
      send(ev, d) { deliver({ ev, d }); },
    };
  }

  window.FWHost = { mount };
})();
