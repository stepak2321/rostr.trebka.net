using Draw = System.Drawing;
using Forms = System.Windows.Forms;

namespace RobloxLauncher.Core;

/// <summary>One line of the tray menu.</summary>
public sealed record TrayEntry(string Text, Action? Click = null, bool Checked = false,
                               bool Bold = false, bool Header = false, bool Separator = false)
{
    public static TrayEntry Sep() => new("", Separator: true);
}

/// <summary>
/// The Rostr icon in the Windows notification area (system tray). Uses the NotifyIcon that comes with
/// Windows Forms, so no extra package is needed. Left click opens Rostr, right click shows the menu.
/// The menu is rebuilt every time it opens, so it always shows the current state, and it is drawn in
/// the same dark colors as the launcher.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.ContextMenuStrip _menu = new();
    private readonly Func<IEnumerable<TrayEntry>> _build;

    public event Action? OpenRequested;

    public TrayIcon(Func<IEnumerable<TrayEntry>> buildMenu)
    {
        _build = buildMenu;

        _menu.Renderer = new DarkRenderer();
        _menu.BackColor = DarkRenderer.Bg;
        _menu.ForeColor = DarkRenderer.Text;
        _menu.Font = new Draw.Font("Segoe UI", 9.75f);
        _menu.ShowImageMargin = false;
        _menu.ShowCheckMargin = true;
        _menu.Padding = new Forms.Padding(0, 4, 0, 4);
        _menu.Opening += (_, _) => Fill();

        _icon = new Forms.NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "Rostr",
            ContextMenuStrip = _menu,
            Visible = true
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left) OpenRequested?.Invoke();
        };
    }

    private void Fill()
    {
        _menu.Items.Clear();
        IEnumerable<TrayEntry> entries;
        try { entries = _build().ToList(); }
        catch (Exception ex)
        {
            Log.Warn("Tray menu failed: " + ex.Message);
            return;
        }

        foreach (var e in entries)
        {
            if (e.Separator)
            {
                _menu.Items.Add(new Forms.ToolStripSeparator());
                continue;
            }

            if (e.Header)
            {
                _menu.Items.Add(new Forms.ToolStripLabel(e.Text)
                {
                    ForeColor = DarkRenderer.Muted,
                    Padding = new Forms.Padding(14, 6, 14, 6)
                });
                continue;
            }

            var item = new Forms.ToolStripMenuItem(e.Text)
            {
                Checked = e.Checked,
                ForeColor = DarkRenderer.Text,
                Padding = new Forms.Padding(2, 6, 12, 6)
            };
            if (e.Bold) item.Font = new Draw.Font(_menu.Font, Draw.FontStyle.Bold);
            var click = e.Click;
            if (click != null)
                item.Click += (_, _) => System.Windows.Application.Current?.Dispatcher.BeginInvoke(click);
            _menu.Items.Add(item);
        }
    }

    public void ShowBalloon(string title, string text)
    {
        try { _icon.ShowBalloonTip(4000, title, text, Forms.ToolTipIcon.Info); }
        catch { }
    }

    // The exe already carries the Rostr icon (ApplicationIcon in the project file).
    private static Draw.Icon LoadIcon()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe))
            {
                var icon = Draw.Icon.ExtractAssociatedIcon(exe);
                if (icon != null) return icon;
            }
        }
        catch { }
        return Draw.SystemIcons.Application;
    }

    public void Dispose()
    {
        // Hide first, otherwise Windows keeps a dead icon in the tray until the mouse moves over it.
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
    }

    // Draws the menu in the launcher's colors instead of the plain white Windows menu.
    private sealed class DarkRenderer : Forms.ToolStripProfessionalRenderer
    {
        public static readonly Draw.Color Bg = Draw.Color.FromArgb(0x15, 0x18, 0x22);
        public static readonly Draw.Color Hover = Draw.Color.FromArgb(0x23, 0x28, 0x38);
        public static readonly Draw.Color Line = Draw.Color.FromArgb(0x2A, 0x30, 0x40);
        public static readonly Draw.Color Text = Draw.Color.FromArgb(0xE6, 0xE8, 0xEE);
        public static readonly Draw.Color Muted = Draw.Color.FromArgb(0x7C, 0x84, 0x99);
        public static readonly Draw.Color Accent = Draw.Color.FromArgb(0x5B, 0x83, 0xFF);

        public DarkRenderer() : base(new Colors()) { RoundedEdges = false; }

        private sealed class Colors : Forms.ProfessionalColorTable
        {
            public override Draw.Color ToolStripDropDownBackground => Bg;
            public override Draw.Color ImageMarginGradientBegin => Bg;
            public override Draw.Color ImageMarginGradientMiddle => Bg;
            public override Draw.Color ImageMarginGradientEnd => Bg;
            public override Draw.Color MenuBorder => Line;
        }

        protected override void OnRenderToolStripBackground(Forms.ToolStripRenderEventArgs e)
        {
            using var b = new Draw.SolidBrush(Bg);
            e.Graphics.FillRectangle(b, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(Forms.ToolStripRenderEventArgs e)
        {
            using var pen = new Draw.Pen(Line);
            var r = new Draw.Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
            e.Graphics.DrawRectangle(pen, r);
        }

        protected override void OnRenderImageMargin(Forms.ToolStripRenderEventArgs e)
        {
            using var b = new Draw.SolidBrush(Bg);
            e.Graphics.FillRectangle(b, e.AffectedBounds);
        }

        protected override void OnRenderMenuItemBackground(Forms.ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled) return;
            using var b = new Draw.SolidBrush(Hover);
            e.Graphics.FillRectangle(b, new Draw.Rectangle(4, 1, e.Item.Width - 8, e.Item.Height - 2));
        }

        protected override void OnRenderSeparator(Forms.ToolStripSeparatorRenderEventArgs e)
        {
            using var pen = new Draw.Pen(Line);
            var y = e.Item.Height / 2;
            e.Graphics.DrawLine(pen, 10, y, e.Item.Width - 10, y);
        }

        protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? e.Item.ForeColor : Muted;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderItemCheck(Forms.ToolStripItemImageRenderEventArgs e)
        {
            var r = e.ImageRectangle;
            using var pen = new Draw.Pen(Accent, 2f);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            e.Graphics.DrawLines(pen, new[]
            {
                new Draw.PointF(r.Left + 4, r.Top + r.Height / 2f),
                new Draw.PointF(r.Left + r.Width / 2f - 1, r.Bottom - 5),
                new Draw.PointF(r.Right - 4, r.Top + 5)
            });
        }
    }
}
