/* FullWidget website: languages, the widget waterfall, the "deeper as you scroll" stage. */
(function () {
  "use strict";
  document.documentElement.classList.add("js");
  const $ = (s, el = document) => el.querySelector(s);
  const $$ = (s, el = document) => [...el.querySelectorAll(s)];
  const reduce = matchMedia("(prefers-reduced-motion: reduce)").matches;
  const { animate, inView, scroll, stagger } = window.Motion || {};

  // ───────────────────────── languages ─────────────────────────
  // Traditional Chinese is in the HTML; these are the other two.
  const T = {
    "zh-CN": {
      _title: "FullWidget — 一个不一样的 Windows 桌面小组件",
      _desc: "FullWidget：免费、开源的 Windows 11 桌面小组件。38 个组件、三套主题，打开就好看；想要更多，就用 HTML、CSS、JS 自己写。",
      skip: "跳到内容", navLabel: "页面", navEasy: "简单上手", navDeep: "深入自定义", navOpen: "开源", navDownload: "下载", langLabel: "语言",
      heroTitle: '一个不一样的<br><span class="nowrap">Windows 桌面小组件</span>',
      heroSub: '38 个组件、三套主题，打开就好看。<br>想要更多，就用 HTML、CSS、JS <span class="nowrap">自己写。</span><span class="nowrap">免费，开源。</span>',
      download: "下载 FullWidget", flowLabel: "38 个内置组件", flowPause: "暂停", flowPlay: "继续",
      easyKicker: "简单上手", easyTitle: '解压，双击，<span class="nowrap">桌面就变好看了。</span>',
      step1Title: "下载，解压，双击", step1Text: "不用安装，也不用账号。解压到任何文件夹，双击 FullWidget.exe 就能用，放在 U 盘里也行。",
      step2Title: "桌面马上有东西", step2Text: "时钟、天气和系统监控自己出现。拖动就能移动，靠近时自动对齐。",
      step3Title: "想要更多，就添加", step3Text: "在控制中心从 38 个组件里挑，点一下就放到桌面。三套主题随时换。",
      deepKicker: "深入自定义", deepTitle: '想改多深，<span class="nowrap">就改多深。</span>', clockTitle: "时钟组件",
      lookHead: "外观", lookAccent: "强调色", lookRadius: "圆角", lookOpacity: "背景不透明度",
      deep1Title: "换一套主题", deep1Text: "三套主题：「量被画出来」「书桌上的纸」「玻璃上的字」。一键换掉整个桌面，也可以每个组件各用各的。",
      deep2Title: "调外观", deep2Text: "颜色、透明度、圆角、字体和大小，在控制中心的表单里边调边看，不用写代码。",
      deep3Title: "写一段 CSS", deep3Text: '表单不够用时，直接写 CSS 盖过去。保存就生效，只影响<span class="nowrap">这一个组件。</span>',
      deep4Title: "自己写一个组件", deep4Text: "旁边这段，就是这个时钟自己的代码。一个组件就是一个小网页：HTML、CSS、JS，有类型提示、热重载和开发文档；写好打包成 .fwz 分享，或投稿到组件市场。",
      deepGuide: "看开发文档 ↗",
      openKicker: "开源・放心", openTitle: '免费，开源，<span class="nowrap">什么都不收集。</span>',
      z1: "账号", z2: "广告", z3: "统计", zeroTitle: "没有账号、广告、统计", zeroText: "FullWidget 不会把你的数据发到网上。只有你放上桌面、需要联网的组件（例如天气）才会联网。",
      gplTitle: "每一行代码都公开", gplText: '源代码在 GitHub 上，谁都可以看、可以改。发布改过的版本，也要<span class="nowrap">一样开源。</span>', gplLink: "在 GitHub 上看 ↗",
      permName: "安装「护眼提醒」", permAsk: "它要：", permNotify: "显示通知（每 20 分钟提醒你看远处 20 秒）", permNo: "不会联网，也读不到你电脑上的文件。",
      permCancel: "取消", permOk: "安装并放到桌面",
      permTitle: "组件要先问你", permText: "别人写的组件要连哪些网站、用哪些系统功能，安装前都列出来，你同意才装。组件之间互相隔离。",
      mBase: "起步", mEach: "每多一个组件", mAll: '38 个全开<span class="nowrap">（实测）</span>',
      lightTitle: "轻，而且懂得让路", lightText: "所有组件共用一个网页引擎进程；全屏玩游戏或看视频时自动暂停，不抢性能。",
      getTitle: "下载 FullWidget", getSub: "免费。解压就能用。", getBtn: "下载完整版", getSlim: "精简版",
      getSlimNote: '（需先安装 <a href="https://dotnet.microsoft.com/download/dotnet/9.0">.NET 9 桌面运行时</a>）',
      specVer: "版本", specNotes: "版本说明", specOs: "系统", specOsVal: "Windows 11 · WebView2（已内置）", specLang: "语言", specLicense: "许可",
      ssTitle: "Windows 已保护你的电脑", ssMore: "更多信息", ssRun: "仍要运行", ssDont: "不运行",
      helpTitle: "第一次打开时", helpText: "Windows 可能显示「Windows 已保护你的电脑」。这是因为程序还没有代码签名（正在申请），不是病毒。",
      help1: "点 <b>更多信息</b>", help2: "点 <b>仍要运行</b>",
      footLabel: "页脚", footGuide: "开发文档", footPrivacy: "隐私", footNote: "© 2026 hmgvibe。「FullWidget」名称和标志不在开源许可范围内。",
      navWidgets: "组件",
      catKicker: "组件一览", catTitle: '38 个组件，<span class="nowrap">每天都用得上。</span>', catSub: "按分类看看都有什么。换个主题，每个组件都跟着变。",
      catLabel: "分类", themeLabel: "主题", thInstrument: "量被画出来", thPaper: "书桌上的纸", thType: "玻璃上的字",
      catMore: "显示全部 38 个", catAll: "全部", catTime: "时间", catWeather: "天气", catSystem: "系统", catProductivity: "效率", catInfo: "资讯", catMedia: "媒体与装饰",
      detKicker: "小细节", detTitle: '用起来顺手的<span class="nowrap">小地方。</span>',
      d1t: "靠近就对齐", d1p: "拖动时出现虚线参考线，靠近屏幕边缘或别的组件就吸过去，间距一样宽。按住 Ctrl 可以随意放。",
      d2t: "拉大，内容跟着变大", d2p: "拖右下角改大小，文字和图表一起放大，不会只多出一片空白。",
      d3t: "置顶，或沉到桌面", d3p: "每个组件可以选置顶、普通，或放在桌面层：被窗口盖住，不挡你做事。",
      d4t: "卡片、毛玻璃、透明", d4p: "组件的底可以是实心卡片、毛玻璃，或完全透明只留文字；阴影也能关掉。",
      d5t: "锁定和鼠标穿透", d5p: "锁定后就不会被误拖；开了穿透，点击会直接落到后面的桌面上。",
      d6t: "同一个放好几个", d6p: "例如放三个时钟，各看一个时区；每个都有自己的设置和外观。",
      d7t: "开机就在", d7p: "可以设置开机自动启动。组件的位置、大小和内容都会记住。",
      d8t: "放进 U 盘带走", d8p: "在 FullWidget.exe 旁边放一个叫 portable 的空文件，设置就存在程序旁边，整个文件夹带着走。",
      d9t: "三种语言", d9p: "繁体中文、简体中文和 English，跟随 Windows，也可以在设置里改。",
      copied: "已复制下载链接。FullWidget 是 Windows 电脑软件，请在电脑上打开。",
    },
    en: {
      _title: "FullWidget — A different kind of Windows desktop widget",
      _desc: "FullWidget: free, open-source desktop widgets for Windows 11. 38 widgets and three themes that look good from the start; write your own in HTML, CSS and JS.",
      skip: "Skip to content", navLabel: "Page", navEasy: "Easy to start", navDeep: "Make it yours", navOpen: "Open source", navDownload: "Download", langLabel: "Language",
      heroTitle: 'A different kind of<br><span class="nowrap">Windows desktop widget</span>',
      heroSub: "38 widgets and three themes that look good from the start.<br>Want more? Write your own in HTML, CSS and JS. Free and open source.",
      download: "Download FullWidget", flowLabel: "38 built-in widgets", flowPause: "Pause", flowPlay: "Play",
      easyKicker: "Easy to start", easyTitle: "Unzip, double-click, and your desktop looks good.",
      step1Title: "Download, unzip, double-click", step1Text: "No installer, no account. Unzip it into any folder and double-click FullWidget.exe. It even runs from a USB stick.",
      step2Title: "Your desktop fills up", step2Text: "A clock, the weather and a system monitor appear by themselves. Drag them around; they snap into line.",
      step3Title: "Add more", step3Text: "Pick from 38 widgets in the Control Center and put one on the desktop with a click. Switch between three themes any time.",
      deepKicker: "Make it yours", deepTitle: "Go as deep as you like.", clockTitle: "Clock widget",
      lookHead: "Look", lookAccent: "Accent", lookRadius: "Corners", lookOpacity: "Background opacity",
      deep1Title: "Switch the theme", deep1Text: "Three themes: Instrument, Paper and Glass. Change the whole desktop at once, or give each widget its own.",
      deep2Title: "Adjust the look", deep2Text: "Colours, opacity, corners, font and size, in a form that shows every change as you make it. No code.",
      deep3Title: "Add some CSS", deep3Text: "When the form isn't enough, write CSS to override anything. Save and it applies, to that widget only.",
      deep4Title: "Write your own widget", deep4Text: "The code shown is this clock's own. A widget is a small web page: HTML, CSS and JS, with type hints, hot reload and a developer guide. Pack it as a .fwz file to share, or submit it to the widget market.",
      deepGuide: "Read the developer guide ↗",
      openKicker: "Open and safe", openTitle: "Free, open source, and it collects nothing.",
      z1: "accounts", z2: "ads", z3: "tracking", zeroTitle: "No account, ads or tracking", zeroText: "FullWidget sends none of your data anywhere. Only widgets you put on the desktop that need the internet (the weather, say) go online.",
      gplTitle: "Every line of code is public", gplText: "The source is on GitHub for anyone to read and change. Changed versions that are shared must be open source too.", gplLink: "See it on GitHub ↗",
      permName: "Install “Eye break”", permAsk: "It wants to:", permNotify: "Show notifications (a reminder every 20 minutes to look far away)", permNo: "It can't go online or read files on your PC.",
      permCancel: "Cancel", permOk: "Install and add to desktop",
      permTitle: "Widgets ask first", permText: "Before you install someone else's widget, you see every site it connects to and every system feature it uses; it installs only if you agree. Widgets are kept apart from each other.",
      mBase: "To start", mEach: "Each extra widget", mAll: "All 38 (measured)",
      lightTitle: "Light, and it gets out of the way", lightText: "All widgets share one web engine process, and they pause while a game or video runs full screen.",
      getTitle: "Download FullWidget", getSub: "Free. Unzip and run.", getBtn: "Download", getSlim: "Small version",
      getSlimNote: '(needs the <a href="https://dotnet.microsoft.com/download/dotnet/9.0">.NET 9 Desktop Runtime</a>)',
      specVer: "Version", specNotes: "Release notes", specOs: "System", specOsVal: "Windows 11 · WebView2 (built in)", specLang: "Languages", specLicense: "License",
      ssTitle: "Windows protected your PC", ssMore: "More info", ssRun: "Run anyway", ssDont: "Don't run",
      helpTitle: "The first time you open it", helpText: "Windows may say “Windows protected your PC”. That's because the app isn't code-signed yet (we've applied); it isn't a virus.",
      help1: "Click <b>More info</b>", help2: "Click <b>Run anyway</b>",
      footLabel: "Footer", footGuide: "Developer guide", footPrivacy: "Privacy", footNote: "© 2026 hmgvibe. The FullWidget name and logo are not covered by the open-source license.",
      navWidgets: "Widgets",
      catKicker: "All widgets", catTitle: "38 widgets for every day.", catSub: "Browse them by group. Switch the theme and every widget changes with it.",
      catLabel: "Group", themeLabel: "Theme", thInstrument: "Instrument", thPaper: "Paper", thType: "Glass",
      catMore: "Show all 38", catAll: "All", catTime: "Time", catWeather: "Weather", catSystem: "System", catProductivity: "Productivity", catInfo: "Info", catMedia: "Media & decor",
      detKicker: "Little things", detTitle: "The little things that make it easy.",
      d1t: "Snaps into line", d1p: "Dashed guides appear as you drag; near a screen edge or another widget it snaps, with even gaps. Hold Ctrl to place it freely.",
      d2t: "Bigger means bigger", d2p: "Drag the bottom-right corner to resize. Text and charts grow with it, instead of leaving empty space.",
      d3t: "On top, or on the desktop", d3p: "Keep a widget on top, in the normal layer, or down on the desktop, where windows cover it and it never gets in the way.",
      d4t: "Card, frosted or clear", d4p: "A widget can sit on a solid card or frosted glass, or be fully clear with only its text. Shadows can go too.",
      d5t: "Lock and click-through", d5p: "Lock a widget so it can't be dragged by accident. With click-through on, clicks go straight to the desktop behind it.",
      d6t: "As many as you like", d6p: "Put three clocks on the desktop, one per time zone. Each copy has its own settings and look.",
      d7t: "There when you start", d7p: "Let it start with Windows. Where each widget sits, its size and what it shows are all remembered.",
      d8t: "Take it with you", d8p: "Put an empty file named portable next to FullWidget.exe and settings stay beside the app, so the whole folder travels on a USB stick.",
      d9t: "Three languages", d9p: "Traditional Chinese, Simplified Chinese and English. It follows Windows, or pick one in Settings.",
      copied: "Download link copied. FullWidget is a Windows PC app: open the link on your computer.",
    },
  };
  const original = {}; // the Traditional Chinese text, read from the page
  const keyed = $$("[data-i18n],[data-i18n-html],[data-i18n-aria],[data-i18n-title]");
  for (const el of keyed) {
    if (el.dataset.i18n) original[el.dataset.i18n] = el.textContent;
    if (el.dataset.i18nHtml) original[el.dataset.i18nHtml] = el.innerHTML;
    if (el.dataset.i18nAria) original[el.dataset.i18nAria] = el.getAttribute("aria-label");
    if (el.dataset.i18nTitle) original[el.dataset.i18nTitle] = el.getAttribute("title");
  }
  original._title = document.title;
  original._desc = $('meta[name="description"]').content;
  original.flowPause = "暫停"; original.flowPlay = "繼續";
  Object.assign(original, { catAll: "全部", catTime: "時間", catWeather: "天氣", catSystem: "系統", catProductivity: "效率", catInfo: "資訊", catMedia: "媒體與裝飾" });
  original.copied = "已複製下載連結。FullWidget 是 Windows 電腦軟件，請在電腦上打開。";

  const supported = ["zh-TW", "zh-CN", "en"];
  function pickLang() {
    const asked = new URLSearchParams(location.search).get("lang");
    if (supported.includes(asked)) return asked;
    try { const saved = localStorage.getItem("fw-lang"); if (supported.includes(saved)) return saved; } catch { /* private mode */ }
    for (const l of navigator.languages || [navigator.language]) {
      const x = (l || "").toLowerCase();
      if (/^zh-(cn|sg|hans)/.test(x)) return "zh-CN";
      if (x.startsWith("zh")) return "zh-TW";
      if (x.startsWith("en")) return "en";
    }
    return "zh-TW";
  }
  let lang = pickLang();
  const tr = (key) => (lang === "zh-TW" ? original[key] : T[lang][key] ?? original[key]);

  function applyLang() {
    document.documentElement.lang = lang === "zh-TW" ? "zh-Hant" : lang === "zh-CN" ? "zh-CN" : "en";
    for (const el of keyed) {
      if (el.dataset.i18n) el.textContent = tr(el.dataset.i18n);
      if (el.dataset.i18nHtml) el.innerHTML = tr(el.dataset.i18nHtml);
      if (el.dataset.i18nAria) el.setAttribute("aria-label", tr(el.dataset.i18nAria));
      if (el.dataset.i18nTitle) el.setAttribute("title", tr(el.dataset.i18nTitle));
    }
    document.title = tr("_title");
    $('meta[name="description"]').content = tr("_desc");
    for (const b of $$(".langs button")) b.setAttribute("aria-pressed", String(b.dataset.lang === lang));
    for (const a of $$("[data-guide]")) a.href = a.href.replace(/docs\/.*$/, lang === "en" ? "docs/widget-guide.en.md" : "docs/組件開發指南.md");
    for (const img of $$("img[data-w]")) img.src = pic(img.dataset.w, img.dataset.t);
    for (const img of $$("img[data-app]")) img.src = `assets/app/${img.dataset.app}.${lang}.webp`;
    for (const fig of $$(".tile")) { const img = $("img", fig); img.src = pic(fig.dataset.folder, fig.dataset.theme); $("figcaption", fig).textContent = name(fig.dataset.folder); img.alt = name(fig.dataset.folder); }
    const pause = $(".flow-pause"); if (pause) pause.textContent = tr(flowPaused ? "flowPlay" : "flowPause");
    catLang();
    codeLang();
  }
  for (const b of $$(".langs button")) b.addEventListener("click", () => {
    if (b.dataset.lang === lang) return;
    lang = b.dataset.lang;
    try { localStorage.setItem("fw-lang", lang); } catch { /* private mode */ }
    applyLang();
    mountClock();
  });

  const pic = (folder, theme) => `assets/widgets/${folder}/${theme}.${lang}.webp`;
  let widgets = [];
  const name = (folder) => { const w = widgets.find((x) => x.folder === folder); return w ? (w.name[lang] || w.name.en) : ""; };

  // ───────────────────────── top bar ─────────────────────────
  const top = $("#top");
  const darks = $$(".deep, .get");
  function topState() {
    top.classList.toggle("scrolled", scrollY > 8);
    const y = top.offsetHeight / 2;
    top.classList.toggle("on-dark", darks.some((s) => { const r = s.getBoundingClientRect(); return r.top <= y && r.bottom >= y; }));
  }
  addEventListener("scroll", topState, { passive: true });
  topState();

  // ───────────────────────── ① the waterfall ─────────────────────────
  const flow = $("#flow");
  let flowPaused = false;
  function columnsFor(width) { return width >= 1680 ? 6 : width >= 1180 ? 5 : width >= 760 ? 4 : 2; }
  function buildFlow() {
    const cols = columnsFor(innerWidth);
    if (flow.dataset.cols === String(cols)) return;
    flow.dataset.cols = String(cols);
    flow.style.setProperty("--cols", cols);
    flow.innerHTML = "";
    // a steady mix: every theme, every category, spread over the columns
    const themes = ["instrument", "paper", "instrument", "type"];
    const order = [...widgets].sort((a, b) => hash(a.folder) - hash(b.folder));
    const heights = new Array(cols).fill(0);
    const lists = Array.from({ length: cols }, () => []);
    order.forEach((w, i) => {
      const c = heights.indexOf(Math.min(...heights));
      lists[c].push({ w, theme: themes[i % themes.length], wp: i % 6 });
      heights[c] += w.h / w.w + 0.2;
    });
    lists.forEach((list, c) => {
      const col = document.createElement("div");
      col.className = "col";
      const track = document.createElement("div");
      track.className = "track";
      for (let copy = 0; copy < 2; copy++) {
        const group = document.createElement("div");
        group.className = "copy";
        for (const [k, item] of list.entries()) {
          const fig = document.createElement("figure");
          fig.className = `tile wp${item.wp}`;
          fig.dataset.folder = item.w.folder;
          fig.dataset.theme = item.theme;
          if (copy) fig.setAttribute("aria-hidden", "true");
          const img = document.createElement("img");
          img.width = item.w.w; img.height = item.w.h;
          img.decoding = "async";
          if (k > 2 || copy) img.loading = "lazy";
          img.src = pic(item.w.folder, item.theme);
          img.alt = copy ? "" : name(item.w.folder);
          const cap = document.createElement("figcaption");
          cap.textContent = name(item.w.folder);
          fig.append(img, cap);
          group.append(fig);
        }
        track.append(group);
      }
      // about the same speed in every column, a little faster or slower each
      const pace = [26, 21, 29, 23, 27, 20][c % 6];
      track.style.setProperty("--dur", `${Math.round((heights[c] * 260) / pace)}s`);
      track.style.setProperty("--delay", `${-((c * 7.3) % 20)}s`);
      col.append(track);
      flow.append(col);
    });
    const pause = document.createElement("button");
    pause.type = "button";
    pause.className = "flow-pause";
    pause.textContent = tr(flowPaused ? "flowPlay" : "flowPause");
    pause.addEventListener("click", () => { flowPaused = !flowPaused; flow.classList.toggle("paused", flowPaused); pause.textContent = tr(flowPaused ? "flowPlay" : "flowPause"); });
    flow.append(pause);
  }
  function hash(s) { let h = 7; for (const ch of s) h = (h * 31 + ch.charCodeAt(0)) >>> 0; return h % 997; }

  // ───────────────────────── ② every widget, by group ─────────────────────────
  const GROUPS = ["time", "weather", "system", "productivity", "info", "media"];
  const GROUP_KEY = { time: "catTime", weather: "catWeather", system: "catSystem", productivity: "catProductivity", info: "catInfo", media: "catMedia" };
  const GROUP_WALL = { time: 0, weather: 2, system: 5, productivity: 3, info: 1, media: 4 };
  // the same order as the README's list
  const ORDER = ["clock", "analogclock", "flipclock", "worldclock", "calendar", "countdown", "pomodoro", "timer", "progress",
    "weather", "forecast", "airquality", "sunmoon",
    "sysinfo", "usage", "netspeed", "disks", "battery", "network", "pcinfo", "recyclebin", "volume", "power",
    "notes", "todo", "launcher", "habits", "calculator", "search",
    "infocard", "rss", "currency", "dailyimage", "onthisday",
    "media", "photoframe", "whitenoise", "label"];
  const grid = $("#catGrid"), chips = $("#catChips");
  let catGroup = "all", catTheme = "instrument";
  const text = (o) => (o ? o[lang] || o.en : "");
  function buildCat() {
    const rank = (f) => { const k = ORDER.indexOf(f); return k < 0 ? 999 : k; };
    const list = [...widgets].sort((a, b) => GROUPS.indexOf(a.category) - GROUPS.indexOf(b.category) || rank(a.folder) - rank(b.folder));
    grid.innerHTML = "";
    for (const w of list) {
      const li = document.createElement("li");
      li.className = "w-item";
      li.dataset.folder = w.folder;
      li.dataset.group = w.category;
      const box = document.createElement("div");
      box.className = `w-pic wp${GROUP_WALL[w.category] ?? 5}`;
      const img = document.createElement("img");
      img.width = w.w; img.height = w.h;
      img.loading = "lazy"; img.decoding = "async"; img.alt = "";
      box.append(img);
      li.append(box, document.createElement("h3"), document.createElement("p"));
      grid.append(li);
    }
    chips.innerHTML = "";
    for (const g of ["all", ...GROUPS]) {
      const n = g === "all" ? widgets.length : widgets.filter((w) => w.category === g).length;
      if (!n) continue;
      const b = document.createElement("button");
      b.type = "button";
      b.dataset.group = g;
      b.setAttribute("aria-pressed", String(g === catGroup));
      b.innerHTML = '<span></span><i class="mono"></i>';
      $("i", b).textContent = n;
      b.addEventListener("click", () => pickGroup(g));
      chips.append(b);
    }
    catLang();
  }
  function catLang() {
    for (const b of $$("button", chips)) $("span", b).textContent = tr(b.dataset.group === "all" ? "catAll" : GROUP_KEY[b.dataset.group]);
    for (const li of $$(".w-item", grid)) {
      const w = widgets.find((x) => x.folder === li.dataset.folder);
      $("h3", li).textContent = text(w.name);
      $("p", li).textContent = text(w.description);
      $("img", li).src = pic(w.folder, catTheme);
    }
  }
  function pickGroup(g) {
    if (g === catGroup) return;
    catGroup = g;
    for (const b of $$("button", chips)) b.setAttribute("aria-pressed", String(b.dataset.group === g));
    const shown = [];
    for (const li of $$(".w-item", grid)) {
      const on = g === "all" || li.dataset.group === g;
      li.hidden = !on;
      if (on) shown.push(li);
    }
    grid.classList.toggle("short", g === "all" && grid.classList.contains("short"));
    $("#catMore").hidden = !(g === "all" && grid.classList.contains("short"));
    if (animate && !reduce) animate(shown, { opacity: [0, 1], y: [14, 0] }, { duration: 0.45, ease: [0.22, 1, 0.36, 1], delay: stagger ? stagger(0.025) : 0 });
  }
  // on a phone the full list starts folded after eight
  $("#catMore").addEventListener("click", () => {
    grid.classList.remove("short");
    $("#catMore").hidden = true;
    const rest = $$(".w-item", grid).slice(8);
    if (animate && !reduce) animate(rest, { opacity: [0, 1], y: [14, 0] }, { duration: 0.45, ease: [0.22, 1, 0.36, 1], delay: stagger ? stagger(0.03) : 0 });
  });
  for (const b of $$("#catThemes button")) b.addEventListener("click", () => {
    if (b.dataset.theme === catTheme) return;
    catTheme = b.dataset.theme;
    for (const x of $$("#catThemes button")) x.setAttribute("aria-pressed", String(x === b));
    const imgs = $$(".w-item:not([hidden]) img", grid);
    for (const li of $$(".w-item", grid)) $("img", li).src = pic(li.dataset.folder, catTheme);
    if (animate && !reduce) animate(imgs, { opacity: [0.2, 1], scale: [0.96, 1] }, { duration: 0.4, ease: [0.22, 1, 0.36, 1] });
  });

  // ───────────────────────── ③ deeper as you scroll ─────────────────────────
  const desk = $("#desk"), frame = $("#clock");
  let clock = null, mounting = null;
  async function mountClock() {
    if (!window.FWHost) return;
    const run = (mounting = (async () => {
      const c = await FWHost.mount(frame, { folder: "clock", locale: lang, theme: "instrument" });
      if (run === mounting) { clock = c; lastStage = ""; stage(progress); }
    })());
  }

  const CSS_LINES = [
    [".colon", "color: var(--w-accent);"],
    [".hm", "font-weight: 300;"],
    [".sec", "opacity: .35;"],
  ];
  const ACCENTS = ["#7de2c4", "#ffd166", "#ff8fa3", "#4cc9f0"];
  // widgets/clock/main.js, shortened (the same in every language)
  const JS = [
    "function render() {",
    "  const { h, m, s, wall } = partsNow();",
    "  $(\"hh\").textContent = pad(h);",
    "  $(\"mm\").textContent = pad(m);",
    "  $(\"sec\").textContent = opts.showSeconds ? pad(s) : \"\";",
    "  // …",
    "  const cells = $(\"ticks\").children;",
    "  for (let i = 0; i < 24; i++)",
    "    cells[i].className = i < h ? \"past\" : i === h ? \"now\" : \"\";",
    "}",
    "",
    "// Tick exactly on the second so the display never lags.",
    "function schedule() {",
    "  render();",
    "  setTimeout(schedule, 1000 - (Date.now() % 1000) + 5);",
    "}",
    "",
    "widget.settings.onChange((s) => { opts = s; render(); });",
    "widget.appearance.onChange(render);",
    "schedule();",
  ];
  const esc = (s) => s.replace(/&/g, "&amp;").replace(/</g, "&lt;");
  function highlight(line) {
    if (line.trim().startsWith("//")) return `<span class="c">${esc(line)}</span>`;
    return esc(line).replace(/(&quot;|"[^"]*")/g, '<span class="s">$1</span>').replace(/\b(const|function|for|let)\b/g, '<span class="k">$1</span>');
  }
  function codeLang() { $("#jsCode").innerHTML = JS.map(highlight).join("\n"); }

  const mix = (a, b, t) => { const p = (h) => [1, 3, 5].map((i) => parseInt(h.slice(i, i + 2), 16)); const x = p(a), y = p(b); return "#" + x.map((v, i) => Math.round(v + (y[i] - v) * t).toString(16).padStart(2, "0")).join(""); };
  let progress = 0, lastStage = "";
  function stage(p) {
    progress = p;
    const x = Math.min(3.999, Math.max(0, p * 4));
    const i = Math.floor(x), f = x - i;
    $$(".layer").forEach((el, k) => el.classList.toggle("on", k === i));
    const cap = $("#caption");
    if (cap.dataset.step !== String(i) || cap.dataset.lang !== lang) {
      cap.dataset.step = String(i); cap.dataset.lang = lang;
      cap.innerHTML = $$(".layer")[i].innerHTML;
      for (const a of $$("a", cap)) a.tabIndex = -1;
    }
    $$(".dots li").forEach((el, k) => el.classList.toggle("on", k <= i));
    $("#panelLook").classList.toggle("on", i === 1);
    $("#panelCss").classList.toggle("on", i === 2);
    $("#panelCode").classList.toggle("on", i === 3);
    desk.classList.toggle("coding", i === 3);
    desk.classList.toggle("tuning", i === 1 || i === 2);

    let theme = "instrument", look = "";
    if (i === 0) theme = f < 0.34 ? "instrument" : f < 0.67 ? "paper" : "type";
    if (i === 1) {
      const s = f * (ACCENTS.length - 1), k = Math.min(ACCENTS.length - 2, Math.floor(s));
      const accent = mix(ACCENTS[k], ACCENTS[k + 1], s - k);
      const radius = Math.round(f < 0.5 ? 22 - 16 * (f / 0.5) : 6 + 26 * ((f - 0.5) / 0.5));
      const opacity = Math.round((0.74 + 0.2 * f) * 100) / 100;
      look = `html:root{--w-accent:${accent};--w-radius:${radius}px;--w-bg:rgba(16,18,22,${opacity});}`;
      $("#lookSwatch").style.background = accent;
      $("#lookHex").textContent = accent.toUpperCase();
      $("#lookRadius").textContent = String(radius);
      $("#lookRadiusBar").style.width = `${(radius / 32) * 100}%`;
      $("#lookOpacity").textContent = `${Math.round(opacity * 100)}%`;
      $("#lookOpacityBar").style.width = `${opacity * 100}%`;
    }
    if (i === 2) {
      const all = CSS_LINES.map(([s, d]) => `${s} { ${d} }`);
      const total = all.join("").length, shown = Math.round(Math.min(1, f / 0.85) * total);
      let left = shown, html = "", done = [];
      for (const [k, line] of all.entries()) {
        const n = Math.min(line.length, Math.max(0, left));
        left -= line.length;
        if (n === line.length) done.push(k);
        const part = line.slice(0, n), cut = part.indexOf("{");
        html += (cut < 0 ? `<span class="k">${esc(part)}</span>` : `<span class="k">${esc(part.slice(0, cut))}</span>${esc(part.slice(cut))}`) + (n < line.length && n > 0 ? '<span class="caret"></span>' : "") + "\n";
        if (n < line.length) break;
      }
      $("#cssCode").innerHTML = html;
      look = done.map((k) => `${CSS_LINES[k][0]}{${CSS_LINES[k][1]}}`).join("");
    }
    desk.dataset.wall = theme;
    const key = theme + "|" + look;
    if (clock && key !== lastStage) { lastStage = key; clock.setTheme(theme); clock.setLook(look); }
  }

  // ───────────────────────── entrances ─────────────────────────
  function entrances() {
    if (!inView || reduce) return;
    const groups = [
      [".easy .kicker", ".easy .h2"], [".scene"], [".scene-steps li"],
      [".cat .kicker", ".cat .h2", ".cat .sec-sub", ".cat-bar"], [".deep .kicker", ".deep .h2"],
      [".details-sec .kicker", ".details-sec .h2"], [".details li"],
      [".open .kicker", ".open .h2"], [".lead", ".side .entry"], [".get-main", ".get-help"],
    ];
    for (const sel of groups) {
      const els = sel.flatMap((s) => $$(s));
      els.forEach((el, k) => { el.classList.add("rise"); el.style.setProperty("--d", `${k * 0.07}s`); });
      for (const el of els) inView(el, () => { el.classList.add("in"); }, { amount: 0.2 });
    }
    // the meters fill once they're seen
    const meters = $(".meters");
    for (const m of $$(".meter i", meters)) m.style.setProperty("--grow", "0");
    inView(meters, () => { $$(".meter i", meters).forEach((m, k) => setTimeout(() => m.style.setProperty("--grow", "1"), k * 120)); }, { amount: 0.5 });
    // the hero: title first, then the rest
    if (animate) {
      animate(".hero-title", { opacity: [0, 1], y: [18, 0] }, { duration: 0.7, ease: [0.22, 1, 0.36, 1] });
      animate(".hero-row", { opacity: [0, 1], y: [14, 0] }, { duration: 0.6, delay: 0.12, ease: [0.22, 1, 0.36, 1] });
      animate(".flow", { opacity: [0, 1] }, { duration: 0.9, delay: 0.25 });
    }
  }

  // ───────────────────────── downloads on a phone ─────────────────────────
  const phone = matchMedia("(max-width: 860px) and (pointer: coarse)");
  let toastTimer = 0;
  function toast(text) {
    let t = $(".toast");
    if (!t) { t = document.createElement("div"); t.className = "toast"; t.setAttribute("role", "status"); document.body.append(t); }
    t.textContent = text;
    t.classList.add("on");
    clearTimeout(toastTimer);
    toastTimer = setTimeout(() => t.classList.remove("on"), 3600);
  }
  for (const a of $$("[data-download]")) a.addEventListener("click", (e) => {
    if (!phone.matches || !navigator.clipboard) return;
    e.preventDefault();
    navigator.clipboard.writeText(a.href).then(() => toast(tr("copied")), () => { location.href = a.href; });
  });

  // ───────────────────────── start ─────────────────────────
  applyLang();
  fetch("widgets.json").then((r) => r.json()).then((list) => {
    widgets = list;
    buildFlow();
    buildCat();
    applyLang();
    let t = 0;
    addEventListener("resize", () => { clearTimeout(t); t = setTimeout(buildFlow, 150); });
  });
  codeLang();
  mountClock();
  // on a phone the stage sits pinned at the top, so each step is read lower down, below it
  const offset = matchMedia("(max-width: 860px)").matches ? ["start end", "end end"] : ["start center", "end center"];
  if (scroll) scroll((p) => stage(p), { target: $("#layers"), offset });
  else stage(0);
  entrances();
})();
