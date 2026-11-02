const $ = (id) => document.getElementById(id);
const t = widget.t;
let opts = widget.settings.get();
const pad = (n) => String(n).padStart(2, "0");
const reduced = matchMedia("(prefers-reduced-motion: reduce)").matches;

/** One split-flap card: two still halves and two flaps that turn on the hinge. */
function makeCard(cls) {
  const el = document.createElement("div");
  el.className = `flip ${cls}`;
  el.innerHTML = `<div class="half top"><span></span></div><div class="half bottom"><span></span></div>
    <div class="flap top"><span></span></div><div class="flap bottom"><span></span></div>`;
  const [top, bottom, flapTop, flapBottom] = el.children;
  let value = null;
  return {
    el,
    set(next, animate) {
      if (next === value) return;
      const old = value;
      value = next;
      if (old == null || !animate || reduced) {
        for (const h of el.children) h.firstChild.textContent = next;
        return;
      }
      // the new top waits behind; the old top falls; the new bottom swings down over the old one
      top.firstChild.textContent = next;
      flapTop.firstChild.textContent = old;
      flapBottom.firstChild.textContent = next;
      bottom.firstChild.textContent = old;
      flapTop.getAnimations().forEach((a) => a.cancel());
      flapBottom.getAnimations().forEach((a) => a.cancel());
      flapTop.animate([{ transform: "rotateX(0deg)" }, { transform: "rotateX(-90deg)" }],
        { duration: 260, easing: "cubic-bezier(0.5, 0, 0.9, 0.6)", fill: "forwards" })
        .finished.then(() => { flapTop.firstChild.textContent = next; flapTop.getAnimations().forEach((a) => a.cancel()); }).catch(() => {});
      flapBottom.animate([{ transform: "rotateX(90deg)" }, { transform: "rotateX(-8deg)", offset: 0.8 }, { transform: "rotateX(0deg)" }],
        { duration: 340, delay: 240, easing: "cubic-bezier(0.2, 0.7, 0.3, 1)", fill: "both" })
        .finished.then(() => { bottom.firstChild.textContent = next; flapBottom.getAnimations().forEach((a) => a.cancel()); }).catch(() => {});
    },
  };
}

const cards = { h: makeCard("hours"), m: makeCard("minutes"), s: makeCard("sec") };

function layout() {
  const row = $("row");
  row.replaceChildren(cards.h.el, cards.m.el, ...(opts.showSeconds ? [cards.s.el] : []));
  document.body.classList.toggle("with-sec", !!opts.showSeconds);
  sizeDigits();
}

/** Digits fill their card: the font size follows the card's height. */
function sizeDigits() {
  // the card's own size in CSS pixels (not on-screen pixels, which grow when the card is zoomed)
  const w = cards.h.el.offsetWidth, ht = cards.h.el.offsetHeight;
  const size = Math.min(ht * 0.7, w * 0.58);
  document.documentElement.style.setProperty("--digit", `${size.toFixed(1)}px`);
  cards.s.el.style.setProperty("--digit", `${(size * 0.58).toFixed(1)}px`);
}

function render(animate) {
  const now = new Date();
  const h = now.getHours();
  cards.h.set(opts.use24h ? pad(h) : String(h % 12 || 12), animate);
  cards.m.set(pad(now.getMinutes()), animate);
  if (opts.showSeconds) cards.s.set(pad(now.getSeconds()), animate);
  $("ampm").textContent = opts.use24h ? "" : h < 12 ? t({ zh: "上午", en: "AM" }) : t({ zh: "下午", en: "PM" });
  const loc = widget.locale;
  $("date").textContent = new Intl.DateTimeFormat(loc, widget.theme === "paper"
    ? { month: "long", day: "numeric", weekday: "long" }
    : { month: "long", day: "numeric", weekday: "short" }).format(now);
  document.body.classList.toggle("no-date", !opts.showDate);
  document.body.classList.toggle("no-head", !opts.showDate && opts.use24h);
}

let timer = 0;
function schedule() {
  clearTimeout(timer);
  render(true);
  const now = Date.now();
  timer = setTimeout(schedule, opts.showSeconds ? 1000 - (now % 1000) + 5 : 60000 - (now % 60000) + 20);
}

widget.settings.onChange((s) => {
  const relayout = s.showSeconds !== opts.showSeconds;
  opts = s;
  if (relayout) layout();
  render(false);
  requestAnimationFrame(sizeDigits);
  schedule();
});
widget.appearance.onChange(() => { render(false); requestAnimationFrame(sizeDigits); });
let sizing = 0;
new ResizeObserver(() => { cancelAnimationFrame(sizing); sizing = requestAnimationFrame(sizeDigits); }).observe($("row"));
layout();
render(false);
schedule();
