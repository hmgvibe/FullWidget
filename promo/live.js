/* Real built-in widgets, running in the promo with a virtual clock.
   Each widget's iframe gets a small runtime in place of Date, setTimeout, setInterval and
   requestAnimationFrame. The timeline moves that clock forward frame by frame, so the widgets
   really tick, count down and redraw, at whatever speed a shot wants, and every recording of the
   same frame is the same. System figures and media playback are made up, but smooth and steady. */
(function () {
  "use strict";
  const BASE = new Date(2026, 10, 3, 9, 41, 0).getTime(); // 9:41 on Tuesday 3 November 2026

  const RUNTIME = `<script>(function(){var R=Date,B=${BASE},now=B,q=[],raf=[],seq=0;
class D extends R{constructor(...a){super(...(a.length?a:[now]))}static now(){return now}}window.Date=D;
try{performance.now=function(){return now-B}}catch(e){}
function add(f,ms,args,every){var id=++seq;q.push({id:id,at:now+Math.max(0,+ms||0),f:f,args:args,every:every});return id}
window.setTimeout=function(f,ms){return add(f,ms,[].slice.call(arguments,2))};
window.setInterval=function(f,ms){return add(f,ms,[].slice.call(arguments,2),Math.max(1,+ms||1))};
window.clearTimeout=window.clearInterval=function(id){q=q.filter(function(x){return x.id!==id});raf=raf.filter(function(x){return x.id!==id})};
window.requestAnimationFrame=function(f){var id=++seq;raf.push({id:id,f:f});return id};
window.cancelAnimationFrame=window.clearTimeout;
window.__fwAt=function(at,f){return add(f,at-now,[])};
window.__fwNow=function(){return now};
window.__fwAdvance=function(to){var n=0;for(;;){var b=null;for(var i=0;i<q.length;i++){var x=q[i];if(x.at<=to&&(!b||x.at<b.at||(x.at===b.at&&x.id<b.id)))b=x}
if(!b||++n>50000)break;now=Math.max(now,b.at);if(b.every)b.at+=b.every;else q.splice(q.indexOf(b),1);try{if(typeof b.f==="function")b.f.apply(null,b.args)}catch(e){console.error(e)}}
now=Math.max(now,to);for(var k=0;k<3;k++){var r=raf;raf=[];if(!r.length)break;r.forEach(function(x){try{x.f(now-B)}catch(e){console.error(e)}})}};
})();<\/script><style>*,*::before,*::after{transition:none!important;animation:none!important;caret-color:transparent!important}</style>`;

  // ── made-up system figures: smooth waves, the same at the same moment ──
  const GB = 1024 ** 3;
  function sys(vt) {
    const s = (vt - BASE) / 1000;
    const cpu = Math.min(96, Math.max(3, 17 + 11 * Math.sin(s * 0.9) + 7 * Math.sin(s * 2.3 + 1) + 4 * Math.sin(s * 5.1)));
    const mem = 61 + 3 * Math.sin(s * 0.21);
    const down = Math.round(1.6e6 * (0.55 + 0.45 * Math.sin(s * 0.7)) + 4e5 * Math.sin(s * 3.1) ** 2);
    const up = Math.round(2.2e5 * (0.6 + 0.4 * Math.sin(s * 1.1 + 2)));
    const total = 16 * GB, used = Math.round((total * mem) / 100);
    return {
      cpu: { usage: Math.round(cpu * 10) / 10, cores: 14 },
      memory: { usage: Math.round(mem * 10) / 10, total, used, available: total - used },
      network: { down, up, totalDown: Math.round(1.9 * GB + s * 1.3e6), totalUp: Math.round(0.86 * GB + s * 2.1e5) },
    };
  }
  function history(vt) {
    const points = [];
    for (let i = 899; i >= 0; i--) {
      const at = vt - i * 1000, x = sys(at);
      points.push([at, x.cpu.usage, x.memory.usage, x.network.down, x.network.up]);
    }
    return { now: vt, points };
  }
  const COVER = "data:image/svg+xml," + encodeURIComponent("<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 100 100'><defs><linearGradient id='g' x1='0' y1='0' x2='1' y2='1'><stop offset='0' stop-color='#ff7a93'/><stop offset='1' stop-color='#ffc36b'/></linearGradient></defs><rect width='100' height='100' fill='url(#g)'/><circle cx='64' cy='36' r='15' fill='#fff6e0' opacity='.85'/><path d='M0 74 L28 58 L52 72 L78 55 L100 65 V100 H0Z' fill='#3a2140' opacity='.35'/><path d='M0 86 L36 74 L62 84 L100 76 V100 H0Z' fill='#3a2140' opacity='.5'/></svg>");
  const MEDIA = (vt, title) => ({
    available: true, app: { id: "", name: "", icon: null }, title: title || COPY.media.title, artist: COPY.media.artist, album: COPY.media.album,
    status: "playing", playing: true, position: 77000, duration: 214000, updated: vt, cover: COVER,
    can: { play: true, pause: true, toggle: true, next: true, previous: true, seek: true },
  });

  /**
   * A live widget in `frame`. options: folder, theme, locale, settings, storage, script (run once
   * mounted), t0 (video time its clock starts moving), speed (virtual seconds per second),
   * actions: [[videoTime, (win, doc) => …]] (clicks, typing), mediaTitle.
   */
  async function mount(frame, o) {
    const w = { frame, o, host: null, vt: BASE, sysOn: false, sysSec: -1 };
    const vtAt = (t) => BASE + Math.max(0, t - (o.t0 || 0)) * 1000 * (o.speed || 1);
    w.host = await FWHost.mount(frame, {
      folder: o.folder, root: "../", locale: o.locale || COPY.lang, theme: o.theme || "instrument", head: RUNTIME,
      settings: o.settings, storage: o.storage || {},
      calls: {
        "system.subscribe": () => { w.sysOn = true; return sys(w.vt); },
        "system.unsubscribe": () => { w.sysOn = false; return null; },
        "system.history": () => history(w.vt),
        "media.subscribe": () => MEDIA(w.vt, o.mediaTitle),
        "media.get": () => MEDIA(w.vt, o.mediaTitle),
        "media.unsubscribe": () => null,
        "media.control": () => null,
        "notify.show": () => null,
        "notify.cancel": () => null,
        "audio.get": () => ({ available: true, volume: 46, muted: false, device: COPY.device }),
        "audio.subscribe": () => ({ available: true, volume: 46, muted: false, device: COPY.device }),
      },
    });
    const win = frame.contentWindow;
    if (o.script) win.eval(o.script);
    for (const [t, fn] of o.actions || []) win.__fwAt(vtAt(t), () => fn(win, frame.contentDocument));
    w.sync = (t) => {
      const vt = vtAt(t);
      if (vt < w.vt) return; // the preview went back in time; the widget keeps where it was
      w.vt = vt;
      if (w.sysOn && Math.floor(vt / 1000) !== w.sysSec) { w.sysSec = Math.floor(vt / 1000); w.host.send("system", sys(vt)); }
      win.__fwAdvance(vt);
    };
    w.setTheme = (th) => w.host.setTheme(th);
    w.setLook = (css) => w.host.setLook(css);
    return w;
  }

  /** Clicks an element inside a widget, the way a pointer would. */
  function click(doc, selector) {
    const el = typeof selector === "string" ? doc.querySelector(selector) : selector;
    if (!el) return;
    for (const type of ["pointerdown", "mousedown", "pointerup", "mouseup", "click"])
      el.dispatchEvent(new (type.startsWith("pointer") ? doc.defaultView.PointerEvent : doc.defaultView.MouseEvent)(type, { bubbles: true, cancelable: true, button: 0 }));
  }

  window.Live = { BASE, mount, click, sys };
})();
