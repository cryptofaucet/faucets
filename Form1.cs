 using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace ProxyHub;

public partial class Form1 : Form
{
    private readonly Color background = Color.FromArgb(18, 21, 29);
    private readonly Color sidebarColor = Color.FromArgb(23, 27, 37);
    private readonly Color panelColor = Color.FromArgb(29, 34, 46);
    private readonly Color accent = Color.FromArgb(75, 130, 220);
    private readonly Color textColor = Color.FromArgb(225, 230, 240);
    private readonly Color mutedColor = Color.FromArgb(145, 155, 175);

    private readonly string dataFolder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ProxyHub");

    private string SourcesFile => Path.Combine(dataFolder, "sources.txt");
    private string ProxiesFile => Path.Combine(dataFolder, "proxies.txt");

    // Старый файл оставлен для совместимости.
    private string ResultsFile => Path.Combine(dataFolder, "proxy-results.json");

    // Новый файл содержит только LIVE-прокси.
    private string LiveProxiesFile => Path.Combine(dataFolder, "live-proxies.json");

    private string GeoSourcesFile => Path.Combine(dataFolder, "geo-sources.json");

    private readonly HttpClient httpClient = new() { Timeout = TimeSpan.FromSeconds(20) };

    private Panel sidebar = null!;
    private Panel viewHost = null!;
    private Panel grabberView = null!;
    private Panel checkerView = null!;
    private Panel liveView = null!;
    private Panel geoView = null!;
    private Panel browserView = null!;
    private Panel browserHost = null!;

    private ListBox sourceList = null!;
    private ListBox geoSourceList = null!;

    private TextBox sourceInput = null!;
    private TextBox geoUrlInput = null!;
    private TextBox geoKeyInput = null!;

    private DataGridView proxyGrid = null!;
    private DataGridView checkerGrid = null!;
    private DataGridView liveGrid = null!;

    private Label grabStatus = null!;
    private Label checkStatus = null!;
    private Label ipStatus = null!;
    private Label liveStatus = null!;

    private Button grabButton = null!;
    private Button checkButton = null!;
    private Button stopButton = null!;
    private Button proxyToggleButton = null!;
    private Button geoAddButton = null!;

    private ComboBox concurrencyBox = null!;
    private ComboBox timeoutBox = null!;
    private ComboBox checkFilterBox = null!;
    private ComboBox countryFilter = null!;
    private ComboBox cityFilter = null!;
    private ComboBox anonymityFilter = null!;
    private ComboBox protocolFilter = null!;
    private ComboBox ipTypeFilter = null!;
    private ComboBox authFilter = null!;
    private ComboBox sortFilter = null!;
    private ComboBox geoProviderBox = null!;

    private TextBox searchFilter = null!;
    private TextBox portFilter = null!;
    private TextBox addressBox = null!;

    private WebView2? browser;

    private string currentBrowserUrl = "https://www.google.com";
    private bool proxyEnabled;
    private bool browserInitializing;
    private string? selectedProxy;
    private string currentExternalIp = "Не определён";

    private int geoEditingIndex = -1;

    private CancellationTokenSource? checkCancellation;

    private readonly Dictionary<string, ProxyRecord> proxies =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly List<GeoSource> geoSources = new();

    private const string TestUrl = "https://api.ipify.org";
    private const string HeaderTestUrl = "https://httpbin.org/headers";

    public Form1()
    {
        InitializeComponent();

        Directory.CreateDirectory(dataFolder);

        Text = "ProxyHub";
        MinimumSize = new Size(1050, 700);
        Size = new Size(1350, 850);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = background;
        ForeColor = textColor;
        Font = new Font("Segoe UI", 9F);

        BuildInterface();

        LoadSources();
        LoadProxies();
        LoadResults();
        LoadGeoSources();

        RefreshGrabGrid();
        RefreshLiveGrid();
        RefreshGeoSourceList();

        _ = UpdateDirectIpAsync();
    }

    // =====================================================
    // ОСНОВНОЙ ИНТЕРФЕЙС
    // =====================================================

    private void BuildInterface()
    {
        sidebar = new Panel
        {
            Dock = DockStyle.Left,
            Width = 220,
            BackColor = sidebarColor
        };

        Panel workspace = new()
        {
            Dock = DockStyle.Fill,
            BackColor = background
        };

        viewHost = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = background,
            Padding = new Padding(22)
        };

        Panel topBar = new()
        {
            Dock = DockStyle.Top,
            Height = 48,
            BackColor = sidebarColor,
            Padding = new Padding(18, 5, 15, 5)
        };

        TableLayoutPanel topLayout = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            BackColor = sidebarColor
        };

        topLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        topLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 155));

        ipStatus = new Label
        {
            Text = "Внешний IP: определяется...",
            ForeColor = textColor,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 10F, FontStyle.Bold)
        };

        proxyToggleButton = CreateButton("Прокси: ВЫКЛ", Color.FromArgb(65, 125, 80));
        proxyToggleButton.Dock = DockStyle.Fill;
        proxyToggleButton.Click += async (_, _) => await ToggleProxy();

        topLayout.Controls.Add(ipStatus, 0, 0);
        topLayout.Controls.Add(proxyToggleButton, 1, 0);
        topBar.Controls.Add(topLayout);

        Label logo = new()
        {
            Text = "PROXY HUB",
            Font = new Font("Segoe UI", 19F, FontStyle.Bold),
            ForeColor = Color.White,
            Location = new Point(22, 25),
            AutoSize = true
        };

        Label menuTitle = new()
        {
            Text = "ИНСТРУМЕНТЫ",
            Font = new Font("Segoe UI", 8F, FontStyle.Bold),
            ForeColor = mutedColor,
            Location = new Point(24, 100),
            AutoSize = true
        };

        AddMenuButton("Proxy Grabber", 130, () => ShowView(grabberView));
        AddMenuButton("Proxy Checker", 180, () => ShowView(checkerView));
        AddMenuButton("LIVE PROXIES", 230, () => ShowView(liveView));
        AddMenuButton("GEO-IP SOURCES", 280, () => ShowView(geoView));

        Button browserButton = CreateButton("Тестовый браузер", panelColor);
        browserButton.Location = new Point(12, 330);
        browserButton.Size = new Size(196, 43);
        browserButton.Click += async (_, _) =>
        {
            ShowView(browserView);
            await EnsureBrowserInitializedAsync();
        };

        Label version = new()
        {
            Text = "ProxyHub v0.5",
            ForeColor = mutedColor,
            AutoSize = true,
            Location = new Point(22, 400)
        };

        sidebar.Controls.Add(logo);
        sidebar.Controls.Add(menuTitle);
        sidebar.Controls.Add(browserButton);
        sidebar.Controls.Add(version);

        grabberView = BuildGrabberWorkspace();
        checkerView = BuildCheckerWorkspace();
        liveView = BuildLiveWorkspace();
        geoView = BuildGeoSourcesWorkspace();
        browserView = BuildBrowserWorkspace();

        workspace.Controls.Add(viewHost);
        workspace.Controls.Add(topBar);

        Controls.Add(workspace);
        Controls.Add(sidebar);

        ShowView(grabberView);
    }

    private void AddMenuButton(string caption, int y, Action action)
    {
        Button button = CreateButton(caption, panelColor);
        button.Location = new Point(12, y);
        button.Size = new Size(196, 43);
        button.Click += (_, _) => action();
        sidebar.Controls.Add(button);
    }

    private void ShowView(Panel view)
    {
        viewHost.Controls.Clear();
        view.Dock = DockStyle.Fill;
        viewHost.Controls.Add(view);
    }

    // =====================================================
    // GRABBER
    // =====================================================

    private Panel BuildGrabberWorkspace()
    {
        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = background
        };

        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 210));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        Panel header = CreateHeader(
            "Proxy Grabber",
            "Сбор прокси из выбранных источников"
        );

        Panel sourcePanel = new()
        {
            Dock = DockStyle.Fill,
            BackColor = panelColor,
            Padding = new Padding(14)
        };

        Label sourceTitle = CreateLabel("ИСТОЧНИКИ ПРОКСИ", 30, true);
        sourceTitle.Dock = DockStyle.Top;

        Panel inputPanel = new()
        {
            Dock = DockStyle.Top,
            Height = 42,
            BackColor = panelColor
        };

        sourceInput = CreateTextBox();
        sourceInput.PlaceholderText = "https://example.com/proxy-list.txt";
        sourceInput.Location = new Point(0, 5);
        sourceInput.Width = 450;

        Button addButton = CreateButton("Добавить", accent);
        addButton.SetBounds(460, 4, 105, 32);
        addButton.Click += (_, _) => AddSource();

        Button removeButton = CreateButton("Удалить", Color.FromArgb(150, 65, 70));
        removeButton.SetBounds(575, 4, 100, 32);
        removeButton.Click += (_, _) => RemoveSource();

        inputPanel.Controls.Add(sourceInput);
        inputPanel.Controls.Add(addButton);
        inputPanel.Controls.Add(removeButton);

        sourceList = new ListBox
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(39, 45, 59),
            ForeColor = textColor,
            BorderStyle = BorderStyle.None,
            IntegralHeight = false
        };

        sourcePanel.Controls.Add(sourceList);
        sourcePanel.Controls.Add(inputPanel);
        sourcePanel.Controls.Add(sourceTitle);

        sourcePanel.Resize += (_, _) =>
        {
            sourceInput.Width = Math.Max(150, inputPanel.ClientSize.Width - 235);
            addButton.Location = new Point(sourceInput.Right + 10, 4);
            removeButton.Location = new Point(addButton.Right + 10, 4);
        };

        Panel results = new()
        {
            Dock = DockStyle.Fill,
            BackColor = background,
            Padding = new Padding(0, 12, 0, 0)
        };

        Panel resultHeader = new()
        {
            Dock = DockStyle.Top,
            Height = 75,
            BackColor = background
        };

        Label resultTitle = CreateLabel("НАЙДЕННЫЕ ПРОКСИ", 22, true);
        resultTitle.Location = new Point(0, 0);

        grabButton = CreateButton("Собрать прокси", accent);
        grabButton.SetBounds(0, 32, 155, 35);
        grabButton.Click += async (_, _) => await GrabProxies();

        Button importButton = CreateButton("Импорт списка", panelColor);
        importButton.SetBounds(165, 32, 130, 35);
        importButton.Click += (_, _) => ImportProxies();

        grabStatus = CreateLabel("Прокси в базе: 0", 40);
        grabStatus.Location = new Point(310, 40);

        resultHeader.Controls.Add(resultTitle);
        resultHeader.Controls.Add(grabButton);
        resultHeader.Controls.Add(importButton);
        resultHeader.Controls.Add(grabStatus);

        proxyGrid = CreateGrid();
        proxyGrid.Columns.Add("proxy", "Прокси");
        proxyGrid.Columns.Add("source", "Источник");

        results.Controls.Add(proxyGrid);
        results.Controls.Add(resultHeader);

        layout.Controls.Add(header, 0, 0);
        layout.Controls.Add(sourcePanel, 0, 1);
        layout.Controls.Add(results, 0, 2);

        Panel view = new() { Dock = DockStyle.Fill, BackColor = background };
        view.Controls.Add(layout);
        return view;
    }

    private async Task GrabProxies()
    {
        if (sourceList.Items.Count == 0)
        {
            MessageBox.Show("Сначала добавь хотя бы один источник.");
            return;
        }

        grabButton.Enabled = false;
        grabButton.Text = "Загрузка...";

        int added = 0;
        int failed = 0;

        try
        {
            foreach (string url in sourceList.Items.Cast<string>().ToArray())
            {
                try
                {
                    string content = await httpClient.GetStringAsync(url);

                    foreach (string line in content.Split(
                        new[] { '\r', '\n' },
                        StringSplitOptions.RemoveEmptyEntries))
                    {
                        string proxy = CleanProxyLine(line);

                        if (proxy.Length == 0 || !IsProxyCandidate(proxy))
                            continue;

                        if (!proxies.ContainsKey(proxy))
                        {
                            ProxyRecord record = new() { Address = proxy };
                            EnsureProxyPort(record);

                            proxies[proxy] = record;
                            added++;
                        }
                    }
                }
                catch
                {
                    failed++;
                }
            }

            SaveProxies();
            RefreshGrabGrid();

            grabStatus.Text =
                $"Добавлено: {added} | Всего: {proxies.Count} | Ошибок: {failed}";
        }
        finally
        {
            grabButton.Enabled = true;
            grabButton.Text = "Собрать прокси";
        }
    }

    private void ImportProxies()
    {
        using OpenFileDialog dialog = new()
        {
            Title = "Импорт списка прокси",
            Filter = "Текстовые файлы (*.txt)|*.txt|Все файлы (*.*)|*.*"
        };

        if (dialog.ShowDialog() != DialogResult.OK)
            return;

        int added = 0;

        try
        {
            foreach (string line in File.ReadAllLines(dialog.FileName))
            {
                string proxy = CleanProxyLine(line);

                if (!IsProxyCandidate(proxy) || proxies.ContainsKey(proxy))
                    continue;

                ProxyRecord record = new() { Address = proxy };
                EnsureProxyPort(record);

                proxies[proxy] = record;
                added++;
            }

            SaveProxies();
            RefreshGrabGrid();
            grabStatus.Text = $"Импортировано: {added} | Всего: {proxies.Count}";
        }
        catch (Exception ex)
        {
            MessageBox.Show("Ошибка импорта:\n" + ex.Message);
        }
    }

    // =====================================================
    // CHECKER
    // =====================================================

    private Panel BuildCheckerWorkspace()
    {
        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = background
        };

        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 145));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        Panel header = CreateHeader(
            "Proxy Checker",
            "Проверка доступности HTTP-прокси и получение внешнего IP"
        );

        Panel settings = new()
        {
            Dock = DockStyle.Fill,
            BackColor = panelColor,
            Padding = new Padding(12)
        };

        settings.Controls.Add(CreateLabel("Потоки", 10, true));

        concurrencyBox = CreateCombo(new[] { "10", "25", "50", "100" });
        concurrencyBox.SetBounds(12, 35, 100, 30);
        concurrencyBox.SelectedItem = "25";

        settings.Controls.Add(CreateLabel("Таймаут, сек.", 10, true, 135, 10));

        timeoutBox = CreateCombo(new[] { "3", "5", "10", "15", "30" });
        timeoutBox.SetBounds(135, 35, 100, 30);
        timeoutBox.SelectedItem = "10";

        settings.Controls.Add(CreateLabel("Что проверять", 10, true, 255, 10));

        checkFilterBox = CreateCombo(new[]
        {
            "Вся база",
            "Только LIVE",
            "Только DEAD"
        });
        checkFilterBox.SetBounds(255, 35, 150, 30);
        checkFilterBox.SelectedIndex = 0;

        checkButton = CreateButton("Начать проверку", accent);
        checkButton.SetBounds(425, 33, 155, 35);
        checkButton.Click += async (_, _) => await StartChecking();

        stopButton = CreateButton("Остановить", Color.FromArgb(150, 65, 70));
        stopButton.SetBounds(590, 33, 120, 35);
        stopButton.Enabled = false;
        stopButton.Click += (_, _) => checkCancellation?.Cancel();

        checkStatus = CreateLabel("Прокси в базе: 0", 88);
        checkStatus.Location = new Point(12, 88);

        settings.Controls.Add(concurrencyBox);
        settings.Controls.Add(timeoutBox);
        settings.Controls.Add(checkFilterBox);
        settings.Controls.Add(checkButton);
        settings.Controls.Add(stopButton);
        settings.Controls.Add(checkStatus);

        checkerGrid = CreateGrid();
        checkerGrid.Columns.Add("proxy", "Прокси");
        checkerGrid.Columns.Add("status", "Статус");
        checkerGrid.Columns.Add("ip", "Внешний IP");
        checkerGrid.Columns.Add("ping", "HTTP отклик");

        layout.Controls.Add(header, 0, 0);
        layout.Controls.Add(settings, 0, 1);
        layout.Controls.Add(checkerGrid, 0, 2);

        Panel view = new() { Dock = DockStyle.Fill, BackColor = background };
        view.Controls.Add(layout);
        return view;
    }

    private async Task StartChecking()
    {
        if (checkCancellation != null)
            return;

        IEnumerable<ProxyRecord> query = proxies.Values;

        string filter = checkFilterBox.SelectedItem?.ToString() ?? "Вся база";

        if (filter == "Только LIVE")
            query = query.Where(p => p.Status == "LIVE");
        else if (filter == "Только DEAD")
            query = query.Where(p => p.Status == "DEAD");

        ProxyRecord[] items = query.ToArray();

        if (items.Length == 0)
        {
            MessageBox.Show("Нет прокси для проверки по выбранному фильтру.");
            return;
        }

        checkerGrid.Rows.Clear();

        foreach (ProxyRecord item in items)
            checkerGrid.Rows.Add(item.Address, "Ожидание", item.ExitIp, "—");

        int concurrency = int.Parse(concurrencyBox.SelectedItem!.ToString()!);
        int timeout = int.Parse(timeoutBox.SelectedItem!.ToString()!);

        checkButton.Enabled = false;
        stopButton.Enabled = true;
        checkFilterBox.Enabled = false;

        checkCancellation = new CancellationTokenSource();

        int completed = 0;
        int live = 0;
        int dead = 0;

        var progress = new Progress<CheckResult>(r =>
        {
            if (r.Index < checkerGrid.Rows.Count)
            {
                DataGridViewRow row = checkerGrid.Rows[r.Index];

                row.Cells["status"].Value = r.Status;
                row.Cells["ip"].Value = r.Ip;
                row.Cells["ping"].Value = r.Ping;

                row.DefaultCellStyle.ForeColor =
                    r.Status == "LIVE" ? Color.LightGreen :
                    r.Status == "DEAD" ? Color.Salmon : mutedColor;
            }

            completed++;

            if (r.Status == "LIVE") live++;
            if (r.Status == "DEAD") dead++;

            checkStatus.Text =
                $"Проверено: {completed}/{items.Length} | LIVE: {live} | DEAD: {dead}";
        });

        using SemaphoreSlim semaphore = new(concurrency);

        try
        {
            CancellationToken token = checkCancellation.Token;

            Task[] tasks = items.Select((item, index) =>
                CheckOneProxy(index, item, timeout, semaphore, token, progress)
            ).ToArray();

            await Task.WhenAll(tasks);

            SaveResults();
            RefreshLiveGrid();

            checkStatus.Text =
                $"Готово! Проверено: {completed} | LIVE: {live} | DEAD: {dead}";
        }
        catch (OperationCanceledException)
        {
            SaveResults();
            RefreshLiveGrid();
            checkStatus.Text = $"Остановлено. LIVE: {live} | DEAD: {dead}";
        }
        finally
        {
            checkCancellation?.Dispose();
            checkCancellation = null;

            checkButton.Enabled = true;
            stopButton.Enabled = false;
            checkFilterBox.Enabled = true;
        }
    }

    private async Task CheckOneProxy(
        int index,
        ProxyRecord record,
        int timeoutSeconds,
        SemaphoreSlim semaphore,
        CancellationToken globalToken,
        IProgress<CheckResult> progress)
    {
        bool entered = false;

        try
        {
            await semaphore.WaitAsync(globalToken);
            entered = true;

            using CancellationTokenSource timeout =
                CancellationTokenSource.CreateLinkedTokenSource(globalToken);

            timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

            Stopwatch sw = Stopwatch.StartNew();

            try
            {
                string ip = await GetExternalIpAsync(record.Address, timeout.Token);

                sw.Stop();

                record.Status = "LIVE";
                record.ExitIp = ip;
                record.SpeedMs = (int)sw.ElapsedMilliseconds;
                record.LastChecked = DateTime.Now;
                record.Protocol = "HTTP";

                EnsureProxyPort(record);

                // Ошибка определения анонимности не должна убивать прокси.
                record.Anonymity =
                    await DetectAnonymityAsync(record.Address, timeout.Token);

                // Geo-IP — отдельная необязательная операция.
                // Ошибка Geo-IP не меняет статус LIVE.
                try
                {
                    await LookupGeoAsync(record, timeout.Token);
                }
                catch (OperationCanceledException)
                {
                    if (globalToken.IsCancellationRequested)
                        throw;

                    // Таймаут Geo-IP не означает, что прокси не работает.
                }
                catch
                {
                    // Прокси остаётся LIVE.
                }

                progress.Report(new CheckResult(
                    index, "LIVE", ip, $"{record.SpeedMs} ms"
                ));
            }
            catch (OperationCanceledException)
            {
                if (globalToken.IsCancellationRequested)
                {
                    progress.Report(new CheckResult(
                        index, "STOPPED", record.ExitIp, "—"
                    ));
                }
                else
                {
                    record.Status = "DEAD";
                    record.LastChecked = DateTime.Now;

                    progress.Report(new CheckResult(
                        index, "DEAD", record.ExitIp, "Таймаут проверки прокси"
                    ));
                }
            }
            catch
            {
                record.Status = "DEAD";
                record.LastChecked = DateTime.Now;

                progress.Report(new CheckResult(
                    index, "DEAD", record.ExitIp, "Ошибка подключения"
                ));
            }
        }
        catch (OperationCanceledException)
        {
            progress.Report(new CheckResult(
                index, "STOPPED", record.ExitIp, "—"
            ));
        }
        finally
        {
            if (entered)
                semaphore.Release();
        }
    }

    private async Task<string> DetectAnonymityAsync(
        string proxyText,
        CancellationToken token)
    {
        try
        {
            using HttpClient client = CreateProxyClient(proxyText, 8);

            using HttpResponseMessage response =
                await client.GetAsync(HeaderTestUrl, token);

            string json = await response.Content.ReadAsStringAsync(token);
            using JsonDocument doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("headers", out JsonElement headers))
                return "Не проверено";

            bool hasForwarded =
                headers.TryGetProperty("X-Forwarded-For", out _) ||
                headers.TryGetProperty("Forwarded", out _) ||
                headers.TryGetProperty("Via", out _) ||
                headers.TryGetProperty("X-Real-Ip", out _);

            if (!hasForwarded)
                return "Elite";

            string combined = headers.ToString().ToLowerInvariant();

            if (combined.Contains("x-forwarded-for") ||
                combined.Contains("x-real-ip"))
                return "Transparent";

            return "Anonymous";
        }
        catch (OperationCanceledException)
        {
            if (token.IsCancellationRequested)
                throw;

            return "Не проверено";
        }
        catch
        {
            return "Не проверено";
        }
    }

    // =====================================================
    // LIVE PROXIES
    // =====================================================

    private Panel BuildLiveWorkspace()
    {
        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = background
        };

        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 75));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 180));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        Panel header = CreateHeader(
            "LIVE PROXIES",
            "Рабочие прокси с геолокацией и фильтрами"
        );

        Panel filters = new()
        {
            Dock = DockStyle.Fill,
            BackColor = panelColor,
            Padding = new Padding(10)
        };

        filters.Controls.Add(CreateLabel("Страна", 10, true, 10, 5));
        countryFilter = CreateCombo(Array.Empty<string>());
        countryFilter.SetBounds(10, 27, 145, 28);

        filters.Controls.Add(CreateLabel("Город", 10, true, 165, 5));
        cityFilter = CreateCombo(Array.Empty<string>());
        cityFilter.SetBounds(165, 27, 145, 28);

        filters.Controls.Add(CreateLabel("Анонимность", 10, true, 320, 5));
        anonymityFilter = CreateCombo(new[]
        {
            "Elite", "Все", "Anonymous", "Transparent", "Не проверено"
        });
        anonymityFilter.SetBounds(320, 27, 145, 28);
        anonymityFilter.SelectedIndex = 0;

        filters.Controls.Add(CreateLabel("Протокол", 10, true, 475, 5));
        protocolFilter = CreateCombo(new[] { "Все", "HTTP" });
        protocolFilter.SetBounds(475, 27, 110, 28);
        protocolFilter.SelectedIndex = 0;

        filters.Controls.Add(CreateLabel("Тип IP", 10, true, 595, 5));
        ipTypeFilter = CreateCombo(new[]
        {
            "Все", "Datacenter", "Residential", "Mobile", "Неизвестно"
        });
        ipTypeFilter.SetBounds(595, 27, 135, 28);
        ipTypeFilter.SelectedIndex = 0;

        filters.Controls.Add(CreateLabel("Авторизация", 10, true, 740, 5));
        authFilter = CreateCombo(new[] { "Все", "Требуется", "Не требуется" });
        authFilter.SetBounds(740, 27, 135, 28);
        authFilter.SelectedIndex = 0;

        filters.Controls.Add(CreateLabel("Поиск IP", 10, true, 10, 65));
        searchFilter = CreateTextBox();
        searchFilter.SetBounds(10, 87, 145, 28);

        filters.Controls.Add(CreateLabel("Порт", 10, true, 165, 65));
        portFilter = CreateTextBox();
        portFilter.SetBounds(165, 87, 90, 28);

        filters.Controls.Add(CreateLabel("Сортировка", 10, true, 265, 65));
        sortFilter = CreateCombo(new[] { "Скорость", "Страна", "Город" });
        sortFilter.SetBounds(265, 87, 145, 28);
        sortFilter.SelectedIndex = 0;

        Button apply = CreateButton("Применить", accent);
        apply.SetBounds(425, 85, 110, 32);
        apply.Click += (_, _) => RefreshLiveGrid();

        Button favorite = CreateButton("★ Избранное", Color.FromArgb(125, 100, 45));
        favorite.SetBounds(545, 85, 125, 32);
        favorite.Click += (_, _) =>
        {
            if (liveGrid.CurrentRow?.Tag is ProxyRecord record)
            {
                record.Favorite = !record.Favorite;
                SaveResults();
                RefreshLiveGrid();
            }
        };

        Button select = CreateButton("Выбрать прокси", accent);
        select.SetBounds(680, 85, 140, 32);
        select.Click += (_, _) => SelectLiveProxy();

        Button recheck = CreateButton("Перепроверить", panelColor);
        recheck.SetBounds(830, 85, 130, 32);
        recheck.Click += async (_, _) => await RecheckSelectedProxy();

        liveStatus = CreateLabel("Рабочих прокси: 0", 132);
        liveStatus.Location = new Point(10, 135);

        filters.Controls.Add(countryFilter);
        filters.Controls.Add(cityFilter);
        filters.Controls.Add(anonymityFilter);
        filters.Controls.Add(protocolFilter);
        filters.Controls.Add(ipTypeFilter);
        filters.Controls.Add(authFilter);
        filters.Controls.Add(searchFilter);
        filters.Controls.Add(portFilter);
        filters.Controls.Add(sortFilter);
        filters.Controls.Add(apply);
        filters.Controls.Add(favorite);
        filters.Controls.Add(select);
        filters.Controls.Add(recheck);
        filters.Controls.Add(liveStatus);

        liveGrid = CreateGrid();
        liveGrid.Columns.Add("proxy", "Прокси");
        liveGrid.Columns.Add("ip", "Внешний IP");
        liveGrid.Columns.Add("country", "Страна");
        liveGrid.Columns.Add("city", "Город");
        liveGrid.Columns.Add("port", "Порт");
        liveGrid.Columns.Add("anonymity", "Анонимность");
        liveGrid.Columns.Add("protocol", "Протокол");
        liveGrid.Columns.Add("speed", "HTTP отклик");
        liveGrid.Columns.Add("iptype", "Тип IP");
        liveGrid.Columns.Add("auth", "Авторизация");
        liveGrid.Columns.Add("favorite", "★");

        liveGrid.CellDoubleClick += (_, _) => SelectLiveProxy();

        layout.Controls.Add(header, 0, 0);
        layout.Controls.Add(filters, 0, 1);
        layout.Controls.Add(liveGrid, 0, 2);

        Panel view = new() { Dock = DockStyle.Fill, BackColor = background };
        view.Controls.Add(layout);

        return view;
    }

    private void RefreshLiveGrid()
    {
        if (liveGrid == null)
            return;

        liveGrid.Rows.Clear();

        IEnumerable<ProxyRecord> query =
            proxies.Values.Where(p => p.Status == "LIVE");

        foreach (ProxyRecord p in query)
            EnsureProxyPort(p);

        string country = countryFilter?.SelectedItem?.ToString() ?? "Все";
        string city = cityFilter?.SelectedItem?.ToString() ?? "Все";
        string anonymity = anonymityFilter?.SelectedItem?.ToString() ?? "Elite";
        string protocol = protocolFilter?.SelectedItem?.ToString() ?? "Все";
        string ipType = ipTypeFilter?.SelectedItem?.ToString() ?? "Все";
        string auth = authFilter?.SelectedItem?.ToString() ?? "Все";
        string search = searchFilter?.Text.Trim() ?? "";
        string port = portFilter?.Text.Trim() ?? "";

        if (anonymity != "Все")
            query = query.Where(p => p.Anonymity == anonymity);

        if (country != "Все")
            query = query.Where(p => p.Country == country);

        if (city != "Все")
            query = query.Where(p => p.City == city);

        if (protocol != "Все")
            query = query.Where(p => p.Protocol == protocol);

        if (ipType != "Все")
            query = query.Where(p => p.IpType == ipType);

        if (auth == "Требуется")
            query = query.Where(p => p.RequiresAuth);

        if (auth == "Не требуется")
            query = query.Where(p => !p.RequiresAuth);

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(p =>
                p.ExitIp.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                p.Address.Contains(search, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(port))
            query = query.Where(p => p.Port.ToString() == port);

        string sort = sortFilter?.SelectedItem?.ToString() ?? "Скорость";

        query = sort switch
        {
            "Страна" => query.OrderBy(p => p.Country),
            "Город" => query.OrderBy(p => p.City),
            _ => query.OrderBy(p => p.SpeedMs)
        };

        foreach (ProxyRecord p in query)
        {
            int index = liveGrid.Rows.Add(
                p.Address,
                p.ExitIp,
                p.Country,
                p.City,
                p.Port,
                p.Anonymity,
                p.Protocol,
                p.SpeedMs > 0 ? p.SpeedMs + " ms" : "—",
                p.IpType,
                p.RequiresAuth ? "Да" : "Нет",
                p.Favorite ? "★" : ""
            );

            liveGrid.Rows[index].Tag = p;
            liveGrid.Rows[index].DefaultCellStyle.ForeColor =
                p.Favorite ? Color.Gold : textColor;
        }

        UpdateFilterOptions();

        liveStatus.Text =
            $"Рабочих прокси: {proxies.Values.Count(p => p.Status == "LIVE")} | " +
            $"Показано: {liveGrid.Rows.Count} | Выбран: {selectedProxy ?? "нет"}";
    }

    private void UpdateFilterOptions()
    {
        SetFilterItems(countryFilter,
            proxies.Values.Where(p => p.Status == "LIVE")
                .Select(p => p.Country)
                .Where(x => !string.IsNullOrWhiteSpace(x) && x != "Неизвестно"));

        SetFilterItems(cityFilter,
            proxies.Values.Where(p => p.Status == "LIVE")
                .Select(p => p.City)
                .Where(x => !string.IsNullOrWhiteSpace(x) && x != "Неизвестно"));
    }

    private void SetFilterItems(ComboBox box, IEnumerable<string> values)
    {
        if (box == null)
            return;

        string previous = box.SelectedItem?.ToString() ?? "Все";

        box.BeginUpdate();
        box.Items.Clear();
        box.Items.Add("Все");

        foreach (string value in values.Distinct().OrderBy(x => x))
            box.Items.Add(value);

        box.SelectedItem = box.Items.Contains(previous) ? previous : "Все";
        box.EndUpdate();
    }

    private void SelectLiveProxy()
    {
        if (liveGrid.CurrentRow?.Tag is not ProxyRecord record)
        {
            MessageBox.Show("Сначала выбери прокси в таблице.");
            return;
        }

        selectedProxy = record.Address;

        MessageBox.Show(
            "Прокси выбран:\n" + selectedProxy +
            "\n\nТеперь можно включить его кнопкой в верхней панели."
        );

        UpdateIpLabel(proxyEnabled ? "Прокси включён" : "Прокси выбран, но выключен");
        RefreshLiveGrid();
    }

    private async Task RecheckSelectedProxy()
    {
        if (liveGrid.CurrentRow?.Tag is not ProxyRecord record)
        {
            MessageBox.Show("Выбери прокси в таблице.");
            return;
        }

        try
        {
            Stopwatch sw = Stopwatch.StartNew();

            string ip = await GetExternalIpAsync(
                record.Address,
                CancellationToken.None
            );

            sw.Stop();

            record.Status = "LIVE";
            record.ExitIp = ip;
            record.SpeedMs = (int)sw.ElapsedMilliseconds;
            record.Protocol = "HTTP";
            record.LastChecked = DateTime.Now;

            EnsureProxyPort(record);

            record.Anonymity =
                await DetectAnonymityAsync(record.Address, CancellationToken.None);

            // Geo-IP не влияет на статус прокси.
            try
            {
                await LookupGeoAsync(record, CancellationToken.None);
            }
            catch
            {
                // Сохраняем LIVE даже при недоступности Geo-IP.
            }

            SaveResults();
            RefreshLiveGrid();

            MessageBox.Show("Прокси работает. IP: " + ip);
        }
        catch (Exception ex)
        {
            record.Status = "DEAD";
            record.LastChecked = DateTime.Now;

            SaveResults();
            RefreshLiveGrid();

            MessageBox.Show("Прокси не отвечает:\n" + ex.Message);
        }
    }

    // =====================================================
    // GEO-IP SOURCES
    // =====================================================

    private Panel BuildGeoSourcesWorkspace()
    {
        Panel view = new()
        {
            Dock = DockStyle.Fill,
            BackColor = background
        };

        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = background
        };

        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        Panel header = CreateHeader(
            "GEO-IP SOURCES",
            "Управление API для определения страны и города прокси"
        );

        Panel workspace = new()
        {
            Dock = DockStyle.Fill,
            BackColor = panelColor,
            Padding = new Padding(14)
        };

        BuildGeoSourceControls(workspace);

        layout.Controls.Add(header, 0, 0);
        layout.Controls.Add(workspace, 0, 1);

        view.Controls.Add(layout);
        return view;
    }

    private void BuildGeoSourceControls(Panel parent)
    {
        Label title = CreateLabel("СПИСОК ИСТОЧНИКОВ", 10, true, 10, 10);

        geoSourceList = new ListBox
        {
            BackColor = Color.FromArgb(39, 45, 59),
            ForeColor = textColor,
            BorderStyle = BorderStyle.None,
            IntegralHeight = false
        };

        geoSourceList.SetBounds(10, 35, 700, 150);
        geoSourceList.SelectedIndexChanged += (_, _) =>
        {
            if (geoSourceList.SelectedIndex >= 0)
                LoadGeoSourceForEdit();
        };

        Label urlLabel = CreateLabel("URL API", 10, true, 10, 198);

        geoUrlInput = CreateTextBox();
        geoUrlInput.PlaceholderText = "https://ipwho.is/{ip}";
        geoUrlInput.SetBounds(10, 220, 500, 30);

        Label providerLabel = CreateLabel("Провайдер", 10, true, 530, 198);

        geoProviderBox = CreateCombo(new[] { "ipwho.is", "IPWho.org" });
        geoProviderBox.SetBounds(530, 220, 180, 30);
        geoProviderBox.SelectedIndex = 0;

        Label keyLabel = CreateLabel("API KEY", 10, true, 10, 265);

        geoKeyInput = CreateTextBox();
        geoKeyInput.PlaceholderText = "API key (если требуется)";
        geoKeyInput.SetBounds(10, 287, 500, 30);

        geoAddButton = CreateButton("Добавить источник", accent);
        geoAddButton.SetBounds(10, 340, 155, 35);
        geoAddButton.Click += (_, _) => AddGeoSource();

        Button activate = CreateButton("Сделать активным", panelColor);
        activate.SetBounds(175, 340, 155, 35);
        activate.Click += (_, _) => ActivateGeoSource();

        Button remove = CreateButton("Удалить", Color.FromArgb(150, 65, 70));
        remove.SetBounds(340, 340, 100, 35);
        remove.Click += (_, _) => RemoveGeoSource();

        Button test = CreateButton("Тест API", panelColor);
        test.SetBounds(450, 340, 100, 35);
        test.Click += async (_, _) => await TestGeoSource();

        Button cancelEdit = CreateButton("Очистить", panelColor);
        cancelEdit.SetBounds(560, 340, 100, 35);
        cancelEdit.Click += (_, _) => ClearGeoEditor();

        Label hint = CreateLabel(
            "В URL обязательно должен присутствовать параметр {ip}. Например: https://ipwho.is/{ip}",
            395
        );
        hint.Location = new Point(10, 395);

        parent.Controls.Add(title);
        parent.Controls.Add(geoSourceList);
        parent.Controls.Add(urlLabel);
        parent.Controls.Add(geoUrlInput);
        parent.Controls.Add(providerLabel);
        parent.Controls.Add(geoProviderBox);
        parent.Controls.Add(keyLabel);
        parent.Controls.Add(geoKeyInput);
        parent.Controls.Add(geoAddButton);
        parent.Controls.Add(activate);
        parent.Controls.Add(remove);
        parent.Controls.Add(test);
        parent.Controls.Add(cancelEdit);
        parent.Controls.Add(hint);
    }

    private void LoadGeoSourceForEdit()
    {
        int index = geoSourceList.SelectedIndex;

        if (index < 0 || index >= geoSources.Count)
            return;

        GeoSource source = geoSources[index];

        geoEditingIndex = index;

        geoUrlInput.Text = source.Url;
        geoKeyInput.Text = source.ApiKey;

        if (geoProviderBox.Items.Contains(source.Provider))
            geoProviderBox.SelectedItem = source.Provider;
        else
            geoProviderBox.SelectedIndex = 0;

        geoAddButton.Text = "Сохранить изменения";
    }

    private void ClearGeoEditor()
    {
        geoEditingIndex = -1;

        geoUrlInput.Clear();
        geoKeyInput.Clear();

        if (geoProviderBox.Items.Count > 0)
            geoProviderBox.SelectedIndex = 0;

        geoAddButton.Text = "Добавить источник";

        geoSourceList.ClearSelected();
    }

    private void RefreshGeoSourceList()
    {
        if (geoSourceList == null)
            return;

        int previousIndex = geoSourceList.SelectedIndex;

        geoSourceList.BeginUpdate();
        geoSourceList.Items.Clear();

        foreach (GeoSource source in geoSources)
        {
            string active = source.Active ? " [АКТИВЕН]" : "";
            geoSourceList.Items.Add(
                $"{source.Provider} | {source.Url}{active}"
            );
        }

        geoSourceList.EndUpdate();

        if (previousIndex >= 0 && previousIndex < geoSourceList.Items.Count)
            geoSourceList.SelectedIndex = previousIndex;
    }

    private void AddGeoSource()
    {
        string url = geoUrlInput.Text.Trim();
        string provider = geoProviderBox.SelectedItem?.ToString() ?? "ipwho.is";

        if (!url.Contains("{ip}") ||
            !Uri.TryCreate(
                url.Replace("{ip}", "1.1.1.1"),
                UriKind.Absolute,
                out Uri? uri
            ) ||
            (uri.Scheme != "http" && uri.Scheme != "https"))
        {
            MessageBox.Show(
                "Укажи URL с параметром {ip}.\nНапример: https://ipwho.is/{ip}"
            );
            return;
        }

        if (geoEditingIndex >= 0 && geoEditingIndex < geoSources.Count)
        {
            // Редактируем выбранный источник, не создавая дубликат.
            GeoSource old = geoSources[geoEditingIndex];

            old.Url = url;
            old.Provider = provider;
            old.ApiKey = geoKeyInput.Text.Trim();

            SaveGeoSources();
            RefreshGeoSourceList();

            geoSourceList.SelectedIndex = geoEditingIndex;

            MessageBox.Show("Источник Geo-IP обновлён.");
        }
        else
        {
            foreach (GeoSource old in geoSources)
                old.Active = false;

            geoSources.Add(new GeoSource
            {
                Url = url,
                Provider = provider,
                ApiKey = geoKeyInput.Text.Trim(),
                Active = true
            });

            geoEditingIndex = geoSources.Count - 1;

            SaveGeoSources();
            RefreshGeoSourceList();

            geoSourceList.SelectedIndex = geoEditingIndex;

            MessageBox.Show("Источник Geo-IP добавлен и активирован.");
        }

        geoAddButton.Text = "Сохранить изменения";
    }

    private void ActivateGeoSource()
    {
        int index = geoSourceList.SelectedIndex;

        if (index < 0 || index >= geoSources.Count)
        {
            MessageBox.Show("Выбери источник в списке.");
            return;
        }

        for (int i = 0; i < geoSources.Count; i++)
            geoSources[i].Active = i == index;

        SaveGeoSources();
        RefreshGeoSourceList();

        geoSourceList.SelectedIndex = index;
    }

    private void RemoveGeoSource()
    {
        int index = geoSourceList.SelectedIndex;

        if (index < 0 || index >= geoSources.Count)
        {
            MessageBox.Show("Выбери источник в списке.");
            return;
        }

        bool wasActive = geoSources[index].Active;

        geoSources.RemoveAt(index);

        if (wasActive && geoSources.Count > 0)
            geoSources[0].Active = true;

        geoEditingIndex = -1;

        SaveGeoSources();
        RefreshGeoSourceList();
        ClearGeoEditor();
    }

    private async Task TestGeoSource()
    {
        int index = geoSourceList.SelectedIndex;

        if (index < 0 || index >= geoSources.Count)
        {
            MessageBox.Show("Выбери источник в списке.");
            return;
        }

        GeoSource source = geoSources[index];

        try
        {
            ProxyRecord sample = new() { ExitIp = "8.8.8.8" };

            await LookupGeoAsync(sample, CancellationToken.None, source);

            MessageBox.Show(
                $"API отвечает.\nСтрана: {sample.Country}\nГород: {sample.City}"
            );
        }
        catch (Exception ex)
        {
            MessageBox.Show("Ошибка теста API:\n" + ex.Message);
        }
    }

    private async Task LookupGeoAsync(
        ProxyRecord record,
        CancellationToken token,
        GeoSource? overrideSource = null)
    {
        GeoSource? source =
            overrideSource ?? geoSources.FirstOrDefault(s => s.Active);

        if (source == null || string.IsNullOrWhiteSpace(record.ExitIp))
            return;

        string url = source.Url.Replace(
            "{ip}",
            Uri.EscapeDataString(record.ExitIp)
        );

        using HttpRequestMessage request = new(HttpMethod.Get, url);

        if (source.Provider == "IPWho.org" &&
            !string.IsNullOrWhiteSpace(source.ApiKey))
        {
            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Bearer",
                    source.ApiKey
                );
        }

        using HttpResponseMessage response =
            await httpClient.SendAsync(request, token);

        response.EnsureSuccessStatusCode();

        string json = await response.Content.ReadAsStringAsync(token);

        using JsonDocument doc = JsonDocument.Parse(json);

        JsonElement root = doc.RootElement;

        if (source.Provider == "ipwho.is")
        {
            if (root.TryGetProperty("success", out JsonElement success) &&
                success.ValueKind == JsonValueKind.False)
            {
                throw new Exception("GeoIP API вернул success=false.");
            }

            record.Country = ReadJsonString(root, "country", "Неизвестно");
            record.City = ReadJsonString(root, "city", "Неизвестно");
            record.IpType = "Неизвестно";
        }
        else
        {
            JsonElement data =
                root.TryGetProperty("data", out JsonElement d) ? d : root;

            if (data.TryGetProperty("geo_location", out JsonElement geo))
            {
                record.Country =
                    ReadJsonString(geo, "country", "Неизвестно");

                record.City =
                    ReadJsonString(geo, "city", "Неизвестно");
            }

            record.IpType = "Неизвестно";
        }
    }

    private string ReadJsonString(
        JsonElement element,
        string property,
        string fallback)
    {
        if (element.TryGetProperty(property, out JsonElement value) &&
            value.ValueKind == JsonValueKind.String)
        {
            return value.GetString() ?? fallback;
        }

        return fallback;
    }

    // =====================================================
    // BROWSER
    // =====================================================

    private Panel BuildBrowserWorkspace()
    {
        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = background
        };

        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 75));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        Panel header = CreateHeader(
            "Тестовый браузер",
            "Отдельное подключение. Системный прокси Windows не меняется."
        );

        FlowLayoutPanel toolbar = new()
        {
            Dock = DockStyle.Fill,
            BackColor = panelColor,
            Padding = new Padding(5),
            WrapContents = false,
            AutoScroll = true
        };

        Button back = CreateButton("←", panelColor);
        back.Size = new Size(38, 32);
        back.Click += (_, _) =>
        {
            if (browser?.CoreWebView2?.CanGoBack == true)
                browser.CoreWebView2.GoBack();
        };

        Button forward = CreateButton("→", panelColor);
        forward.Size = new Size(38, 32);
        forward.Click += (_, _) =>
        {
            if (browser?.CoreWebView2?.CanGoForward == true)
                browser.CoreWebView2.GoForward();
        };

        Button reload = CreateButton("⟳", panelColor);
        reload.Size = new Size(38, 32);
        reload.Click += (_, _) => browser?.CoreWebView2?.Reload();

        Button home = CreateButton("Домой", panelColor);
        home.Size = new Size(65, 32);
        home.Click += (_, _) => NavigateBrowser("https://www.google.com");

        addressBox = CreateTextBox();
        addressBox.Width = 400;
        addressBox.Margin = new Padding(5, 5, 2, 3);
        addressBox.Text = "https://www.google.com";

        addressBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                NavigateBrowser(addressBox.Text);
                e.SuppressKeyPress = true;
            }
        };

        Button go = CreateButton("Перейти", accent);
        go.Size = new Size(85, 32);
        go.Click += (_, _) => NavigateBrowser(addressBox.Text);

        toolbar.Controls.Add(back);
        toolbar.Controls.Add(forward);
        toolbar.Controls.Add(reload);
        toolbar.Controls.Add(home);
        toolbar.Controls.Add(addressBox);
        toolbar.Controls.Add(go);

        browserHost = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = panelColor
        };

        Label placeholder = new()
        {
            Text = "Нажми «Тестовый браузер» для запуска",
            ForeColor = mutedColor,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter
        };

        browserHost.Controls.Add(placeholder);

        layout.Controls.Add(header, 0, 0);
        layout.Controls.Add(toolbar, 0, 1);
        layout.Controls.Add(browserHost, 0, 2);

        Panel view = new() { Dock = DockStyle.Fill, BackColor = background };
        view.Controls.Add(layout);

        return view;
    }

    private async Task EnsureBrowserInitializedAsync()
    {
        if (browser != null || browserInitializing)
            return;

        browserInitializing = true;

        try
        {
            await CreateBrowserAsync(proxyEnabled, selectedProxy);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Не удалось запустить WebView2:\n" + ex.Message +
                "\n\nПроверь установку Microsoft Edge WebView2 Runtime."
            );
        }
        finally
        {
            browserInitializing = false;
        }
    }

    private async Task CreateBrowserAsync(bool useProxy, string? proxyText)
    {
        if (browser != null)
        {
            browserHost.Controls.Remove(browser);
            browser.Dispose();
            browser = null;
        }

        string folder;
        string arguments;

        if (useProxy && !string.IsNullOrWhiteSpace(proxyText))
        {
            if (!TryParseProxy(
                proxyText,
                out string host,
                out int port,
                out _,
                out _))
            {
                throw new Exception("Некорректный адрес выбранного прокси.");
            }

            string hash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(proxyText))
            );

            folder = Path.Combine(dataFolder, "BrowserProxy_" + hash);

            arguments =
                $"--proxy-server=http://{host}:{port} --proxy-bypass-list=<-loopback>";
        }
        else
        {
            folder = Path.Combine(dataFolder, "BrowserDirect");
            arguments = "--no-proxy-server";
        }

        Directory.CreateDirectory(folder);

        CoreWebView2Environment environment =
            await CoreWebView2Environment.CreateAsync(
                null,
                folder,
                new CoreWebView2EnvironmentOptions
                {
                    AdditionalBrowserArguments = arguments
                }
            );

        browser = new WebView2 { Dock = DockStyle.Fill };

        browserHost.Controls.Clear();
        browserHost.Controls.Add(browser);

        await browser.EnsureCoreWebView2Async(environment);

        if (useProxy &&
            !string.IsNullOrWhiteSpace(proxyText) &&
            TryParseProxy(
                proxyText,
                out _,
                out _,
                out string? username,
                out string? password) &&
            !string.IsNullOrEmpty(username))
        {
            browser.CoreWebView2.BasicAuthenticationRequested += (_, e) =>
            {
                e.Response.UserName = username;
                e.Response.Password = password ?? "";
            };
        }

        browser.CoreWebView2.NavigationCompleted += (_, _) =>
        {
            if (browser?.Source != null)
            {
                currentBrowserUrl = browser.Source.ToString();
                addressBox.Text = currentBrowserUrl;
            }
        };

        browser.CoreWebView2.Navigate(currentBrowserUrl);
    }

    private void NavigateBrowser(string address)
    {
        if (browser?.CoreWebView2 == null)
            return;

        string url = address.Trim();

        if (string.IsNullOrWhiteSpace(url))
            return;

        if (!url.Contains("://"))
            url = "https://" + url;

        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != "http" && uri.Scheme != "https"))
        {
            MessageBox.Show("Введи правильный адрес сайта.");
            return;
        }

        currentBrowserUrl = uri.ToString();
        addressBox.Text = currentBrowserUrl;
        browser.CoreWebView2.Navigate(currentBrowserUrl);
    }

    // =====================================================
    // ПРОКСИ ON/OFF
    // =====================================================

    private async Task ToggleProxy()
    {
        if (browserInitializing)
            return;

        proxyToggleButton.Enabled = false;

        try
        {
            if (!proxyEnabled)
            {
                if (string.IsNullOrWhiteSpace(selectedProxy))
                {
                    MessageBox.Show("Сначала выбери прокси на странице LIVE PROXIES.");
                    return;
                }

                proxyToggleButton.Text = "Проверка...";

                string externalIp = await GetExternalIpAsync(
                    selectedProxy,
                    CancellationToken.None
                );

                if (!IPAddress.TryParse(externalIp, out _))
                    throw new Exception("Прокси вернул некорректный IP.");

                await CreateBrowserAsync(true, selectedProxy);

                proxyEnabled = true;
                currentExternalIp = externalIp;

                proxyToggleButton.Text = "Прокси: ВКЛ";
                proxyToggleButton.BackColor = Color.FromArgb(150, 65, 70);

                UpdateIpLabel("Прокси включён");
            }
            else
            {
                proxyToggleButton.Text = "Отключение...";

                string externalIp = await GetExternalIpAsync(
                    null,
                    CancellationToken.None
                );

                await CreateBrowserAsync(false, null);

                proxyEnabled = false;
                currentExternalIp = externalIp;

                proxyToggleButton.Text = "Прокси: ВЫКЛ";
                proxyToggleButton.BackColor = Color.FromArgb(65, 125, 80);

                UpdateIpLabel("Прямое подключение");
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("Не удалось переключить подключение:\n" + ex.Message);

            proxyToggleButton.Text =
                proxyEnabled ? "Прокси: ВКЛ" : "Прокси: ВЫКЛ";
        }
        finally
        {
            proxyToggleButton.Enabled = true;
        }
    }

    // =====================================================
    // HTTP И ВНЕШНИЙ IP
    // =====================================================

    private HttpClient CreateProxyClient(string proxyText, int timeoutSeconds)
    {
        if (!TryParseProxy(
            proxyText,
            out string host,
            out int port,
            out string? username,
            out string? password))
        {
            throw new Exception("Некорректный прокси.");
        }

        WebProxy proxy = new($"http://{host}:{port}");

        if (!string.IsNullOrEmpty(username))
            proxy.Credentials = new NetworkCredential(username, password ?? "");

        HttpClientHandler handler = new()
        {
            Proxy = proxy,
            UseProxy = true
        };

        return new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(timeoutSeconds)
        };
    }

    private async Task<string> GetExternalIpAsync(
        string? proxyText,
        CancellationToken token)
    {
        using HttpClient client = string.IsNullOrWhiteSpace(proxyText)
            ? new HttpClient(new HttpClientHandler { UseProxy = false })
            : CreateProxyClient(proxyText, 15);

        using HttpResponseMessage response =
            await client.GetAsync(TestUrl, token);

        response.EnsureSuccessStatusCode();

        string ip = (await response.Content.ReadAsStringAsync(token)).Trim();

        if (!IPAddress.TryParse(ip, out _))
            throw new Exception("Сервер не вернул корректный IP.");

        return ip;
    }

    private async Task UpdateDirectIpAsync()
    {
        try
        {
            string ip = await GetExternalIpAsync(null, CancellationToken.None);

            if (IsDisposed)
                return;

            currentExternalIp = ip;
            UpdateIpLabel("Прямое подключение");
        }
        catch
        {
            if (!IsDisposed)
                ipStatus.Text = "Внешний IP: не удалось определить";
        }
    }

    private void UpdateIpLabel(string connection)
    {
        if (ipStatus == null)
            return;

        ipStatus.Text = $"IP: {currentExternalIp}   |   {connection}";
    }

    // =====================================================
    // ПАРСИНГ ПРОКСИ
    // =====================================================

    private bool TryParseProxy(
        string value,
        out string host,
        out int port,
        out string? username,
        out string? password)
    {
        host = "";
        port = 0;
        username = null;
        password = null;

        string address = value.Trim();

        address = Regex.Replace(
            address,
            @"^(?:https?|socks4|socks5)://",
            "",
            RegexOptions.IgnoreCase
        );

        if (address.Contains('@'))
        {
            string[] parts = address.Split('@');

            if (parts.Length != 2)
                return false;

            string[] credentials = parts[0].Split(':');

            if (credentials.Length < 2)
                return false;

            username = credentials[0];
            password = string.Join(":", credentials.Skip(1));
            address = parts[1];
        }

        string[] fields = address.Split(':');

        if (fields.Length == 2)
        {
            host = fields[0];

            if (!int.TryParse(fields[1], out port))
                return false;
        }
        else if (fields.Length == 4)
        {
            host = fields[0];

            if (!int.TryParse(fields[1], out port))
                return false;

            username = fields[2];
            password = fields[3];
        }
        else
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(host) &&
               Uri.CheckHostName(host) != UriHostNameType.Unknown &&
               port > 0 && port <= 65535;
    }

    // Восстанавливает Port из Address.
    // Это важно для старых JSON, где Port мог быть равен нулю.
    private void EnsureProxyPort(ProxyRecord record)
    {
        if (record == null || string.IsNullOrWhiteSpace(record.Address))
            return;

        if (record.Port > 0 && record.Port <= 65535)
            return;

        if (TryParseProxy(
            record.Address,
            out _,
            out int parsedPort,
            out _,
            out _))
        {
            record.Port = parsedPort;
        }
    }

    private string CleanProxyLine(string line)
    {
        string value = line.Trim();

        if (value.StartsWith("#") ||
            value.StartsWith("//") ||
            value.Length == 0)
        {
            return "";
        }

        return value;
    }

    private bool IsProxyCandidate(string value)
    {
        string cleaned = Regex.Replace(
            value,
            @"^(?:https?|socks4|socks5)://",
            "",
            RegexOptions.IgnoreCase
        );

        return Regex.IsMatch(
            cleaned,
            @"^(?:[^@\s]+@)?(?:\d{1,3}\.){1,3}\d{1,3}:\d{2,5}(?::[^:\s]+(?::.*)?)?$"
        ) || Regex.IsMatch(
            cleaned,
            @"^(?:[^@\s]+@)?[a-zA-Z0-9.-]+:\d{2,5}(?::[^:\s]+(?::.*)?)?$"
        );
    }

    // =====================================================
    // СОХРАНЕНИЕ И ЗАГРУЗКА
    // =====================================================

    private void LoadSources()
    {
        if (!File.Exists(SourcesFile))
            return;

        foreach (string line in File.ReadAllLines(SourcesFile))
        {
            string url = line.Trim();

            if (Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) &&
                (uri.Scheme == "http" || uri.Scheme == "https"))
            {
                sourceList.Items.Add(url);
            }
        }
    }

    private void SaveSources()
    {
        File.WriteAllLines(SourcesFile, sourceList.Items.Cast<string>());
    }

    private void AddSource()
    {
        string url = sourceInput.Text.Trim();

        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != "http" && uri.Scheme != "https"))
        {
            MessageBox.Show("Введи правильную ссылку http:// или https://");
            return;
        }

        if (sourceList.Items.Contains(url))
        {
            MessageBox.Show("Этот источник уже добавлен.");
            return;
        }

        sourceList.Items.Add(url);
        sourceInput.Clear();
        SaveSources();
    }

    private void RemoveSource()
    {
        if (sourceList.SelectedIndex < 0)
        {
            MessageBox.Show("Сначала выбери источник.");
            return;
        }

        sourceList.Items.RemoveAt(sourceList.SelectedIndex);
        SaveSources();
    }

    private void LoadProxies()
    {
        if (!File.Exists(ProxiesFile))
            return;

        foreach (string line in File.ReadAllLines(ProxiesFile))
        {
            string proxy = line.Trim();

            if (!IsProxyCandidate(proxy))
                continue;

            if (!proxies.TryGetValue(proxy, out ProxyRecord? record))
            {
                record = new ProxyRecord { Address = proxy };
                proxies[proxy] = record;
            }

            EnsureProxyPort(record);
        }
    }

    private void SaveProxies()
    {
        File.WriteAllLines(ProxiesFile, proxies.Keys);
    }

    private void RefreshGrabGrid()
    {
        proxyGrid.Rows.Clear();

        foreach (string proxy in proxies.Keys.OrderBy(x => x))
            proxyGrid.Rows.Add(proxy, "База");

        grabStatus.Text = $"Прокси в базе: {proxies.Count}";
    }

    // Сохраняем только LIVE-прокси в новый файл.
    private void SaveResults()
    {
        try
        {
            List<ProxyRecord> liveRecords = proxies.Values
                .Where(p => p.Status == "LIVE")
                .ToList();

            foreach (ProxyRecord record in liveRecords)
                EnsureProxyPort(record);

            string json = JsonSerializer.Serialize(
                liveRecords,
                new JsonSerializerOptions { WriteIndented = true }
            );

            File.WriteAllText(LiveProxiesFile, json);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Ошибка сохранения результатов:\n" + ex.Message);
        }
    }

    private void LoadResults()
    {
        // Сначала используем новый файл.
        // Если его ещё нет, загружаем старый proxy-results.json.
        string fileToLoad = File.Exists(LiveProxiesFile)
            ? LiveProxiesFile
            : ResultsFile;

        if (!File.Exists(fileToLoad))
            return;

        try
        {
            List<ProxyRecord>? saved =
                JsonSerializer.Deserialize<List<ProxyRecord>>(
                    File.ReadAllText(fileToLoad)
                );

            if (saved == null)
                return;

            foreach (ProxyRecord record in saved)
            {
                if (string.IsNullOrWhiteSpace(record.Address))
                    continue;

                EnsureProxyPort(record);

                if (proxies.TryGetValue(
                    record.Address,
                    out ProxyRecord? existing))
                {
                    existing.Status = record.Status;
                    existing.ExitIp = record.ExitIp;
                    existing.Country = record.Country;
                    existing.City = record.City;
                    existing.Anonymity = record.Anonymity;
                    existing.Protocol = record.Protocol;
                    existing.IpType = record.IpType;
                    existing.SpeedMs = record.SpeedMs;

                    // Порт из JSON сохраняем, если он был заполнен.
                    if (record.Port > 0)
                        existing.Port = record.Port;

                    existing.RequiresAuth = record.RequiresAuth;
                    existing.Favorite = record.Favorite;
                    existing.LastChecked = record.LastChecked;

                    EnsureProxyPort(existing);
                }
                else
                {
                    proxies[record.Address] = record;
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Ошибка загрузки результатов:\n" + ex.Message
            );
        }
    }

    private void SaveGeoSources()
    {
        try
        {
            File.WriteAllText(
                GeoSourcesFile,
                JsonSerializer.Serialize(
                    geoSources,
                    new JsonSerializerOptions { WriteIndented = true }
                )
            );
        }
        catch (Exception ex)
        {
            MessageBox.Show("Ошибка сохранения Geo-IP источников:\n" + ex.Message);
        }
    }

    private void LoadGeoSources()
    {
        if (File.Exists(GeoSourcesFile))
        {
            try
            {
                List<GeoSource>? loaded =
                    JsonSerializer.Deserialize<List<GeoSource>>(
                        File.ReadAllText(GeoSourcesFile)
                    );

                if (loaded != null)
                    geoSources.AddRange(loaded);
            }
            catch
            {
                // Повреждённый файл не должен останавливать запуск.
            }
        }

        // Если источников нет или ни один не активен,
        // активируем первый доступный.
        if (geoSources.Count == 0)
        {
            geoSources.Add(new GeoSource
            {
                Provider = "ipwho.is",
                Url = "https://ipwho.is/{ip}",
                Active = true
            });

            SaveGeoSources();
        }
        else if (!geoSources.Any(s => s.Active))
        {
            geoSources[0].Active = true;
            SaveGeoSources();
        }
        else
        {
            // На случай, если в старом файле активными оказались несколько.
            bool firstFound = false;

            foreach (GeoSource source in geoSources)
            {
                if (source.Active && !firstFound)
                {
                    firstFound = true;
                }
                else
                {
                    source.Active = false;
                }
            }

            SaveGeoSources();
        }
    }

    // =====================================================
    // ВСПОМОГАТЕЛЬНЫЕ ЭЛЕМЕНТЫ
    // =====================================================

    private Panel CreateHeader(string titleText, string descriptionText)
    {
        Panel panel = new()
        {
            Dock = DockStyle.Fill,
            BackColor = background
        };

        Label title = new()
        {
            Text = titleText,
            Font = new Font("Segoe UI", 22F, FontStyle.Bold),
            ForeColor = Color.White,
            Location = new Point(0, 0),
            AutoSize = true
        };

        Label description = new()
        {
            Text = descriptionText,
            ForeColor = mutedColor,
            Location = new Point(0, 47),
            AutoSize = true
        };

        panel.Controls.Add(title);
        panel.Controls.Add(description);

        return panel;
    }

    private Label CreateLabel(
        string text,
        int y,
        bool bold = false,
        int x = 0,
        int? ignored = null)
    {
        return new Label
        {
            Text = text,
            ForeColor = bold ? textColor : mutedColor,
            Font = new Font(
                "Segoe UI",
                9F,
                bold ? FontStyle.Bold : FontStyle.Regular
            ),
            Location = new Point(x, y),
            AutoSize = true
        };
    }

    private TextBox CreateTextBox()
    {
        return new TextBox
        {
            BackColor = Color.FromArgb(39, 45, 59),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Segoe UI", 9F)
        };
    }

    private ComboBox CreateCombo(IEnumerable<string> values)
    {
        ComboBox box = new()
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = Color.FromArgb(39, 45, 59),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9F)
        };

        box.Items.AddRange(values.Cast<object>().ToArray());

        if (box.Items.Count > 0)
            box.SelectedIndex = 0;

        return box;
    }

    private DataGridView CreateGrid()
    {
        DataGridView grid = new()
        {
            Dock = DockStyle.Fill,
            BackgroundColor = panelColor,
            BorderStyle = BorderStyle.None,
            GridColor = Color.FromArgb(48, 55, 70),
            ForeColor = Color.White,
            EnableHeadersVisualStyles = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            ReadOnly = true,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            Font = new Font("Segoe UI", 9F),
            ClipboardCopyMode =
                DataGridViewClipboardCopyMode.EnableWithoutHeaderText
        };

        grid.ColumnHeadersDefaultCellStyle.BackColor =
            Color.FromArgb(39, 45, 59);

        grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;

        grid.ColumnHeadersDefaultCellStyle.Font =
            new Font("Segoe UI", 9F, FontStyle.Bold);

        grid.DefaultCellStyle.BackColor = panelColor;
        grid.DefaultCellStyle.ForeColor = textColor;
        grid.DefaultCellStyle.SelectionBackColor = accent;
        grid.DefaultCellStyle.SelectionForeColor = Color.White;
        grid.RowTemplate.Height = 29;

        return grid;
    }

    private Button CreateButton(string text, Color color)
    {
        return new Button
        {
            Text = text,
            BackColor = color,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false
        };
    }

    // =====================================================
    // МОДЕЛИ ДАННЫХ
    // =====================================================

    private sealed class ProxyRecord
    {
        public string Address { get; set; } = "";
        public string Status { get; set; } = "UNKNOWN";
        public string ExitIp { get; set; } = "";
        public string Country { get; set; } = "Неизвестно";
        public string City { get; set; } = "Неизвестно";
        public string Anonymity { get; set; } = "Не проверено";
        public string Protocol { get; set; } = "HTTP";
        public string IpType { get; set; } = "Неизвестно";
        public int SpeedMs { get; set; }
        public int Port { get; set; }
        public bool RequiresAuth { get; set; }
        public bool Favorite { get; set; }
        public DateTime LastChecked { get; set; }

        public ProxyRecord()
        {
        }

        public void UpdateParsedFields(Func<string, bool> parser)
        {
            // Резерв для расширения модели.
        }
    }

    private sealed class GeoSource
    {
        public string Provider { get; set; } = "ipwho.is";
        public string Url { get; set; } = "https://ipwho.is/{ip}";
        public string ApiKey { get; set; } = "";
        public bool Active { get; set; }
    }

    private sealed class CheckResult
    {
        public int Index { get; }
        public string Status { get; }
        public string Ip { get; }
        public string Ping { get; }

        public CheckResult(int index, string status, string ip, string ping)
        {
            Index = index;
            Status = status;
            Ip = ip;
            Ping = ping;
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        checkCancellation?.Cancel();
        checkCancellation?.Dispose();

        browser?.Dispose();
        httpClient.Dispose();

        base.OnFormClosed(e);
    }
}