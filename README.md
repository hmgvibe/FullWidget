# FullWidget

Windows 桌面小組件，自帶 38 個：時鐘（數字、指針、翻頁、世界時鐘）、日曆、倒數日、番茄鐘、計時器、時間進度、天氣、一週天氣、空氣品質、日出日落、系統監控、使用曲線、網速、磁碟、電池、網路狀態、電腦資訊、資源回收筒、音量、電源、便利貼、待辦清單、快捷啟動、習慣打卡、計算機、搜尋框、自訂資訊卡、新聞訂閱、匯率、每日圖片、歷史上的今天、媒體控制、相框、白噪音、文字標籤。可以換主題、調外觀，也可以用 HTML/CSS/JS 自己寫組件。
**官網：<https://hmgvibe.github.io/FullWidget/>**　·　[下載](https://github.com/hmgvibe/FullWidget/releases/latest)

Desktop widgets for Windows — [English below](#english).

## 開始使用

1. 解壓到任何資料夾（例如 `D:\FullWidget`），雙擊 `FullWidget.exe`。
2. 桌面上會出現時鐘、天氣和系統監控。拖動就能移動，右鍵有選單。
3. 左鍵點系統托盤裏的 FullWidget 圖標（三張彩色卡片拼成的 F），打開**控制中心**：添加組件、改外觀、換主題、設定開機自動啟動。

**需要：** Windows 11（Windows 10 沒有測試過），以及 Microsoft Edge WebView2 Runtime（Windows 11 已內建）。
小的那個 zip 還需要 [.NET 9 桌面執行階段](https://dotnet.microsoft.com/download/dotnet/9.0)；沒有的話第一次打開時 Windows 會提示下載。檔名有 `full` 的那個不需要。

**資料放在哪：** 設定和組件保存的內容在 `%AppData%\FullWidget`。想把資料和程式放在一起（例如放在隨身碟）：在 `FullWidget.exe` 旁邊建一個名為 `portable` 的空檔案，資料就會存在旁邊的 `data` 資料夾。

**自己寫組件：** 控制中心 →「添加組件」→「新建組件」，再按「開發文檔」看說明。

**移除：** 先退出 FullWidget，在命令列執行 `FullWidget.exe --cleanup`（從開機啟動和 .fwz 檔案類型中移除），再刪掉程式資料夾；資料在 `%AppData%\FullWidget` 和 `%LocalAppData%\FullWidget`。


## 授權

- FullWidget 以 [GNU GPL 3.0](LICENSE) 授權：可以自由使用、修改和再發佈，但發佈修改過的版本時，也必須以 GPL 3.0 公開原始碼。
- `sdk/` 和 `templates/` 以 [MIT](sdk/LICENSE) 授權：自己寫的組件可以用任何授權，包括不開源。
- **名字和 logo**：「FullWidget」這個名字和 logo 不在上面的授權範圍內。修改過的版本請改用別的名字和圖標，不要讓人以為是官方版本。

## Code signing policy（程式簽章政策）

Windows 版的 `FullWidget.exe` 和 `FullWidget.dll` 會簽章：

- 免費簽章服務由 [SignPath.io](https://about.signpath.io/) 提供，證書由 [SignPath Foundation](https://signpath.org/) 提供。
- 只簽由這個儲存庫的原始碼、經 GitHub Actions 建置出來的檔案；每次發佈都要由核准者手動批准。
- 成員和角色：
  - 提交與審核（Committers and reviewers）：[hmgvibe](https://github.com/hmgvibe)
  - 核准（Approvers）：[hmgvibe](https://github.com/hmgvibe)

## 隱私

除非你自己要求，或是你放上桌面的組件需要，FullWidget 不會把任何資料傳到網路上。沒有統計、沒有追蹤、沒有帳號。會連網的只有：

- **天氣、一週天氣、空氣品質、日出日落**：[Open-Meteo](https://open-meteo.com/)（天氣和地名）；沒設定城市時，用 [GeoJS](https://www.geojs.io/) 從 IP 位址估計所在城市。第一次啟動時桌面上就有天氣組件，所以會連一次。
- **匯率**：[ExchangeRate-API](https://www.exchangerate-api.com/)。
- **每日圖片**：Bing。**歷史上的今天**：維基百科。**新聞訂閱**：你填的 RSS 網址。
- **組件市場**：打開控制中心「添加組件 → 線上」時，讀取 `hmgvibe.github.io/fullwidget-market` 的清單。
- **第三方組件**：只能連到它安裝時列出、你同意過的網站。

這些服務只會收到查詢需要的內容（例如城市名稱），以及連線本身帶的 IP 位址。

---

## English

Desktop widgets for Windows, 38 built in: clocks (digital, analog, flip, world), calendar, countdown, Pomodoro, timer, time progress, weather, 7-day forecast, air quality, sun & moon, system monitor, usage graph, network speed, drives, battery, network status, PC info, Recycle Bin, volume, power, notes, to-do list, launcher, habits, calculator, search box, custom info card, news feed, exchange rates, picture of the day, on this day, now playing, photo frame, ambient sounds and text label. Change themes and looks, or write your own widgets in HTML/CSS/JS.

**Website: <https://hmgvibe.github.io/FullWidget/?lang=en>** · [Download](https://github.com/hmgvibe/FullWidget/releases/latest)

1. Unzip anywhere (e.g. `D:\FullWidget`) and run `FullWidget.exe`.
2. A clock, the weather and a system monitor appear on the desktop. Drag to move them; right-click for a menu.
3. Click the FullWidget icon (an F made of colored cards) in the system tray to open the **Control Center**: add widgets, change their look, switch themes, start with Windows. The language follows Windows (English, 繁體中文 or 简体中文) and can be changed under Settings.

**Needs:** Windows 11 (Windows 10 is untested) and the Microsoft Edge WebView2 Runtime (built into Windows 11).
The smaller zip also needs the [.NET 9 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/9.0); Windows offers to download it on first start. The `full` zip doesn't.

**Where data goes:** settings and what widgets save live in `%AppData%\FullWidget`. To keep data next to the app (on a USB stick, say), create an empty file named `portable` next to `FullWidget.exe`; data then goes into the `data` folder beside it.

**Make your own widget:** Control Center → Add widgets → New widget, then open the Developer guide.

**Removing it:** quit FullWidget, run `FullWidget.exe --cleanup` from a command prompt (takes it out of startup and the .fwz file type), then delete the app folder. Data is in `%AppData%\FullWidget` and `%LocalAppData%\FullWidget`.

**License:** FullWidget is licensed under the [GNU GPL 3.0](LICENSE): use, change and share it freely, but modified versions you distribute must also be GPL 3.0 with their source. `sdk/` and `templates/` are [MIT](sdk/LICENSE), so your own widgets can use any license, closed ones included. The name "FullWidget" and its logo are not covered by these licenses: please give modified versions another name and icon.

### Code signing policy

`FullWidget.exe` and `FullWidget.dll` are signed. Free code signing is provided by [SignPath.io](https://about.signpath.io/), certificate by [SignPath Foundation](https://signpath.org/). Only files built from this repository by GitHub Actions are signed, and every release is approved by hand.

- Committers and reviewers: [hmgvibe](https://github.com/hmgvibe)
- Approvers: [hmgvibe](https://github.com/hmgvibe)

### Privacy

This program will not transfer any information to other networked systems unless specifically requested by the user or needed by a widget the user put on the desktop. There is no telemetry, tracking or account. Only these connect to the internet:

- **Weather, forecast, air quality, sun & moon:** [Open-Meteo](https://open-meteo.com/) (weather and place names); without a city set, [GeoJS](https://www.geojs.io/) guesses the city from your IP address. The weather widget is on the desktop at first start, so this happens once right away.
- **Exchange rates:** [ExchangeRate-API](https://www.exchangerate-api.com/). **Picture of the day:** Bing. **On this day:** Wikipedia. **News feed:** the RSS address you enter.
- **Widget market:** the list at `hmgvibe.github.io/fullwidget-market`, when you open Add widgets → Online.
- **Third-party widgets:** only the sites listed when they were installed, which you agreed to.

These services receive only what the request needs (such as a city name) and the IP address any connection carries.
