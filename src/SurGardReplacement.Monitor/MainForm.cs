using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace SurGardReplacement.Monitor;

internal sealed class MainForm : Form
{
    private readonly string _dataDirectory;
    private readonly string _logDirectory;
    private readonly BindingList<SignalRow> _rows = [];
    private readonly Dictionary<string, SignalRow> _rowsById = new(StringComparer.OrdinalIgnoreCase);
    private readonly TableLayoutPanel _layout = new();
    private readonly DataGridView _grid = new();
    private readonly ToolStripStatusLabel _clockLabel = new();
    private readonly ToolStripStatusLabel _serviceLabel = new();
    private readonly ToolStripStatusLabel _receiverLabel = new();
    private readonly ToolStripStatusLabel _andromedaLabel = new();
    private readonly ToolStripStatusLabel _spoolLabel = new();
    private readonly ToolStripStatusLabel _diskLabel = new();
    private readonly System.Windows.Forms.Timer _timer = new();
    private string? _currentLogPath;
    private long _logPosition;
    private long _sequence;

    public MainForm(string dataDirectory)
    {
        _dataDirectory = dataDirectory;
        _logDirectory = Path.Combine(dataDirectory, "logs");

        Text = "SurGard Replacement Monitor 0.3.0";
        MinimumSize = new Size(900, 520);
        Size = new Size(1160, 690);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.White;

        _layout.Dock = DockStyle.Fill;
        _layout.Margin = Padding.Empty;
        _layout.Padding = Padding.Empty;
        _layout.ColumnCount = 1;
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _layout.RowCount = 3;
        _layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        _layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(_layout);

        BuildMenu();
        BuildGrid();
        BuildStatusBar();

        Shown += (_, _) =>
        {
            LoadAllEvents();
            RefreshStatus();
            _timer.Start();
        };

        _timer.Interval = 1000;
        _timer.Tick += (_, _) =>
        {
            ReadNewEvents();
            RefreshStatus();
        };
    }

    private void BuildMenu()
    {
        var menu = new MenuStrip
        {
            BackColor = Color.FromArgb(34, 77, 121),
            ForeColor = Color.White
        };

        var file = new ToolStripMenuItem("Fișier") { ForeColor = Color.White };
        var openLogs = new ToolStripMenuItem("Deschide folderul de loguri");
        openLogs.Click += (_, _) => OpenDirectory(_logDirectory);
        var openSpool = new ToolStripMenuItem("Deschide spool-ul");
        openSpool.Click += (_, _) => OpenDirectory(Path.Combine(_dataDirectory, "spool", "pending"));
        var exit = new ToolStripMenuItem("Ieșire");
        exit.Click += (_, _) => Close();
        file.DropDownItems.AddRange([openLogs, openSpool, new ToolStripSeparator(), exit]);

        var view = new ToolStripMenuItem("Vizualizare") { ForeColor = Color.White };
        var clear = new ToolStripMenuItem("Curăță tabelul local");
        clear.Click += (_, _) =>
        {
            _rows.Clear();
            _rowsById.Clear();
        };
        var refresh = new ToolStripMenuItem("Reîncarcă toate semnalele");
        refresh.Click += (_, _) => LoadAllEvents();
        view.DropDownItems.AddRange([clear, refresh]);

        var tools = new ToolStripMenuItem("Instrumente") { ForeColor = Color.White };
        var blacklist = new ToolStripMenuItem("Blacklist obiecte...");
        blacklist.Click += (_, _) => OpenBlacklistAdministrator();
        tools.DropDownItems.Add(blacklist);

        menu.Items.AddRange([file, view, tools]);
        MainMenuStrip = menu;
        _layout.Controls.Add(menu, 0, 0);
    }

    private void BuildGrid()
    {
        _grid.Dock = DockStyle.Fill;
        _grid.AutoGenerateColumns = false;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToOrderColumns = true;
        _grid.ReadOnly = true;
        _grid.MultiSelect = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.TabStop = false;
        _grid.RowHeadersVisible = false;
        _grid.BackgroundColor = Color.White;
        _grid.BorderStyle = BorderStyle.Fixed3D;
        _grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
        _grid.RowTemplate.Height = 27;
        _grid.ColumnHeadersHeight = 34;
        _grid.EnableHeadersVisualStyles = false;
        _grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(221, 234, 246);
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(11, 37, 69);
        _grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        _grid.DefaultCellStyle.Font = new Font("Segoe UI", 9F);
        _grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(247, 250, 253);

        // The table is a live display, not an editor. A clicked row must not
        // become an anchor that drags the viewport away from the newest signal.
        _grid.CellMouseDown += (_, eventArgs) =>
        {
            if (eventArgs.RowIndex < 0)
                return;

            BeginInvoke(ClearGridSelection);
        };

        AddColumn("Nr.", nameof(SignalRow.Number), 55);
        AddColumn("Protocol", nameof(SignalRow.Protocol), 75);
        AddColumn("Data și ora", nameof(SignalRow.DateAndTime), 155);
        AddColumn("Întârziere", nameof(SignalRow.Delay), 85);
        AddColumn("ID", nameof(SignalRow.Id), 75);
        AddColumn("SurGard Contact-ID", nameof(SignalRow.ContactId), 180);
        AddColumn("Sursa", nameof(SignalRow.Source), 210);
        AddColumn("Andromeda", nameof(SignalRow.Delivery), 125, fill: true);

        _grid.CellFormatting += (_, eventArgs) =>
        {
            if (eventArgs.RowIndex < 0 ||
                _grid.Rows[eventArgs.RowIndex].DataBoundItem is not SignalRow row)
                return;

            var style = _grid.Rows[eventArgs.RowIndex].DefaultCellStyle;
            style.ForeColor = row.Delivery switch
            {
                "Confirmat" => Color.FromArgb(16, 112, 55),
                "Blocat" => Color.FromArgb(176, 35, 35),
                "Eroare" => Color.FromArgb(176, 35, 35),
                _ => Color.FromArgb(122, 90, 0)
            };
        };

        _grid.DataSource = _rows;
        _layout.Controls.Add(_grid, 0, 1);
    }

    private void AddColumn(string header, string property, int width, bool fill = false)
    {
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = header,
            DataPropertyName = property,
            Width = width,
            AutoSizeMode = fill ? DataGridViewAutoSizeColumnMode.Fill : DataGridViewAutoSizeColumnMode.None,
            MinimumWidth = width
        });
    }

    private void BuildStatusBar()
    {
        var status = new StatusStrip
        {
            BackColor = Color.FromArgb(232, 239, 246),
            SizingGrip = true
        };

        _clockLabel.BorderSides = ToolStripStatusLabelBorderSides.Right;
        _serviceLabel.BorderSides = ToolStripStatusLabelBorderSides.Right;
        _receiverLabel.BorderSides = ToolStripStatusLabelBorderSides.Right;
        _andromedaLabel.BorderSides = ToolStripStatusLabelBorderSides.Right;
        _spoolLabel.BorderSides = ToolStripStatusLabelBorderSides.Right;
        _diskLabel.Spring = true;
        _diskLabel.TextAlign = ContentAlignment.MiddleRight;

        status.Items.AddRange([
            _clockLabel,
            _serviceLabel,
            _receiverLabel,
            _andromedaLabel,
            _spoolLabel,
            _diskLabel
        ]);
        _layout.Controls.Add(status, 0, 2);
    }

    private void LoadAllEvents()
    {
        _grid.SuspendLayout();
        _rows.RaiseListChangedEvents = false;
        _rows.Clear();
        _rowsById.Clear();
        _sequence = 0;

        try
        {
            if (Directory.Exists(_logDirectory))
            {
                foreach (var path in Directory
                    .EnumerateFiles(_logDirectory, "surguard-*.jsonl")
                    .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase))
                {
                    ReadHistoricalLog(path);
                }
            }

            LoadPendingMessages();
        }
        finally
        {
            _rows.RaiseListChangedEvents = true;
            _rows.ResetBindings();
            _grid.ResumeLayout();
        }

        ShowLatestSignal();

        _currentLogPath = GetTodayLogPath();
        try
        {
            _logPosition = File.Exists(_currentLogPath)
                ? new FileInfo(_currentLogPath).Length
                : 0;
        }
        catch
        {
            _logPosition = 0;
        }
    }

    private void ReadHistoricalLog(string path)
    {
        try
        {
            foreach (var line in File.ReadLines(path, Encoding.UTF8))
            {
                // Delivery failures can be extremely frequent while Andromeda is
                // offline. They do not create signals and need not be parsed when
                // rebuilding the complete history.
                if (line.Contains("\"eventType\":\"packet_accepted\"", StringComparison.Ordinal) ||
                    line.Contains("\"eventType\":\"packet_blocked\"", StringComparison.Ordinal) ||
                    line.Contains("\"eventType\":\"andromeda_acknowledged\"", StringComparison.Ordinal))
                {
                    ProcessEvent(line);
                }
            }
        }
        catch
        {
            // Continue with the remaining logs. Monitoring must not affect reception.
        }
    }

    private void LoadPendingMessages()
    {
        var pendingDirectory = Path.Combine(_dataDirectory, "spool", "pending");
        if (!Directory.Exists(pendingDirectory))
            return;

        try
        {
            foreach (var path in Directory
                .EnumerateFiles(pendingDirectory, "*.json")
                .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    using var document = JsonDocument.Parse(
                        File.ReadAllText(path, Encoding.UTF8));
                    var envelope = document.RootElement;
                    var id = GetString(envelope, "id", "Id");
                    if (string.IsNullOrWhiteSpace(id))
                        continue;

                    if (_rowsById.TryGetValue(id, out var existing))
                    {
                        existing.Delivery = "În așteptare";
                        existing.Delay = string.Empty;
                        continue;
                    }

                    var row = new SignalRow
                    {
                        Number = ++_sequence,
                        MessageId = id,
                        ReceivedAt = GetDateTime(envelope, "receivedAtUtc", "ReceivedAtUtc")
                            ?? File.GetCreationTimeUtc(path),
                        Protocol = GetString(envelope, "protocol", "Protocol") ?? "?",
                        ContactId = GetString(envelope, "contactId", "ContactId") ?? "?",
                        Source = GetString(envelope, "source", "Source") ?? "necunoscut",
                        Delivery = "În așteptare"
                    };

                    _rowsById[id] = row;
                    _rows.Insert(0, row);
                }
                catch
                {
                    // Skip only the invalid/incomplete spool file.
                }
            }
        }
        catch
        {
            // The service may change the spool while it is being inspected.
        }
    }

    private void ReadNewEvents()
    {
        var path = GetTodayLogPath();
        if (!string.Equals(path, _currentLogPath, StringComparison.OrdinalIgnoreCase))
        {
            LoadAllEvents();
            return;
        }

        if (!File.Exists(path))
            return;

        try
        {
            using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

            if (stream.Length < _logPosition)
                _logPosition = 0;

            stream.Seek(_logPosition, SeekOrigin.Begin);
            using var reader = new StreamReader(stream, Encoding.UTF8, true, leaveOpen: true);
            string? line;
            while ((line = reader.ReadLine()) is not null)
                ProcessEvent(line);
            _logPosition = stream.Length;
        }
        catch
        {
            // The next timer tick retries. Monitoring must not affect the service.
        }
    }

    private void ProcessEvent(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;

        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            var eventType = root.GetProperty("eventType").GetString();
            var timestamp = root.GetProperty("timestampUtc").GetDateTimeOffset();
            var details = root.GetProperty("details");

            if (eventType is "packet_accepted" or "packet_blocked")
            {
                var id = GetString(details, "id", "Id");
                if (string.IsNullOrWhiteSpace(id) || _rowsById.ContainsKey(id))
                    return;

                var receivedAt = GetDateTime(details, "receivedAtUtc", "ReceivedAtUtc") ?? timestamp;
                var row = new SignalRow
                {
                    Number = ++_sequence,
                    MessageId = id,
                    ReceivedAt = receivedAt,
                    Protocol = GetString(details, "protocol", "Protocol") ?? "?",
                    ContactId = GetString(details, "contactId", "ContactId") ?? "?",
                    Source = GetString(details, "source", "Source") ?? "necunoscut"
                };

                if (eventType == "packet_blocked")
                {
                    row.Delivery = "Blocat";
                    row.Delay = "—";
                }

                _rowsById[id] = row;
                InsertLiveRow(row);
            }
            else if (eventType == "andromeda_acknowledged")
            {
                var id = GetString(details, "id", "Id");
                if (id is not null && _rowsById.TryGetValue(id, out var row))
                {
                    row.Delivery = "Confirmat";
                    row.Delay = $"{Math.Max(0, (timestamp - row.ReceivedAt).TotalSeconds):0.0} s";
                }
            }
            else if (eventType == "andromeda_delivery_failed")
            {
                var id = GetString(details, "messageId", "MessageId");
                if (id is not null && _rowsById.TryGetValue(id, out var row))
                    row.Delivery = "Eroare";
            }
        }
        catch
        {
            // Ignore a malformed/incomplete diagnostic line and continue tailing.
        }
    }

    private void InsertLiveRow(SignalRow row)
    {
        var firstDisplayedRow = _grid.FirstDisplayedScrollingRowIndex;
        var followLatest = firstDisplayedRow <= 0;

        _rows.Insert(0, row);

        if (_grid.Rows.Count == 0)
            return;

        try
        {
            // Inserting at index zero shifts every existing row down by one.
            // Follow the new row only when the operator was already at the top;
            // otherwise keep the same historical row at the top of the viewport.
            _grid.FirstDisplayedScrollingRowIndex = followLatest
                ? 0
                : Math.Min(firstDisplayedRow + 1, _grid.Rows.Count - 1);
            ClearGridSelection();
        }
        catch (InvalidOperationException)
        {
            // A binding/layout refresh can briefly make the row unavailable.
            // The next live insertion will restore the requested behavior.
        }
    }

    private void ShowLatestSignal()
    {
        if (_grid.Rows.Count == 0)
            return;

        try
        {
            _grid.FirstDisplayedScrollingRowIndex = 0;
            ClearGridSelection();
        }
        catch (InvalidOperationException)
        {
            // The grid may still be completing its initial binding/layout.
        }
    }

    private void ClearGridSelection()
    {
        _grid.ClearSelection();
        _grid.CurrentCell = null;
    }

    private void RefreshStatus()
    {
        _clockLabel.Text = DateTime.Now.ToString("HH:mm:ss  dd.MM.yyyy");
        var statusPath = Path.Combine(_logDirectory, "status.json");

        if (!File.Exists(statusPath))
        {
            _serviceLabel.Text = "SERVICIU: fără status";
            _serviceLabel.ForeColor = Color.DarkRed;
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(
                File.ReadAllText(statusPath, Encoding.UTF8));
            var root = document.RootElement;
            var generatedAt = root.GetProperty("generatedAtUtc").GetDateTimeOffset();
            var stale = DateTimeOffset.UtcNow - generatedAt > TimeSpan.FromSeconds(30);
            var receiver = root.GetProperty("receiver");
            var andromeda = root.GetProperty("andromeda");
            var spool = root.GetProperty("spool");

            _serviceLabel.Text = stale ? "SERVICIU: status vechi" : "SERVICIU: RUNNING";
            _serviceLabel.ForeColor = stale ? Color.DarkRed : Color.DarkGreen;
            var blocked = receiver.TryGetProperty("blocked", out var blockedValue)
                ? blockedValue.GetInt64()
                : 0;
            _receiverLabel.Text =
                $"UDP: {receiver.GetProperty("udpAccepted").GetInt64()}  TCP: {receiver.GetProperty("tcpAccepted").GetInt64()}  Respins: {receiver.GetProperty("rejected").GetInt64()}  Blocate: {blocked}";
            var pending = spool.GetProperty("pendingMessages").GetInt32();
            var connected = andromeda.GetProperty("connected").GetBoolean();
            var connectionText = connected ? "CONECTAT" : pending == 0 ? "IDLE" : "NECONECTAT";
            _andromedaLabel.Text = $"ANDROMEDA: {connectionText}";
            _andromedaLabel.ForeColor =
                connected ? Color.DarkGreen : pending == 0 ? Color.DarkGoldenrod : Color.DarkRed;
            _spoolLabel.Text = $"PENDING: {pending}";
            _spoolLabel.ForeColor = pending == 0 ? Color.DarkGreen : Color.DarkRed;
            _diskLabel.Text = $"C: liber {GetFreeDiskSpace()}";
        }
        catch
        {
            _serviceLabel.Text = "SERVICIU: status invalid";
            _serviceLabel.ForeColor = Color.DarkRed;
        }
    }

    private string GetTodayLogPath() =>
        Path.Combine(_logDirectory, $"surguard-{DateTime.UtcNow:yyyy-MM-dd}.jsonl");

    private static string? GetString(JsonElement element, string camelCase, string pascalCase)
    {
        if (element.TryGetProperty(camelCase, out var value) ||
            element.TryGetProperty(pascalCase, out value))
            return value.ValueKind == JsonValueKind.Null ? null : value.GetString();
        return null;
    }

    private static DateTimeOffset? GetDateTime(
        JsonElement element, string camelCase, string pascalCase)
    {
        if (element.TryGetProperty(camelCase, out var value) ||
            element.TryGetProperty(pascalCase, out value))
            return value.GetDateTimeOffset();
        return null;
    }

    private static string GetFreeDiskSpace()
    {
        try
        {
            var root = Path.GetPathRoot(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));
            var drive = new DriveInfo(root!);
            return $"{drive.AvailableFreeSpace / 1024d / 1024d / 1024d:0.0} GB";
        }
        catch
        {
            return "?";
        }
    }

    private static void OpenDirectory(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{path}\"",
                UseShellExecute = true
            });
        }
        catch
        {
            // The monitor remains usable if Explorer cannot be opened.
        }
    }

    private void OpenBlacklistAdministrator()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = Environment.ProcessPath
                    ?? Path.Combine(AppContext.BaseDirectory, "SurGardReplacement.Monitor.exe"),
                Arguments = $"--blacklist-admin --data-dir \"{_dataDirectory}\"",
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = AppContext.BaseDirectory
            });
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            // UAC was cancelled by the operator.
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"Nu s-a putut deschide administrarea blacklist-ului.\n\n{exception.Message}",
                "Blacklist obiecte", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
