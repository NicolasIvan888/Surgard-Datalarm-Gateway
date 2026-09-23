using System.Collections;
using System.Reflection;
using System.Text.Json;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        var root = Path.Combine(Path.GetTempPath(), "SurGardMonitorHistory-" + Guid.NewGuid().ToString("N"));
        var logs = Path.Combine(root, "logs");
        var pending = Path.Combine(root, "spool", "pending");
        Directory.CreateDirectory(logs);
        Directory.CreateDirectory(pending);

        try
        {
            var firstReceived = new DateTimeOffset(2026, 8, 11, 10, 0, 0, TimeSpan.Zero);
            var secondReceived = firstReceived.AddMinutes(1);
            var thirdReceived = firstReceived.AddDays(1);
            var pendingOnlyReceived = thirdReceived.AddMinutes(1);
            var blockedReceived = thirdReceived.AddMinutes(2);

            var firstLog = new List<string>
            {
                Event("packet_accepted", Envelope("id-1", firstReceived, "9998E60200041"))
            };
            for (var index = 0; index < 3500; index++)
            {
                firstLog.Add(Event("andromeda_delivery_failed", new
                {
                    messageId = "id-2",
                    error = "offline"
                }));
            }
            firstLog.Add(Event("packet_accepted", Envelope("id-2", secondReceived, "9998E40001001")));
            firstLog.Add(Event("andromeda_acknowledged", new { id = "id-1", contactId = "9998E60200041" }));
            File.WriteAllLines(Path.Combine(logs, "surguard-2026-08-11.jsonl"), firstLog);

            File.WriteAllText(
                Path.Combine(logs, "surguard-2026-08-12.jsonl"),
                Event("packet_accepted", Envelope("id-3", thirdReceived, "9998E60239120")) + Environment.NewLine +
                Event("packet_blocked", Envelope("blocked-1", blockedReceived, "6935E60200039")) + Environment.NewLine);

            File.WriteAllText(
                Path.Combine(pending, "pending-only.json"),
                JsonSerializer.Serialize(Envelope("id-4", pendingOnlyReceived, "9998E60272122")));
            File.WriteAllText(
                Path.Combine(pending, "existing.json"),
                JsonSerializer.Serialize(Envelope("id-2", secondReceived, "9998E40001001")));

            ApplicationConfiguration.Initialize();
            var assembly = Assembly.Load("SurGardReplacement.Monitor");
            var formType = assembly.GetType("SurGardReplacement.Monitor.MainForm", throwOnError: true)!;
            using var form = (Form)Activator.CreateInstance(
                formType,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                args: [root],
                culture: null)!;

            formType.GetMethod("LoadAllEvents", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(form, null);
            var rows = (IList)formType.GetField("_rows", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(form)!;

            Assert(rows.Count == 5, $"Expected 5 rows, found {rows.Count}.");
            var byId = rows.Cast<object>().ToDictionary(
                row => Get(row, "MessageId"),
                row => row,
                StringComparer.OrdinalIgnoreCase);
            Assert(byId.Count == 5, "Message IDs must remain unique.");
            Assert(Get(byId["id-1"], "Delivery") == "Confirmat", "Acknowledged signal was not restored.");
            Assert(Get(byId["id-2"], "Delivery") == "În așteptare", "Pending signal was not marked pending.");
            Assert(Get(byId["id-4"], "ContactId") == "9998E60272122", "Pending-only signal was not loaded.");
            Assert(Get(byId["blocked-1"], "Delivery") == "Blocat", "Blocked signal was not displayed as blocked.");

            form.Show();
            Application.DoEvents();

            var processEvent = formType.GetMethod(
                "ProcessEvent", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var grid = (DataGridView)formType.GetField(
                "_grid", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;

            for (var index = 0; index < 40; index++)
            {
                var received = pendingOnlyReceived.AddSeconds(index + 1);
                processEvent.Invoke(form, [
                    Event("packet_accepted", Envelope(
                        $"live-{index}", received, $"{index:0000}E60200041"))
                ]);
            }
            Application.DoEvents();

            Assert(grid.FirstDisplayedScrollingRowIndex == 0,
                "The live view must follow new signals while positioned at the top.");

            grid.FirstDisplayedScrollingRowIndex = 10;
            var historicalTopId = Get(grid.Rows[10].DataBoundItem!, "MessageId");
            processEvent.Invoke(form, [
                Event("packet_accepted", Envelope(
                    "history-preserve", pendingOnlyReceived.AddMinutes(2), "8888E60200041"))
            ]);
            Application.DoEvents();

            Assert(grid.FirstDisplayedScrollingRowIndex == 11,
                "The historical viewport must not jump to the newest signal.");
            Assert(Get(grid.Rows[11].DataBoundItem!, "MessageId") == historicalTopId,
                "The same historical row must remain at the top after a live insertion.");

            grid.FirstDisplayedScrollingRowIndex = 0;
            processEvent.Invoke(form, [
                Event("packet_accepted", Envelope(
                    "latest-follow", pendingOnlyReceived.AddMinutes(3), "7777E60200041"))
            ]);
            Application.DoEvents();

            Assert(grid.FirstDisplayedScrollingRowIndex == 0,
                "Returning to the top must resume live following.");
            Assert(Get(grid.Rows[0].DataBoundItem!, "MessageId") == "latest-follow",
                "The newest signal must be the first visible row in live-follow mode.");

            grid.CurrentCell = grid.Rows[5].Cells[0];
            grid.Rows[5].Selected = true;
            formType.GetMethod("ClearGridSelection", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(form, null);
            Assert(grid.SelectedCells.Count == 0 && grid.CurrentCell is null,
                "Signal rows must not remain selected or anchor the viewport.");

            Console.WriteLine(
                "Monitor tests passed: history/blocked load, live follow, preserved history viewport, no selection.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static object Envelope(string id, DateTimeOffset receivedAtUtc, string contactId) => new
    {
        id,
        receivedAtUtc,
        protocol = "UDP",
        source = "46.166.60.1:50000",
        contactId,
        packetHex = "00"
    };

    private static string Event(string eventType, object details) => JsonSerializer.Serialize(new
    {
        timestampUtc = DateTimeOffset.UtcNow,
        eventType,
        details
    });

    private static string Get(object instance, string property) =>
        (string)instance.GetType().GetProperty(property)!.GetValue(instance)!;

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
