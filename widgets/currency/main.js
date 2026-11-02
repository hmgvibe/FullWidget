const $ = (id) => document.getElementById(id);
const t = widget.t;
const esc = (s) => String(s ?? "").replace(/[&<>"]/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" })[c]);
let opts = widget.settings.get();
let data = null;      // { base, at, updated, rates } from the service
let previous = null;  // the day before, for the change arrows
let amount = null;    // what the user typed, in the base currency
let failed = false;

const names = new Intl.DisplayNames(widget.locale, { type: "currency" });
const nameOf = (c) => { try { return names.of(c); } catch { return c; } };
const base = () => (/^[A-Z]{3}$/.test(opts.base || "") ? opts.base : "USD");
const list = () => [...new Set(String(opts.list || "").toUpperCase().split(/[^A-Z]+/).filter((c) => c.length === 3 && c !== base()))].slice(0, 12);

// currencies without minor units, or with huge numbers per dollar
const WHOLE = new Set(["JPY", "KRW", "VND", "IDR", "CLP", "ISK", "HUF", "TWD"]);
function money(v, code) {
  const digits = WHOLE.has(code) && Math.abs(v) >= 100 ? 0 : Math.abs(v) >= 1000 ? 0 : Math.abs(v) >= 1 ? 2 : 4;
  return new Intl.NumberFormat(widget.locale, { maximumFractionDigits: digits, minimumFractionDigits: Math.min(digits, 2) }).format(v);
}

async function refresh() {
  try {
    const r = await widget.fetch(`https://open.er-api.com/v6/latest/${base()}`);
    const j = await r.json();
    if (j.result !== "success") throw new Error(j["error-type"] || "failed");
    const next = { base: j.base_code, at: Date.now(), updated: j.time_last_update_unix * 1000, rates: j.rates };
    // keep the last day's rates for comparison
    const days = (await widget.storage.get("days")) || {};
    const day = new Date(next.updated).toISOString().slice(0, 10);
    days[`${next.base}:${day}`] = next.rates;
    const keys = Object.keys(days).filter((k) => k.startsWith(`${next.base}:`)).sort();
    for (const k of Object.keys(days)) if (!keys.slice(-2).includes(k)) delete days[k];
    await widget.storage.set("days", days);
    const prevKey = keys.filter((k) => k < `${next.base}:${day}`).pop();
    previous = prevKey ? days[prevKey] : null;
    data = next;
    failed = false;
    widget.storage.set("last", data);
  } catch (e) {
    widget.log(`currency: ${e.message}`);
    failed = true;
    if (!data || data.base !== base()) {
      data = null;
      showMessage(t({ "zh-TW": "暫時取不到匯率", "zh-CN": "暂时取不到汇率", en: "Can't load rates right now" }), true);
      return;
    }
  }
  render();
}

function showMessage(text, retry) {
  $("rows").hidden = true;
  $("msg").hidden = false;
  $("msg-text").textContent = text;
  $("retry").hidden = !retry;
}

function render() {
  if (!data) return;
  $("msg").hidden = true;
  $("rows").hidden = false;
  const b = data.base;
  $("base").textContent = b;
  $("amt").placeholder = t({ "zh-TW": "輸入金額來換算", "zh-CN": "输入金额来换算", en: "Amount to convert" });
  $("rows").innerHTML = list().filter((c) => data.rates[c]).map((c) => {
    const rate = data.rates[c];                 // 1 base = rate c
    const per = 1 / rate;                       // 1 c = per base
    const unit = WHOLE.has(c) && per < 0.5 ? 100 : 1; // "100 JPY = 20.7 TWD" reads better than 0.207
    let change = "";
    if (previous && previous[c]) {
      // change in what one unit of c costs in base; up = c got dearer
      const pct = ((previous[c] / rate) - 1) * 100;
      if (Math.abs(pct) >= 0.01) change = `<span class="chg ${pct > 0 ? "up" : "down"}">${pct > 0 ? "▲" : "▼"} ${Math.abs(pct).toFixed(2)}%</span>`;
    }
    // the headline: what one (or a hundred) of that currency costs in mine; the conversion only when asked
    const conv = amount != null
      ? `${money(amount, b)} ${b} = ${money(amount * rate, c)} ${c}`
      : `${unit} ${c} → ${b}`;
    return `<li class="row">
      <span class="code w-num">${c}</span>
      <span class="name">${esc(nameOf(c))}</span>
      <span class="val w-num">${money(per * unit, b)}</span>
      <span class="per">${conv}${change}</span>
    </li>`;
  }).join("") || `<li class="none">${esc(t({ "zh-TW": "在設定裏填上要看的貨幣", "zh-CN": "在设置里填写要看的货币", en: "Choose currencies in Settings" }))}</li>`;
  const d = new Intl.DateTimeFormat(widget.locale, { month: "numeric", day: "numeric" }).format(data.updated);
  $("date").textContent = failed ? t({ "zh-TW": `離線 · ${d} 的匯率`, "zh-CN": `离线 · ${d} 的汇率`, en: `Offline · rates of ${d}` })
    : t({ "zh-TW": `${d} 更新`, "zh-CN": `${d} 更新`, en: `Updated ${d}` });
}

$("amt").addEventListener("input", () => {
  const v = parseFloat($("amt").value.replace(/[,，\s]/g, ""));
  amount = Number.isFinite(v) && v >= 0 ? v : null;
  render();
  widget.storage.set("amount", amount);
});
// the service's terms ask for this credit
$("credit").textContent = t({ "zh-TW": "資料：ExchangeRate-API", "zh-CN": "数据：ExchangeRate-API", en: "Rates by ExchangeRate-API" });
$("credit").onclick = (e) => { e.preventDefault(); widget.shell.open("https://www.exchangerate-api.com").catch(() => {}); };
$("retry").textContent = t({ "zh-TW": "重試", "zh-CN": "重试", en: "Retry" });
$("retry").onclick = refresh;

widget.settings.onChange((s) => {
  const rebase = s.base !== opts.base;
  opts = s;
  if (rebase) { data = null; previous = null; refresh(); } else render();
});
widget.appearance.onChange(render);

(async () => {
  amount = await widget.storage.get("amount");
  if (amount != null) $("amt").value = String(amount);
  const last = await widget.storage.get("last");
  if (last && last.base === base()) {
    data = last;
    const days = (await widget.storage.get("days")) || {};
    const keys = Object.keys(days).filter((k) => k.startsWith(`${base()}:`)).sort();
    const today = `${base()}:${new Date(data.updated).toISOString().slice(0, 10)}`;
    const prevKey = keys.filter((k) => k < today).pop();
    previous = prevKey ? days[prevKey] : null;
    render();
  } else showMessage(t({ "zh-TW": "正在載入…", "zh-CN": "正在加载…", en: "Loading…" }), false);
  // the service updates once a day; look every 3 hours
  if (!data || Date.now() - data.at > 3 * 3600e3) await refresh();
  setInterval(refresh, 3 * 3600e3);
})();
