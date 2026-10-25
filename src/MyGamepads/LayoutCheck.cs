using System.Text.Json;

// Geometry checks use isolated forms without opening HID or virtual controllers.
static class LayoutCheck
{
    public static void Run()
    {
        var checks = new List<object>();
        var errors = new List<string>();
        foreach (int count in new[] { 0, 2, 4 })
            foreach (int width in new[] { 720, 800, 1100 })
            {
                using var form = new MainForm(false) { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-30000, -30000) };
                form.Show();
                float scale = form.DeviceDpi / 96f;
                form.ClientSize = new Size((int)(width * scale), (int)(545 * scale));
                form.ShowPads(Enumerable.Range(1, count).Select(i => new PadView(i, "Bluetooth", i == 4 ? "Нет данных 3 секунды. Переподключение…" : "Работает", false, new PadState(0xffff, 255, 255, -32768, 32767, 32767, -32768), 123)).ToArray());
                Application.DoEvents(); form.PerformLayout(); Application.DoEvents();
                void Inspect(Control parent)
                {
                    foreach (Control c in parent.Controls)
                    {
                        if (c.Visible && c.Right > parent.ClientSize.Width - parent.Padding.Right + 2)
                            errors.Add($"{count} pads, width {width}: horizontal overflow {c.GetType().Name} {c.Text} ({c.Right} > {parent.ClientSize.Width - parent.Padding.Right})");
                        if (c is Label label && label.Visible)
                        {
                            var preferred = label.GetPreferredSize(new Size(label.Width, 0));
                            if (label.Height < preferred.Height) errors.Add($"{count} pads, width {width}, DPI {form.DeviceDpi}: clipped label {label.Text} ({label.Size}, required {preferred})");
                        }
                        var siblings = parent.Controls.Cast<Control>().Where(x => x.Visible && x != c);
                        if (c is Label or Button)
                            foreach (var other in siblings.Where(x => x is Label or Button))
                                if (c.Bounds.IntersectsWith(other.Bounds)) errors.Add($"Overlapping {c.Text} / {other.Text}");
                        Inspect(c);
                    }
                }
                Inspect(form);
                checks.Add(new { Count = count, LogicalWidth = width, Dpi = form.DeviceDpi, Errors = errors.Count });
            }
        File.WriteAllText(AppPaths.FilePath("layout-check.json"), JsonSerializer.Serialize(new { Checks = checks, Errors = errors }, new JsonSerializerOptions { WriteIndented = true }));
        Environment.ExitCode = errors.Count == 0 ? 0 : 1;
    }
}

