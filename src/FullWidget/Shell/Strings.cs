namespace FullWidget.Shell;

/// UI text in Traditional Chinese, Simplified Chinese and English, side by side.
/// The language follows the setting ("auto" = Windows' display language); other languages get English.
static class Strings
{
    /// "zh-TW", "zh-CN" or "en".
    public static string Lang { get; private set; } = "zh-TW";

    /// Takes a culture name such as "zh-HK", "zh-Hans-CN" or "de-DE".
    public static void SetLanguage(string culture)
    {
        var c = culture.ToLowerInvariant();
        Lang = !c.StartsWith("zh") ? "en"
            : c is "zh-cn" or "zh-sg" or "zh-hans" || c.StartsWith("zh-hans") ? "zh-CN"
            : "zh-TW";
    }

    static string T(string tw, string cn, string en) => Lang switch { "zh-CN" => cn, "en" => en, _ => tw };

    public const string AppName = "FullWidget";

    // ── tray & menus ──
    public static string AddWidget => T("添加組件", "添加组件", "Add widget");
    public static string NewWidgetEllipsis => T("新建組件…", "新建组件…", "New widget…");
    public static string MyWidgets => T("我的組件", "我的组件", "My widgets");
    public static string CategoryTime => T("時間", "时间", "Time");
    public static string CategoryWeather => T("天氣", "天气", "Weather");
    public static string CategorySystem => T("系統", "系统", "System");
    public static string CategoryProductivity => T("效率", "效率", "Productivity");
    public static string CategoryInfo => T("資訊", "资讯", "Info");
    public static string CategoryMedia => T("媒體與裝飾", "媒体与装饰", "Media & decor");
    public static string CategoryOther => T("其他", "其他", "Other");
    public static string LockAll => T("鎖定全部組件", "锁定全部组件", "Lock all widgets");
    public static string HideAll => T("隱藏全部組件", "隐藏全部组件", "Hide all widgets");
    public static string Exit => T("退出", "退出", "Quit");
    public static string ControlCenter => T("控制中心", "控制中心", "Control Center");
    public static string OpenControlCenter => T("打開控制中心", "打开控制中心", "Open Control Center");
    public static string WidgetSettings => T("設定…", "设置…", "Settings…");
    public static string SettingsEllipsis => T("設定…", "设置…", "Settings…");
    public static string ArrangeMode => T("排列模式", "排列模式", "Arrange mode");
    public static string Theme => T("主題", "主题", "Theme");
    public static string FollowGlobalShort => T("跟隨全局", "跟随全局", "Global theme");
    public static string Card => T("卡片", "卡片", "Card");
    public static string Locate => T("定位", "定位", "Locate");
    public static string RemoveConfirmShort => T("再按一次移除", "再按一次移除", "Click again to remove");
    public static string Lock => T("鎖定位置", "锁定位置", "Lock position");
    public static string Layer => T("層級", "层级", "Layer");
    public static string StyleTransparent => T("透明", "透明", "Clear");
    public static string StyleFrosted => T("毛玻璃", "毛玻璃", "Frosted");
    public static string Shadow => T("陰影", "阴影", "Shadow");
    public static string ClickThrough => T("滑鼠穿透", "鼠标穿透", "Click-through");
    public static string Reload => T("重新載入", "重新加载", "Reload");
    public static string DevTools => T("開發者工具", "开发者工具", "Developer tools");
    public static string Remove => T("移除", "移除", "Remove");
    public static string LayerTopShort => T("置頂", "置顶", "Top");
    public static string LayerNormalShort => T("普通", "普通", "Normal");
    public static string LayerDesktopShort => T("桌面", "桌面", "Desktop");
    public static string LayerDesktop => T("桌面層", "桌面层", "Desktop");
    public static string AlreadyHave(int n) => string.Format(T("已有 {0}", "已有 {0}", "{0} placed"), n);
    public static string TrayHeader(string version, int n) =>
        string.Format(T("v{0} · 桌面上有 {1} 個組件", "v{0} · 桌面上有 {1} 个组件", "v{0} · {1} widgets on the desktop"), version, n);

    // ── notices ──
    public static string ClickThroughTip => T(
        "已開啟滑鼠穿透：點擊會穿過「{0}」。\n要關閉，請在托盤圖標 → 我的組件 中取消。",
        "已开启鼠标穿透：点击会穿过“{0}”。\n要关闭，请在托盘图标 → 我的组件 中取消。",
        "Click-through is on: clicks pass through \"{0}\".\nTo turn it off, use the tray icon → My widgets.");
    public static string AlreadyRunning => T("FullWidget 已在運行，可在系統托盤找到它。", "FullWidget 已在运行，可在系统托盘找到它。", "FullWidget is already running. You'll find it in the system tray.");
    public static string FirstRun => T("FullWidget 在這裏。左鍵點圖標打開控制中心，右鍵打開選單。", "FullWidget 在这里。左键点图标打开控制中心，右键打开菜单。", "FullWidget lives here. Click the icon for the Control Center, right-click for the menu.");
    public static string RestartNeeded => T("此設定需要重新啟動 FullWidget 才會生效。現在重新啟動嗎？", "此设置需要重新启动 FullWidget 才会生效。现在重新启动吗？", "FullWidget needs to restart for this setting. Restart now?");
    public static string NoWebView2 => T(
        "找不到 Microsoft Edge WebView2 Runtime。\n請先安裝：https://go.microsoft.com/fwlink/p/?LinkId=2124703",
        "找不到 Microsoft Edge WebView2 Runtime。\n请先安装：https://go.microsoft.com/fwlink/p/?LinkId=2124703",
        "Microsoft Edge WebView2 Runtime was not found.\nInstall it first: https://go.microsoft.com/fwlink/p/?LinkId=2124703");
    public static string StartFailed => T("FullWidget 啟動失敗：{0}", "FullWidget 启动失败：{0}", "FullWidget could not start: {0}");
    public static string WidgetMissing => T("組件「{0}」已不存在，對應的實例暫時不會顯示。", "组件“{0}”已不存在，对应的实例暂时不会显示。", "Widget \"{0}\" is missing; its copies won't show for now.");

    // ── windows & dialogs ──
    public static string DocsTitle => T("開發文檔", "开发文档", "Developer guide");
    public static string GuideMissing => T("找不到開發文檔", "找不到开发文档", "The developer guide is missing");
    public static string FilterPrograms => T("程式和捷徑|*.exe;*.lnk;*.url;*.appref-ms;*.bat;*.cmd|所有檔案|*.*", "程序和快捷方式|*.exe;*.lnk;*.url;*.appref-ms;*.bat;*.cmd|所有文件|*.*", "Programs and shortcuts|*.exe;*.lnk;*.url;*.appref-ms;*.bat;*.cmd|All files|*.*");
    public static string FilterImages => T("圖片|*.jpg;*.jpeg;*.png;*.gif;*.webp;*.bmp|所有檔案|*.*", "图片|*.jpg;*.jpeg;*.png;*.gif;*.webp;*.bmp|所有文件|*.*", "Pictures|*.jpg;*.jpeg;*.png;*.gif;*.webp;*.bmp|All files|*.*");
    public static string FilterAll => T("所有檔案|*.*", "所有文件|*.*", "All files|*.*");
    public static string PickPackageTitle => T("選擇要安裝的組件", "选择要安装的组件", "Choose a widget to install");
    public static string PackageFilter => T("FullWidget 組件 (*.fwz)|*.fwz;*.dwz|Zip 壓縮檔 (*.zip)|*.zip", "FullWidget 组件 (*.fwz)|*.fwz;*.dwz|Zip 压缩文件 (*.zip)|*.zip", "FullWidget widget (*.fwz)|*.fwz;*.dwz|Zip archive (*.zip)|*.zip");
    public static string ExportTitle => T("匯出組件", "导出组件", "Export widget");
    public static string ExportFilter => T("FullWidget 組件 (*.fwz)|*.fwz", "FullWidget 组件 (*.fwz)|*.fwz", "FullWidget widget (*.fwz)|*.fwz");
    public static string FwzTypeName => T("FullWidget 組件", "FullWidget 组件", "FullWidget widget");
    public static string Allow => T("允許", "允许", "Allow");

    // ── errors shown in the control center ──
    public static string NotInstalled => T("這個組件沒有安裝", "这个组件没有安装", "This widget isn't installed");
    public static string NeedsPermission => T("這個組件需要先允許權限", "这个组件需要先允许权限", "This widget's permissions must be allowed first");
    public static string BuiltinCantExport => T("內建組件不能匯出", "内置组件不能导出", "Built-in widgets can't be exported");
    public static string BuiltinCantUninstall => T("內建組件不能解除安裝", "内置组件不能卸载", "Built-in widgets can't be uninstalled");
    public static string ExportFailed => T("匯出失敗：{0}", "导出失败：{0}", "Export failed: {0}");
    public static string CreateFailed => T("建立失敗：{0}", "创建失败：{0}", "Could not create it: {0}");
    public static string InstallFailed => T("安裝失敗：{0}", "安装失败：{0}", "Install failed: {0}");
    public static string FromMarket => T("來自組件市場", "来自组件市场", "From the widget market");
    public static string MarketUnreachable => T("連不上組件市場，請檢查網路後再試", "连不上组件市场，请检查网络后再试", "Can't reach the widget market. Check the network and try again");
    public static string MarketNoSuchWidget => T("組件市場裏沒有這個組件了，請重新整理", "组件市场里没有这个组件了，请刷新", "That widget is no longer in the market. Refresh the list");
    public static string MarketBadPackage => T("下載的組件和市場清單對不上，為了安全沒有安裝", "下载的组件和市场列表对不上，为了安全没有安装", "The downloaded widget doesn't match the market's list, so it wasn't installed");
    public static string AlreadyHandled => T("這個安裝已經處理過了", "这个安装已经处理过了", "This install was already handled");
    public static string NotLoadedSeeLog => T("組件沒有載入，請查看日誌", "组件没有加载，请查看日志", "The widget didn't load; see the log");

    // ── widget folders that don't load (catalog) ──
    public static string ReservedId => T("id「{0}」以 {1} 開頭，這是內建組件專用的", "id“{0}”以 {1} 开头，这是内置组件专用的", "id \"{0}\" starts with {1}, which is reserved for built-in widgets");
    public static string DuplicateId => T("id「{0}」和「{1}」重複了，每個組件的 id 要不一樣", "id“{0}”和“{1}”重复了，每个组件的 id 要不一样", "id \"{0}\" is also used by \"{1}\"; every widget needs its own id");
    public static string JsonError => T("widget.json {0}格式有誤{1}：{2}", "widget.json {0}格式有误{1}：{2}", "widget.json {0}is not valid JSON{1}: {2}");
    public static string LineN => T("第 {0} 行", "第 {0} 行", "line {0} ");
    public static string HintComma => T("（多半是上一行結尾少了逗號）", "（多半是上一行结尾少了逗号）", " (probably a missing comma at the end of the line above)");
    public static string HintColon => T("（名稱後面要有冒號）", "（名称后面要有冒号）", " (a name needs a colon after it)");
    public static string HintQuotes => T("（文字要用英文雙引號包起來）", "（文字要用英文双引号包起来）", " (text needs straight double quotes)");
    public static string HintType => T("（這一項的類型不對，例如該用 [ ] 的地方用了文字）", "（这一项的类型不对，例如该用 [ ] 的地方用了文字）", " (wrong kind of value, e.g. text where a [ ] list belongs)");
    public static string HintBrackets => T("（大括號或中括號沒有成對）", "（大括号或中括号没有成对）", " (unbalanced braces or brackets)");
    public static string CantRead => T("讀不到 widget.json：{0}", "读不到 widget.json：{0}", "Can't read widget.json: {0}");
    public static string EmptyManifest => T("widget.json 是空的", "widget.json 是空的", "widget.json is empty");
    public static string MissingId => T("widget.json 缺少 id", "widget.json 缺少 id", "widget.json has no id");
    public static string BadId => T("id「{0}」不符合規則：只能用小寫英文字母、數字和 . _ -，長 2–64 個字元", "id“{0}”不符合规则：只能用小写英文字母、数字和 . _ -，长 2–64 个字符", "id \"{0}\" isn't valid: use lowercase letters, digits and . _ -, 2–64 characters");
    public static string NoEntry => T("找不到入口檔案「{0}」（widget.json 的 entry）", "找不到入口文件“{0}”（widget.json 的 entry）", "Entry file \"{0}\" not found (\"entry\" in widget.json)");
    public static string SettingNoKey => T("settings 裏有一項沒有 key", "settings 里有一项没有 key", "A setting has no key");
    public static string SettingDupKey => T("settings 裏的 key「{0}」重複了", "settings 里的 key“{0}”重复了", "Setting key \"{0}\" appears twice");

    // ── packages ──
    public static string FileNotFound => T("找不到這個檔案", "找不到这个文件", "File not found");
    public static string PackageTooLarge => T("檔案太大（上限 64 MB）", "文件太大（上限 64 MB）", "The file is too large (64 MB limit)");
    public static string NotAWidget => T("裏面沒有 widget.json，不是 FullWidget 組件", "里面没有 widget.json，不是 FullWidget 组件", "There's no widget.json inside; it isn't a FullWidget widget");
    public static string ReservedInstall => T("id「{0}」是內建組件專用的，不能安裝", "id“{0}”是内置组件专用的，不能安装", "id \"{0}\" is reserved for built-in widgets");
    public static string Corrupt => T("檔案已損壞，或不是 zip 格式", "文件已损坏，或不是 zip 格式", "The file is damaged or isn't a zip file");
    public static string TooManyFiles => T("檔案數量太多（上限 {0} 個）", "文件数量太多（上限 {0} 个）", "Too many files (limit {0})");
    public static string UnpackedTooLarge => T("解壓後太大（上限 256 MB）", "解压后太大（上限 256 MB）", "Too large when unpacked (256 MB limit)");
    public static string UnsafePath => T("檔案路徑不安全：{0}", "文件路径不安全：{0}", "Unsafe file path: {0}");
    public static string CantOverwriteBuiltin => T("不能覆蓋內建組件", "不能覆盖内置组件", "A built-in widget can't be replaced");

    // ── new widget ──
    public static string EnterName => T("請輸入名稱", "请输入名称", "Enter a name");
    public static string NameTooLong => T("名稱太長（最多 40 個字）", "名称太长（最多 40 个字）", "The name is too long (40 characters at most)");
    public static string NoTemplate => T("找不到組件模板", "找不到组件模板", "The widget template is missing");
}
