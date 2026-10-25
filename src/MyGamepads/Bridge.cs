using System.Runtime.InteropServices;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;

sealed record PadView(int Slot, string Transport, string Status, bool Exclusive, PadState State, long Reports);
sealed class Bridge : IAsyncDisposable
{
    readonly CancellationTokenSource stop = new();
    readonly object gate = new();
    readonly Dictionary<string, Session> sessions = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, int> rememberedSlots = new(StringComparer.OrdinalIgnoreCase);
    readonly ViGEmClient client;
    Task? loop;
    public string Error { get; private set; } = "";
    public Bridge() { client = new ViGEmClient(); }
    public void Start() => loop = Task.Run(Scan);
    public PadView[] Snapshot() { lock (gate) return sessions.Values.Select(s => s.View).OrderBy(s => s.Slot).ToArray(); }
    async Task Scan()
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                var devices = Hid.Enumerate().Where(Profile.Accept).OrderBy(d => d.Path, StringComparer.OrdinalIgnoreCase).ToArray();
                lock (gate)
                {
                    foreach (var s in sessions.Where(k => !devices.Any(d => d.Path.Equals(k.Key, StringComparison.OrdinalIgnoreCase))).Select(k => k.Value)) s.Cancel();
                    foreach (var key in sessions.Where(k => k.Value.Finished).Select(k => k.Key).ToArray()) sessions.Remove(key);
                    foreach (var d in devices)
                    {
                        if (sessions.ContainsKey(d.Path) || sessions.Count >= 4) continue;
                        var used = sessions.Values.Select(s => s.Slot).ToArray();
                        int slot = rememberedSlots.TryGetValue(d.Path, out var old) && !used.Contains(old) ? old : Enumerable.Range(1, 4).First(n => !used.Contains(n));
                        rememberedSlots[d.Path] = slot;
                        var session = new Session(d, slot, client, stop.Token);
                        sessions[d.Path] = session; session.Start();
                    }
                }
                Error = "";
            }
            catch (Exception e) { Error = e.Message; }
            try { await Task.Delay(1000, stop.Token); } catch (OperationCanceledException) { break; }
        }
    }
    public async ValueTask DisposeAsync()
    {
        stop.Cancel();
        if (loop != null) await loop;
        Session[] all;
        lock (gate) { all = sessions.Values.ToArray(); foreach (var s in all) s.Cancel(); }
        await Task.WhenAll(all.Select(s => s.Completion));
        client.Dispose(); stop.Dispose();
    }
    sealed class Session
    {
        readonly Device device;
        readonly ViGEmClient client;
        readonly CancellationTokenSource stop;
        PadView view;
        public int Slot => view.Slot;
        public PadView View => Volatile.Read(ref view);
        public Task Completion { get; private set; } = Task.CompletedTask;
        public bool Finished => Completion.IsCompleted;
        public Session(Device d, int slot, ViGEmClient c, CancellationToken token)
        { device = d; client = c; stop = CancellationTokenSource.CreateLinkedTokenSource(token); view = new(slot, d.Path.Contains("bth", StringComparison.OrdinalIgnoreCase) || d.Length > 64 ? "Bluetooth" : "USB", "Подключение…", false, default, 0); }
        public void Start() => Completion = Task.Run(Run);
        public void Cancel() { try { stop.Cancel(); } catch (ObjectDisposedException) { } }
        async Task Run()
        {
            IXbox360Controller? pad = null;
            try
            {
                // Exclusive access prevents another reader from consuming the physical pad.
                // Fallback stays visible in the UI; it must never be presented as hidden.
                var handle = Hid.CreateFile(device.Path, 0x80000000, 0, 0, 3, 0x40000000, 0);
                bool exclusive = !handle.IsInvalid;
                if (!exclusive) { handle.Dispose(); handle = Hid.CreateFile(device.Path, 0x80000000, 3, 0, 3, 0x40000000, 0); }
                if (handle.IsInvalid) { var code = Marshal.GetLastWin32Error(); handle.Dispose(); throw new IOException($"Нет доступа к геймпаду (Windows {code}). Закройте x360ce / Steam / DS4Windows."); }
                using var stream = new FileStream(handle, FileAccess.Read, device.Length, true);
                var buffer = new byte[device.Length];
                long count = 0;
                while (!stop.IsCancellationRequested)
                {
                    using var watchdog = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                    watchdog.CancelAfter(TimeSpan.FromSeconds(3));
                    int n;
                    try { n = await stream.ReadAsync(buffer, watchdog.Token); }
                    catch (OperationCanceledException) when (!stop.IsCancellationRequested) { throw new IOException("Нет данных 3 секунды. Переподключение…"); }
                    if (n == 0) throw new IOException("Геймпад отключён");
                    if (!Profile.Decode(buffer.AsSpan(0, n), out var state))
                    {
                        view = view with { Status = $"Неизвестный пакет 0x{buffer[0]:X2} ({n} байт)" };
                        if (pad != null) Profile.Submit(pad, default);
                        continue;
                    }
                    if (pad == null)
                    {
                        pad = client.CreateXbox360Controller(); pad.AutoSubmitReport = false; pad.Connect();
                        // The bus compares against a zero-filled cache on a new target.
                        // Prime it with a one-unit axis change so a neutral first packet
                        // replaces the driver's initial USB descriptor state too.
                        for (int prime = 0; prime < 15; prime++)
                        {
                            Profile.Submit(pad, new PadState(0, 0, 0, 1, 0, 0, 0));
                            await Task.Delay(20, stop.Token);
                        }
                    }
                    Profile.Submit(pad, state);
                    view = new(Slot, view.Transport, "Работает", exclusive, state, ++count);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                view = view with { Status = e.Message, State = default }; Log.Write($"Игрок {Slot}: {e.Message}");
                if (pad != null) { try { Profile.Submit(pad, default); pad.Disconnect(); } catch (Exception cleanup) { Log.Write(cleanup.Message); } pad = null; }
                try { await Task.Delay(4000, stop.Token); } catch (OperationCanceledException) { }
            }
            finally { if (pad != null) { try { Profile.Submit(pad, default); pad.Disconnect(); } catch (Exception e) { Log.Write(e.Message); } } stop.Dispose(); }
        }
    }
}
static class Log
{
    static readonly object Gate = new();
    public static string PathName => AppPaths.FilePath("controller.log");
    public static void Write(string text) { try { lock (Gate) { if (File.Exists(PathName) && new FileInfo(PathName).Length > 1_000_000) File.Move(PathName, PathName + ".old", true); File.AppendAllText(PathName, $"{DateTimeOffset.Now:O} {text}{Environment.NewLine}"); } } catch { } }
}
