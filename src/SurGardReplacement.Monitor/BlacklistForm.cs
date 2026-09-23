using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SurGardReplacement.Monitor;

internal sealed class BlacklistForm : Form
{
    private static readonly Regex ObjectIdPattern = new(
        "^[0-9A-F]{4}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _path;
    private readonly BindingList<BlacklistRow> _rows = [];
    private readonly DataGridView _grid = new();
    private readonly TextBox _objectId = new() { CharacterCasing = CharacterCasing.Upper, MaxLength = 4, Width = 90 };
    private readonly TextBox _reason = new() { Width = 330 };

    public BlacklistForm(string dataDirectory)
    {
        _path = Path.Combine(dataDirectory, "blacklist.json");
        Text = "Blacklist obiecte — SurGard Replacement";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(720, 430);
        Size = new Size(820, 520);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            ColumnCount = 1,
            RowCount = 4
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var warning = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(760, 0),
            ForeColor = Color.DarkRed,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            Text = "Atenție: toate evenimentele obiectelor din această listă sunt confirmate către emițător, dar NU sunt transmise către Andromeda."
        };

        var editor = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(0, 10, 0, 8) };
        editor.Controls.AddRange([
            new Label { Text = "Cod obiect (4 caractere):", AutoSize = true, Margin = new Padding(0, 6, 6, 0) },
            _objectId,
            new Label { Text = "Motiv:", AutoSize = true, Margin = new Padding(14, 6, 6, 0) },
            _reason
        ]);
        var add = new Button { Text = "Adaugă", AutoSize = true, Margin = new Padding(10, 0, 0, 0) };
        add.Click += (_, _) => AddObject();
        editor.Controls.Add(add);

        _grid.Dock = DockStyle.Fill;
        _grid.AutoGenerateColumns = false;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.ReadOnly = true;
        _grid.MultiSelect = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.DataSource = _rows;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Cod obiect", DataPropertyName = nameof(BlacklistRow.ObjectId), Width = 120 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Motiv", DataPropertyName = nameof(BlacklistRow.Reason), AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Adăugat", DataPropertyName = nameof(BlacklistRow.AddedAt), Width = 155 });

        var actions = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
        var close = new Button { Text = "Închide", AutoSize = true };
        close.Click += (_, _) => Close();
        var remove = new Button { Text = "Elimină obiectul selectat", AutoSize = true };
        remove.Click += (_, _) => RemoveSelected();
        actions.Controls.AddRange([close, remove]);

        layout.Controls.Add(warning, 0, 0);
        layout.Controls.Add(editor, 0, 1);
        layout.Controls.Add(_grid, 0, 2);
        layout.Controls.Add(actions, 0, 3);
        Controls.Add(layout);

        LoadEntries();
    }

    private void LoadEntries()
    {
        _rows.Clear();
        if (!File.Exists(_path))
            return;

        try
        {
            var document = JsonSerializer.Deserialize<BlacklistDocument>(
                File.ReadAllText(_path, Encoding.UTF8), JsonOptions)
                ?? throw new InvalidDataException("Fișierul este gol.");
            if (document.Version != 1)
                throw new InvalidDataException($"Versiune necunoscută: {document.Version}.");

            foreach (var entry in (document.Objects ?? []).OrderBy(item => item.ObjectId))
                _rows.Add(new BlacklistRow(entry.ObjectId, entry.Reason ?? string.Empty, entry.AddedAtUtc));
        }
        catch (Exception exception)
        {
            MessageBox.Show($"Blacklist-ul nu poate fi citit. Nu a fost modificat.\n\n{exception.Message}",
                "Blacklist invalid", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void AddObject()
    {
        var objectId = _objectId.Text.Trim().ToUpperInvariant();
        if (!ObjectIdPattern.IsMatch(objectId))
        {
            MessageBox.Show("Introduceți exact 4 caractere: cifre sau literele A–F.",
                "Cod obiect invalid", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (_rows.Any(row => string.Equals(row.ObjectId, objectId, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show($"Obiectul {objectId} este deja în blacklist.",
                "Blacklist obiecte", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (MessageBox.Show(
                $"Blocați TOATE evenimentele obiectului {objectId}?\n\nEmițătorul va primi confirmare, dar evenimentele nu vor ajunge în Andromeda.",
                "Confirmare blocare", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;

        _rows.Add(new BlacklistRow(objectId, _reason.Text.Trim(), DateTimeOffset.UtcNow));
        if (!SaveEntries())
        {
            _rows.RemoveAt(_rows.Count - 1);
            return;
        }

        _objectId.Clear();
        _reason.Clear();
    }

    private void RemoveSelected()
    {
        if (_grid.CurrentRow?.DataBoundItem is not BlacklistRow selected)
            return;

        if (MessageBox.Show(
                $"Eliminați obiectul {selected.ObjectId} din blacklist?\n\nEvenimentele lui vor fi din nou transmise către Andromeda.",
                "Confirmare eliminare", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;

        var index = _rows.IndexOf(selected);
        _rows.Remove(selected);
        if (!SaveEntries())
            _rows.Insert(Math.Max(0, index), selected);
    }

    private bool SaveEntries()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var document = new BlacklistDocument(1, _rows
                .OrderBy(row => row.ObjectId, StringComparer.OrdinalIgnoreCase)
                .Select(row => new BlacklistEntry(row.ObjectId, row.Reason, row.AddedAtUtc))
                .ToList());
            var temporaryPath = _path + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(document, JsonOptions), new UTF8Encoding(false));
            File.Move(temporaryPath, _path, overwrite: true);
            return true;
        }
        catch (Exception exception)
        {
            MessageBox.Show($"Blacklist-ul nu a putut fi salvat.\n\n{exception.Message}",
                "Eroare la salvare", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    private sealed record BlacklistDocument(int Version, List<BlacklistEntry>? Objects);
    private sealed record BlacklistEntry(string ObjectId, string? Reason, DateTimeOffset? AddedAtUtc);
    private sealed record BlacklistRow(string ObjectId, string Reason, DateTimeOffset? AddedAtUtc)
    {
        public string AddedAt => AddedAtUtc?.ToLocalTime().ToString("dd.MM.yyyy HH:mm") ?? string.Empty;
    }
}
