/* Every word in the promo, in three languages: index.html?lang=zh-CN (or en); Traditional Chinese
   by default. Widget, theme and Control Center names match the app and the website. */
(function () {
  const ALL = {
    "zh-TW": {
      words: ["桌面", "也可以", "很好看"],
      count: { unit: "個組件", sub: "打開就有，全部免費。" },
      cats: [
        { name: "時間", n: 9, wp: 0, list: ["時鐘", "指針時鐘", "翻頁時鐘", "世界時鐘", "日曆", "倒數日", "番茄鐘", "計時器", "時間進度"] },
        { name: "天氣", n: 4, wp: 2, list: ["天氣", "一週天氣", "空氣品質", "日出日落"] },
        { name: "系統", n: 10, wp: 5, list: ["系統監控", "使用曲線", "網速", "磁碟", "電池", "網路狀態", "電腦資訊", "資源回收筒", "音量", "電源"] },
        { name: "效率", n: 6, wp: 3, list: ["待辦清單", "計算機", "便利貼", "習慣打卡", "快捷啟動", "搜尋框"] },
        { name: "資訊", n: 5, wp: 4, list: ["新聞訂閱", "匯率", "歷史上的今天", "每日圖片", "自訂資訊卡"] },
        { name: "媒體", n: 4, wp: 1, list: ["媒體控制", "白噪音", "相框", "文字標籤"] },
      ],
      catUnit: "個",
      themes: { title: "三套主題，一鍵換裝。", names: ["量被畫出來", "書桌上的紙", "玻璃上的字"] },
      look: { title: "顏色、圓角、透明度，隨你調。", panel: "外觀", rows: ["強調色", "圓角", "不透明度"] },
      grow: { title: "拉大，內容跟著放大。" },
      snap: { title: "拖過去，自動對齊。" },
      layers: { title: "放桌面，或放最上層。", tags: ["桌面層", "置頂", "滑鼠穿透"], doc: "週報.docx" },
      full: { title: "全螢幕時，自動讓開。", sub: "玩遊戲、看影片時，組件隱藏並停止更新。", chip: "組件已隱藏 · 暫停更新" },
      code: { title: "會寫網頁，就會寫組件。", sub: "HTML、CSS、JS，改了馬上看到。" },
      pack: {
        title: "打包成 .fwz，雙擊就能裝。", file: "CPU 小卡.fwz",
        sheet: { title: "安裝「CPU 小卡」", by: "你 · v1.0.0", needs: "它要：", items: ["讀取系統資訊（CPU、記憶體、網路）"], no: "不會連網，也讀不到你電腦上的檔案。", cancel: "取消", ok: "安裝並放到桌面" },
      },
      market: {
        title: "組件市場，一鍵安裝。",
        cc: { title: "FullWidget 控制中心", nav: ["我的組件", "添加組件", "主題", "設定"], heading: "添加組件", tabs: ["已安裝", "線上"], install: "安裝", added: "已安裝" },
        cards: [
          { folder: "hmgvibe.eyebreak", name: "護眼提醒", by: "hmgvibe · v1.0.0", note: "需要：通知" },
          { folder: "hmgvibe.proverb", name: "每日一句", by: "hmgvibe · v1.0.0", note: "不需要權限" },
        ],
        mine: { name: "CPU 小卡", by: "你 · v1.0.0", status: "審核中" },
      },
      light: { value: "0.4%", label: "38 個組件全開，CPU 約 0.4%", sub: "記憶體約 355 MB · 實測於 Windows 11" },
      open: { lines: ["免費。", "開源。", "什麼都不收集。"], zeros: ["帳號", "廣告", "統計"] },
      end: { tagline: "一個不一樣的 Windows 桌面小組件", cta: "免費下載" },
      // what the live widgets show
      todo: ["回覆客戶郵件", "整理會議記錄", "預約牙醫", "讀完第三章"],
      media: { title: "晴天的海邊", artist: "林間小路", album: "夏日記事" },
      device: "揚聲器",
    },
    "zh-CN": {
      words: ["桌面", "也可以", "很好看"],
      count: { unit: "个组件", sub: "打开就有，全部免费。" },
      cats: [
        { name: "时间", n: 9, wp: 0, list: ["时钟", "指针时钟", "翻页时钟", "世界时钟", "日历", "倒数日", "番茄钟", "计时器", "时间进度"] },
        { name: "天气", n: 4, wp: 2, list: ["天气", "一周天气", "空气质量", "日出日落"] },
        { name: "系统", n: 10, wp: 5, list: ["系统监控", "使用曲线", "网速", "磁盘", "电池", "网络状态", "电脑信息", "回收站", "音量", "电源"] },
        { name: "效率", n: 6, wp: 3, list: ["待办清单", "计算器", "便利贴", "习惯打卡", "快捷启动", "搜索框"] },
        { name: "资讯", n: 5, wp: 4, list: ["新闻订阅", "汇率", "历史上的今天", "每日图片", "自定义信息卡"] },
        { name: "媒体", n: 4, wp: 1, list: ["媒体控制", "白噪音", "相框", "文字标签"] },
      ],
      catUnit: "个",
      themes: { title: "三套主题，一键换装。", names: ["量被画出来", "书桌上的纸", "玻璃上的字"] },
      look: { title: "颜色、圆角、透明度，随你调。", panel: "外观", rows: ["强调色", "圆角", "不透明度"] },
      grow: { title: "拉大，内容跟着放大。" },
      snap: { title: "拖过去，自动对齐。" },
      layers: { title: "放桌面，或放最上层。", tags: ["桌面层", "置顶", "鼠标穿透"], doc: "周报.docx" },
      full: { title: "全屏时，自动让开。", sub: "玩游戏、看视频时，组件隐藏并停止更新。", chip: "组件已隐藏 · 暂停更新" },
      code: { title: "会写网页，就会写组件。", sub: "HTML、CSS、JS，改了马上看到。" },
      pack: {
        title: "打包成 .fwz，双击就能装。", file: "CPU 小卡.fwz",
        sheet: { title: "安装「CPU 小卡」", by: "你 · v1.0.0", needs: "它要：", items: ["读取系统信息（CPU、内存、网络）"], no: "不会联网，也读不到你电脑上的文件。", cancel: "取消", ok: "安装并放到桌面" },
      },
      market: {
        title: "组件市场，一键安装。",
        cc: { title: "FullWidget 控制中心", nav: ["我的组件", "添加组件", "主题", "设置"], heading: "添加组件", tabs: ["已安装", "在线"], install: "安装", added: "已安装" },
        cards: [
          { folder: "hmgvibe.eyebreak", name: "护眼提醒", by: "hmgvibe · v1.0.0", note: "需要：通知" },
          { folder: "hmgvibe.proverb", name: "每日一句", by: "hmgvibe · v1.0.0", note: "不需要权限" },
        ],
        mine: { name: "CPU 小卡", by: "你 · v1.0.0", status: "审核中" },
      },
      light: { value: "0.4%", label: "38 个组件全开，CPU 约 0.4%", sub: "内存约 355 MB · 实测于 Windows 11" },
      open: { lines: ["免费。", "开源。", "什么都不收集。"], zeros: ["账号", "广告", "统计"] },
      end: { tagline: "一个不一样的 Windows 桌面小组件", cta: "免费下载" },
      todo: ["回复客户邮件", "整理会议记录", "预约牙医", "读完第三章"],
      media: { title: "晴天的海边", artist: "林间小路", album: "夏日记事" },
      device: "扬声器",
    },
    en: {
      words: ["Your desktop", "can look", "this good"],
      count: { unit: "widgets", sub: "Built in, and all free." },
      cats: [
        { name: "Time", n: 9, wp: 0, list: ["Clock", "Analog clock", "Flip clock", "World clock", "Calendar", "Countdown", "Pomodoro", "Timer", "Time progress"] },
        { name: "Weather", n: 4, wp: 2, list: ["Weather", "7-day forecast", "Air quality", "Sun & moon"] },
        { name: "System", n: 10, wp: 5, list: ["System monitor", "Usage graph", "Network speed", "Drives", "Battery", "Network status", "This PC", "Recycle Bin", "Volume", "Power"] },
        { name: "Tools", n: 6, wp: 3, list: ["To-do list", "Calculator", "Sticky note", "Habits", "Launcher", "Search box"] },
        { name: "Info", n: 5, wp: 4, list: ["News feed", "Exchange rates", "On this day", "Picture of the day", "Custom card"] },
        { name: "Media", n: 4, wp: 1, list: ["Now playing", "Ambient sounds", "Photo frame", "Text label"] },
      ],
      catUnit: "widgets",
      themes: { title: "Three themes, one click.", names: ["Instrument", "Paper", "Glass"] },
      look: { title: "Color, corners, opacity. Your call.", panel: "Look", rows: ["Accent color", "Corner radius", "Opacity"] },
      grow: { title: "Make it bigger. It all scales." },
      snap: { title: "Drag it close. It snaps." },
      layers: { title: "On the desktop, or on top.", tags: ["Desktop", "Top", "Click-through"], doc: "Weekly report.docx" },
      full: { title: "Full screen? It steps aside.", sub: "Games and videos get the whole screen.", chip: "Widgets hidden · updates paused" },
      code: { title: "Know HTML? Write a widget.", sub: "HTML, CSS and JS. See every change at once." },
      pack: {
        title: "Share it as one .fwz file.", file: "CPU card.fwz",
        sheet: { title: "Install “CPU card”", by: "You · v1.0.0", needs: "It wants to:", items: ["Read system info (CPU, memory, network)"], no: "It can't go online or read files on your PC.", cancel: "Cancel", ok: "Install and add" },
      },
      market: {
        title: "More in the widget market.",
        cc: { title: "FullWidget Control Center", nav: ["My widgets", "Add widgets", "Themes", "Settings"], heading: "Add widgets", tabs: ["Installed", "Online"], install: "Install", added: "Installed" },
        cards: [
          { folder: "hmgvibe.eyebreak", name: "Eye break", by: "hmgvibe · v1.0.0", note: "Needs: notifications" },
          { folder: "hmgvibe.proverb", name: "Saying of the day", by: "hmgvibe · v1.0.0", note: "No permissions" },
        ],
        mine: { name: "CPU card", by: "You · v1.0.0", status: "In review" },
      },
      light: { value: "0.4%", label: "38 widgets, about 0.4% CPU", sub: "About 355 MB of memory · measured on Windows 11" },
      open: { lines: ["Free.", "Open source.", "Collects nothing."], zeros: ["accounts", "ads", "tracking"] },
      end: { tagline: "A different kind of Windows desktop widget", cta: "Download free" },
      todo: ["Reply to the client", "Tidy meeting notes", "Book the dentist", "Finish chapter 3"],
      media: { title: "Sunny Day by the Sea", artist: "Forest Path", album: "Summer Notes" },
      device: "Speakers",
    },
  };
  const asked = new URLSearchParams(location.search).get("lang");
  const lang = ALL[asked] ? asked : "zh-TW";
  document.documentElement.lang = lang === "zh-TW" ? "zh-Hant" : lang;
  // the same in every language
  const SHARED = {
    wordmark: "FullWidget",
    langs: { names: ["繁體中文", "简体中文", "English"], titles: ["三種語言", "三种语言", "Three languages"] },
    gpl: "GPL-3.0", repo: "github.com/hmgvibe/FullWidget", url: "hmgvibe.github.io/FullWidget", req: "Windows 11",
  };
  const T = { lang, ...SHARED, ...ALL[lang] };
  T.open = { ...T.open, gpl: SHARED.gpl, repo: SHARED.repo };
  T.end = { ...T.end, url: SHARED.url, req: SHARED.req };
  window.COPY = T;
})();
