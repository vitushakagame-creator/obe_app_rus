using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace ObiBrowser;

internal sealed class BrowserForm : Form
{
    private const string ObiHostUrl = "https://obi-host-panel.radzor111.chatgpt.site/";
    private static readonly Color Navy = Color.FromArgb(21, 43, 62);
    private static readonly Color Mint = Color.FromArgb(166, 242, 220);
    private static readonly Color Chrome = Color.FromArgb(235, 241, 247);
    private static readonly Color Ink = Color.FromArgb(30, 53, 72);
    private static readonly Color Muted = Color.FromArgb(67, 89, 109);

    private readonly TextBox address = new()
    {
        Dock = DockStyle.Fill,
        Margin = new Padding(8, 8, 8, 5),
        Font = new Font("Segoe UI", 11),
        ForeColor = Ink,
        MaxLength = 8192,
        AccessibleName = "Адрес сайта или поисковый запрос"
    };
    private readonly TabControl tabs = new()
    {
        Dock = DockStyle.Fill,
        Margin = Padding.Empty,
        Padding = new Point(14, 5),
        AccessibleName = "Вкладки"
    };
    private readonly Label status = new()
    {
        Dock = DockStyle.Fill,
        Margin = Padding.Empty,
        Padding = new Padding(12, 0, 10, 0),
        TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = Muted,
        AutoEllipsis = true
    };
    private readonly Button back;
    private readonly Button forward;
    private readonly Button reload;
    private readonly Button closeTab;
    private readonly string startPage;
    private Task<CoreWebView2Environment>? environment;
    private bool runtimeErrorShown;

    private BrowserTab? Current => tabs.SelectedTab?.Tag as BrowserTab;

    internal BrowserForm()
    {
        Text = "OBI Browser";
        Font = new Font("Segoe UI", 9F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(1160, 740);
        MinimumSize = new Size(620, 420);
        StartPosition = FormStartPosition.CenterScreen;
        using (Icon? icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath))
            if (icon != null)
                Icon = (Icon)icon.Clone();

        using Stream resource = typeof(BrowserForm).Assembly.GetManifestResourceStream("ObiBrowser.StartPage.html")
            ?? throw new InvalidOperationException("Отсутствует стартовая страница.");
        using var reader = new StreamReader(resource);
        startPage = reader.ReadToEnd();

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Chrome,
            ColumnCount = 1,
            RowCount = 4,
            Margin = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 49));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 27));

        var brand = new Label
        {
            Text = "OBI Browser",
            Font = new Font("Segoe UI", 16F, FontStyle.Bold),
            ForeColor = Mint,
            BackColor = Navy,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(14, 0, 0, 0),
            Margin = Padding.Empty,
            Dock = DockStyle.Fill
        };
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = new Padding(0, 4, 10, 4),
            BackColor = Navy,
            ColumnCount = 3,
            RowCount = 1
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 152));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Button createSite = ButtonFor("Создать сайт", "Открыть OBI Host в новой вкладке", OpenSiteBuilder);
        createSite.BackColor = Mint;
        header.Controls.Add(brand, 0, 0);
        header.Controls.Add(createSite, 1, 0);
        header.Controls.Add(ButtonFor("Вход через Windows ↗", "Открыть вход в OBI Host в браузере Windows", OpenSiteBuilderInWindows), 2, 0);

        var bar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 8,
            RowCount = 1,
            Padding = new Padding(8, 4, 8, 4),
            Margin = Padding.Empty
        };
        foreach (int width in new[] { 38, 38, 38, 38, 0, 62, 96, 38 })
            bar.ColumnStyles.Add(width == 0
                ? new ColumnStyle(SizeType.Percent, 100)
                : new ColumnStyle(SizeType.Absolute, width));
        bar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        back = ButtonFor("←", "Назад", () => Current?.Engine?.GoBack());
        forward = ButtonFor("→", "Вперёд", () => Current?.Engine?.GoForward());
        reload = ButtonFor("↻", "Обновить страницу", () => Current?.Engine?.Reload());
        closeTab = ButtonFor("×", "Закрыть вкладку", CloseCurrentTab);
        bar.Controls.Add(back, 0, 0);
        bar.Controls.Add(forward, 1, 0);
        bar.Controls.Add(reload, 2, 0);
        bar.Controls.Add(ButtonFor("⌂", "Стартовая страница", GoHome), 3, 0);
        bar.Controls.Add(address, 4, 0);
        bar.Controls.Add(ButtonFor("Найти", "Открыть адрес или выполнить поиск", NavigateFromAddress), 5, 0);
        Button addTab = ButtonFor("+ Вкладка", "Новая вкладка", OpenHomeTab);
        addTab.BackColor = Navy;
        addTab.ForeColor = Mint;
        bar.Controls.Add(addTab, 6, 0);
        bar.Controls.Add(closeTab, 7, 0);

        layout.Controls.Add(header, 0, 0);
        layout.Controls.Add(bar, 0, 1);
        layout.Controls.Add(tabs, 0, 2);
        layout.Controls.Add(status, 0, 3);
        Controls.Add(layout);

        address.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter)
                return;
            e.SuppressKeyPress = true;
            NavigateFromAddress();
        };
        tabs.SelectedIndexChanged += (_, _) => RefreshChrome(replaceAddress: true);
        Shown += (_, _) => OpenHomeTab();
        RefreshChrome();
    }

    private static Button ButtonFor(string text, string accessibleName, Action action)
    {
        var button = new Button
        {
            Text = text,
            AccessibleName = accessibleName,
            Dock = DockStyle.Fill,
            Margin = new Padding(2, 3, 2, 3),
            BackColor = Color.White,
            ForeColor = Ink,
            FlatStyle = FlatStyle.Flat,
            UseVisualStyleBackColor = false
        };
        button.FlatAppearance.BorderColor = Color.FromArgb(201, 215, 226);
        button.Click += (_, _) => action();
        return button;
    }

    private async void OpenHomeTab()
    {
        BrowserTab? tab = await OpenTabAsync();
        if (tab?.Engine == null)
            return;

        tab.Engine.NavigateToString(startPage);
        if (tab == Current && !address.Focused && ActiveForm == this)
            FocusAddress();
    }

    private async void OpenSiteBuilder()
    {
        BrowserTab? tab = await OpenTabAsync();
        if (tab?.Engine is CoreWebView2 engine)
            engine.Navigate(ObiHostUrl);
    }

    private void OpenSiteBuilderInWindows()
    {
        try
        {
            using Process? browser = Process.Start(new ProcessStartInfo(ObiHostUrl) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception || ex is InvalidOperationException)
        {
            MessageBox.Show(this, "Открой этот адрес в браузере Windows:\n" + ObiHostUrl,
                "Вход в OBI Host", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private async Task<BrowserTab?> OpenTabAsync()
    {
        var tab = new BrowserTab();
        tabs.TabPages.Add(tab.Page);
        tabs.SelectedTab = tab.Page;
        RefreshChrome(replaceAddress: true);

        try
        {
            string profile = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OBI Browser", "WebView2");
            environment ??= CoreWebView2Environment.CreateAsync(userDataFolder: profile);
            CoreWebView2Environment shared = await environment;
            if (tab.Page.IsDisposed)
                return null;

            await tab.View.EnsureCoreWebView2Async(shared);
            if (tab.Page.IsDisposed)
                return null;

            ConnectPage(tab, tab.View.CoreWebView2);
            tab.Status = "Готово";
            RefreshIfCurrent(tab);
            return tab;
        }
        catch (Exception ex)
        {
            if (tab.Page.IsDisposed)
                return null;

            if (environment?.IsFaulted == true)
                environment = null;

            tab.Status = "Движок WebView2 не запустился";
            RefreshIfCurrent(tab);
            if (!runtimeErrorShown)
            {
                runtimeErrorShown = true;
                MessageBox.Show(this,
                    "Проверь, установлен ли Microsoft Edge WebView2 Runtime, затем открой новую вкладку.\n\n"
                    + ex.Message, "OBI Browser", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            return null;
        }
    }

    private void ConnectPage(BrowserTab tab, CoreWebView2 engine)
    {
        engine.Settings.AreHostObjectsAllowed = false;
        engine.Settings.IsWebMessageEnabled = false;

        engine.NavigationStarting += (sender, e) =>
        {
            if (e.Uri != BrowserAddress.Home && !BrowserAddress.TryWebAddress(e.Uri, out _))
            {
                e.Cancel = true;
                tab.NavigationId = null;
                tab.Status = "Браузер открывает адреса HTTP и HTTPS";
            }
            else
            {
                tab.NavigationId = e.NavigationId;
                tab.Status = "Загрузка…";
            }
            RefreshIfCurrent(tab);
        };

        engine.NavigationCompleted += (_, e) =>
        {
            if (e.NavigationId != tab.NavigationId)
                return;
            tab.NavigationId = null;
            tab.Status = e.IsSuccess || e.WebErrorStatus == CoreWebView2WebErrorStatus.OperationCanceled
                ? "Готово"
                : "Страница не загрузилась · " + e.WebErrorStatus;
            RefreshIfCurrent(tab);
        };
        engine.LaunchingExternalUriScheme += (_, e) => e.Cancel = true;
        engine.SourceChanged += (_, _) => RefreshIfCurrent(tab);
        engine.HistoryChanged += (_, _) => RefreshIfCurrent(tab);
        engine.DocumentTitleChanged += (_, _) =>
        {
            string title = engine.DocumentTitle.Replace('\r', ' ').Replace('\n', ' ').Trim();
            if (title.Length > 26)
                title = title.Substring(0, char.IsHighSurrogate(title[25]) ? 25 : 26) + "…";
            tab.Page.Text = title.Length == 0 ? "Новая вкладка" : title.Replace("&", "&&");
        };
        engine.NewWindowRequested += HandleNewWindow;
        engine.WindowCloseRequested += (_, _) =>
        {
            if (!IsDisposed && IsHandleCreated)
                BeginInvoke((Action)(() => CloseBrowserTab(tab)));
        };
        tab.View.KeyDown += (_, e) =>
        {
            Action? shortcut = FindShortcut(e.KeyData);
            if (shortcut == null)
                return;

            e.Handled = true;
            e.SuppressKeyPress = true;
            BeginInvoke(shortcut);
        };
    }

    private async void HandleNewWindow(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        if (!e.IsUserInitiated || (!string.IsNullOrEmpty(e.Uri) &&
            e.Uri != BrowserAddress.Home && !BrowserAddress.TryWebAddress(e.Uri, out _)))
        {
            e.Handled = true;
            return;
        }

        using CoreWebView2Deferral deferral = e.GetDeferral();
        try
        {
            BrowserTab? tab = await OpenTabAsync();
            if (tab?.Engine is CoreWebView2 engine)
                e.NewWindow = engine;
            else
                e.Handled = true;
        }
        catch (Exception)
        {
            e.Handled = true;
        }
    }

    private void RefreshIfCurrent(BrowserTab tab)
    {
        if (tab == Current)
            RefreshChrome();
    }

    private void RefreshChrome(bool replaceAddress = false)
    {
        BrowserTab? tab = Current;
        CoreWebView2? engine = tab?.Engine;
        string? source = engine?.Source;
        back.Enabled = engine?.CanGoBack == true;
        forward.Enabled = engine?.CanGoForward == true;
        reload.Enabled = engine != null;
        closeTab.Enabled = tab != null;
        if (replaceAddress || !address.Focused)
            address.Text = BrowserAddress.TryWebAddress(source ?? "", out _) ? source : "";
        status.Text = tab?.Status ?? "Открой новую вкладку";
    }

    private void NavigateFromAddress()
    {
        BrowserTab? tab = Current;
        if (tab?.Engine == null)
            return;
        if (!BrowserAddress.TryResolve(address.Text, out Uri? target) || target == null)
        {
            tab.Status = "Введи адрес HTTP / HTTPS или поисковый запрос";
            RefreshChrome();
            address.Focus();
            return;
        }

        tab.View.Focus();
        tab.Engine.Navigate(target.AbsoluteUri);
    }

    private void GoHome()
    {
        if (Current?.Engine is CoreWebView2 engine)
            engine.NavigateToString(startPage);
    }

    private void CloseCurrentTab()
    {
        if (Current is BrowserTab tab)
            CloseBrowserTab(tab);
    }

    private void CloseBrowserTab(BrowserTab tab)
    {
        if (IsDisposed || tab.Page.IsDisposed)
            return;
        tabs.TabPages.Remove(tab.Page);
        tab.Page.Dispose();
        if (tabs.TabCount == 0)
            OpenHomeTab();
    }

    private void FocusAddress()
    {
        address.Focus();
        address.SelectAll();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        Action? shortcut = FindShortcut(keyData);
        if (shortcut == null)
            return base.ProcessCmdKey(ref msg, keyData);

        shortcut();
        return true;
    }

    private Action? FindShortcut(Keys keyData) => keyData switch
    {
        (Keys.Control | Keys.L) => FocusAddress,
        (Keys.Control | Keys.T) => OpenHomeTab,
        (Keys.Control | Keys.W) => CloseCurrentTab,
        (Keys.Alt | Keys.Left) when back.Enabled => () => Current?.Engine?.GoBack(),
        (Keys.Alt | Keys.Right) when forward.Enabled => () => Current?.Engine?.GoForward(),
        Keys.F5 or (Keys.Control | Keys.R) => () => Current?.Engine?.Reload(),
        _ => null
    };

    private sealed class BrowserTab
    {
        internal WebView2 View { get; } = new() { Dock = DockStyle.Fill };
        internal TabPage Page { get; } = new("Новая вкладка");
        internal CoreWebView2? Engine => View.IsDisposed ? null : View.CoreWebView2;
        internal string Status { get; set; } = "Запуск движка…";
        internal ulong? NavigationId { get; set; }

        internal BrowserTab()
        {
            Page.Tag = this;
            Page.Controls.Add(View);
        }
    }
}
