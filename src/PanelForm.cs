using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TokenMeter
{
    /// <summary>
    /// The panel. Owner-drawn in one pass. API-only: everything shown is a number the usage
    /// endpoint returned (plus the dashed forecast derived from those readings). Without a login
    /// there is nothing to show, so the panel says so.
    ///
    /// Two ways to live on screen: a pop-up that hides when it loses focus (the default), or
    /// pinned - it stays put, can be dragged, remembers where it was, and can collapse to a
    /// one-line mini bar. The height follows the content, so an absent section leaves no gap.
    /// </summary>
    public class PanelForm : Form
    {
        [DllImport("user32.dll")] private static extern bool ReleaseCapture();
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wp, IntPtr lp);
        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HTCAPTION = 2;

        private const string FontName = "Microsoft YaHei UI";
        private const int BaseWidth = 428;
        private const int CompactWidth = 330;
        private const int MinHeight = 240;
        private const int HeaderHeight = 46;
        private const int Pad = 18;

        internal readonly float _s;
        private readonly Font _f9, _f8, _f7, _f11b, _f26b, _f9b;

        private Snapshot _snap;
        private AppConfig _cfg;
        private string _hot;
        private int _w;          // layout width the current paint/measure pass works to
        private bool _glass;     // acrylic backdrop active
        private bool? _glassAsked;   // the setting ApplyGlass last acted on

        private readonly Dictionary<string, Rectangle> _zones = new Dictionary<string, Rectangle>();

        public event EventHandler RefreshRequested;
        public event EventHandler SettingsRequested;
        public event EventHandler LoginRequested;
        /// <summary>Pin / compact / position changed - the owner persists the config.</summary>
        public event EventHandler LayoutChanged;

        public bool SuppressAutoHide { get; set; }

        private bool Pinned { get { return _cfg != null && _cfg.Pinned; } }
        private bool Compact { get { return _cfg != null && _cfg.Pinned && _cfg.Compact; } }

        public PanelForm()
        {
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            BackColor = Theme.Bg;
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            KeyPreview = true;

            _s = Dpi() / 96f;
            ClientSize = new Size(S(BaseWidth), S(486));
            _w = ClientSize.Width;

            _f9 = new Font(FontName, 9f);
            _f8 = new Font(FontName, 8f);
            _f7 = new Font(FontName, 7.5f);
            _f9b = new Font(FontName, 9f, FontStyle.Bold);
            _f11b = new Font(FontName, 11f, FontStyle.Bold);
            _f26b = new Font(FontName, 26f, FontStyle.Bold);
        }

        private static float Dpi()
        {
            try { using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) return g.DpiX; }
            catch (Exception) { return 96f; }
        }

        private int S(int v) { return (int)Math.Round(v * _s); }
        private int Rx { get { return _w - S(Pad); } }
        private int Lx { get { return S(Pad); } }

        // A pinned panel appears without stealing focus from whatever you're typing in.
        protected override bool ShowWithoutActivation { get { return Pinned; } }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Glass.RoundCorners(Handle);
            ApplyGlass();
        }

        /// <summary>Re-read the glass setting (and theme, which tints it) and repaint.</summary>
        public void ApplyGlass()
        {
            if (!IsHandleCreated) return;
            _glassAsked = _cfg != null && _cfg.Glass;
            _glass = Glass.Apply(Handle, _glassAsked.Value, Theme.Dark);
            Theme.SetGlass(_glass);
            Invalidate();
        }

        public void Update(Snapshot snap, AppConfig cfg)
        {
            _snap = snap; _cfg = cfg;
            if (IsHandleCreated)
            {
                BackColor = Theme.Bg;
                if (cfg != null && _glassAsked != cfg.Glass) ApplyGlass();
            }
            Fit();
            Invalidate();
        }

        // ---- size & position ---------------------------------------------------------

        /// <summary>Size the window to its content: measure by painting into a throwaway bitmap.</summary>
        private void Fit()
        {
            _w = S(Compact ? CompactWidth : BaseWidth);
            int h;
            using (var bmp = new Bitmap(1, 1))
            using (Graphics g = Graphics.FromImage(bmp))
                h = PaintAll(g);
            if (!Compact) h = Math.Max(h, S(MinHeight));
            FitTo(new Size(_w, h));
        }

        /// <summary>
        /// Resize, keeping the anchor that matters: a pinned panel keeps its top-left where you
        /// put it; a pop-up keeps its bottom-right against the tray.
        /// </summary>
        private void FitTo(Size sz)
        {
            if (ClientSize == sz) return;
            if (!Visible) { ClientSize = sz; return; }
            Rectangle r = Bounds;
            Point loc = Pinned ? r.Location : new Point(r.Right - sz.Width, r.Bottom - sz.Height);
            SetBounds(loc.X, loc.Y, sz.Width, sz.Height);
            ClampToScreen();
        }

        private void ClampToScreen()
        {
            Rectangle wa = Screen.FromRectangle(Bounds).WorkingArea;
            int x = Math.Max(wa.Left, Math.Min(Left, wa.Right - Width));
            int y = Math.Max(wa.Top, Math.Min(Top, wa.Bottom - Height));
            if (x != Left || y != Top) Location = new Point(x, y);
        }

        private bool SavedPositionVisible()
        {
            if (_cfg == null || _cfg.PanelX == int.MinValue || _cfg.PanelY == int.MinValue) return false;
            var head = new Rectangle(_cfg.PanelX, _cfg.PanelY, Width, S(HeaderHeight));
            foreach (Screen sc in Screen.AllScreens)
                if (sc.WorkingArea.IntersectsWith(head)) return true;
            return false;
        }

        private void SavePosition()
        {
            if (_cfg == null) return;
            _cfg.PanelX = Left; _cfg.PanelY = Top;
        }

        /// <summary>Show at the pinned spot if there is one, otherwise above the tray.</summary>
        public void ShowPanel()
        {
            Fit();
            if (Pinned && SavedPositionVisible())
            {
                Location = new Point(_cfg.PanelX, _cfg.PanelY);
            }
            else
            {
                Rectangle wa = Screen.PrimaryScreen.WorkingArea;
                Left = Math.Max(wa.Left + 8, wa.Right - Width - 12);
                Top = Math.Max(wa.Top + 8, wa.Bottom - Height - 12);
            }
            Show();
            ClampToScreen();
            TopMost = false; TopMost = true;
            if (!Pinned) { Activate(); BringToFront(); }
        }

        // ---- input -------------------------------------------------------------------

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (!SuppressAutoHide && !Pinned) Hide();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape) Hide();
            else if (e.KeyCode == Keys.F5) Raise(RefreshRequested);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            string hit = HitTest(e.Location);
            if (hit != _hot) { _hot = hit; Cursor = hit != null ? Cursors.Hand : Cursors.Default; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hot != null) { _hot = null; Cursor = Cursors.Default; Invalidate(); }
        }

        /// <summary>
        /// Drag by the header (or anywhere on the mini bar). Moving the pop-up means you want it
        /// somewhere specific, so a drag pins it.
        /// </summary>
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || _cfg == null || HitTest(e.Location) != null) return;
            if (!Compact && e.Y > S(HeaderHeight)) return;

            Point before = Location;
            ReleaseCapture();
            SendMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);   // returns when the drag ends
            if (Location == before) return;
            ClampToScreen();
            _cfg.Pinned = true;
            SavePosition();
            Raise(LayoutChanged);
            Invalidate();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            string hit = HitTest(e.Location);
            if (hit == null) return;
            switch (hit)
            {
                case "close": ClosePanel(); break;
                case "pin": TogglePin(); break;
                case "compact": ToggleCompact(); break;
                case "refresh": Raise(RefreshRequested); break;
                case "settings": Raise(SettingsRequested); break;
                case "login": Raise(LoginRequested); break;
            }
        }

        /// <summary>Closing a pinned panel un-pins it: the next tray click brings back the pop-up.</summary>
        private void ClosePanel()
        {
            if (Pinned) { _cfg.Pinned = false; _cfg.Compact = false; Raise(LayoutChanged); }
            Hide();
        }

        private void TogglePin()
        {
            if (_cfg == null) return;
            _cfg.Pinned = !_cfg.Pinned;
            if (_cfg.Pinned) SavePosition(); else _cfg.Compact = false;
            Fit();
            Raise(LayoutChanged);
            Invalidate();
        }

        /// <summary>The mini bar only makes sense on screen for good, so collapsing also pins.</summary>
        private void ToggleCompact()
        {
            if (_cfg == null) return;
            _cfg.Compact = !Compact;
            if (_cfg.Compact) _cfg.Pinned = true;
            _hot = null;
            Fit();
            SavePosition();
            Raise(LayoutChanged);
            Invalidate();
        }

        private void Raise(EventHandler h) { if (h != null) h(this, EventArgs.Empty); }
        private string HitTest(Point p) { foreach (var kv in _zones) if (kv.Value.Contains(p)) return kv.Key; return null; }

        // ---- level colours -----------------------------------------------------------

        private Color LevelText(double pct100)
        {
            if (pct100 >= _cfg.DangerPct * 100) return Theme.DangerText;
            if (pct100 >= _cfg.WarnPct * 100) return Theme.WarnText;
            return Theme.OkText;
        }
        private Color LevelFill(double pct100)
        {
            if (pct100 >= _cfg.DangerPct * 100) return Theme.DangerFill;
            if (pct100 >= _cfg.WarnPct * 100) return Theme.WarnFill;
            return Theme.OkFill;
        }

        /// <summary>
        /// The 5-hour number's colour. It follows the level, but a forecast that runs out before
        /// the reset lifts it to at least amber - the warning should arrive while there's still
        /// time to slow down, not at 70%.
        /// </summary>
        private Color FiveText(Snapshot s)
        {
            if (s.ForecastHits && s.FivePct < _cfg.WarnPct * 100) return Theme.WarnText;
            return LevelText(s.FivePct);
        }

        private static string P(double pct100)
        {
            return double.IsNaN(pct100) ? "—" : ((int)Math.Round(pct100)) + "%";
        }

        /// <summary>The forecast as a sentence ("limit ~10:40, 1h before reset"), or null.</summary>
        public static string Verdict(Snapshot s, bool brief)
        {
            if (s == null || !s.HasForecast || s.FivePct >= 100) return null;
            if (s.ForecastHits)
                return L.F(brief ? "fc.hit.short" : "fc.hit", Fmt.LocalTime(s.ForecastHitUtc),
                           Fmt.Duration(s.FiveResetUtc - s.ForecastHitUtc));
            return L.F(brief ? "fc.ok.short" : "fc.ok", (int)Math.Round(s.ForecastEndPct));
        }

        // ---- paint -------------------------------------------------------------------

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            _w = ClientSize.Width;
            if (_glass)
            {
                // Acrylic shows through wherever the pixels are transparent; a translucent wash of
                // the theme background keeps the text legible over whatever is behind the panel.
                // ClearType can't blend onto a transparent surface, so text goes greyscale here.
                g.Clear(Color.Transparent);
                using (var b = new SolidBrush(Color.FromArgb(Theme.Dark ? 150 : 175, Theme.Bg)))
                    g.FillRectangle(b, ClientRectangle);
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            }
            else
            {
                g.Clear(Theme.Bg);
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            }
            // Windows 11 rounds and outlines the window itself; draw our own frame only before that.
            if (!Glass.HasRoundCorners)
                Theme.StrokeRound(g, new RectangleF(0.5f, 0.5f, ClientSize.Width - 1, ClientSize.Height - 1), S(10), Theme.Border, 1f);
            PaintAll(g);
        }

        /// <summary>Paints everything and returns the bottom of the content (used to size the window).</summary>
        private int PaintAll(Graphics g)
        {
            _zones.Clear();
            if (Compact) return PaintCompact(g);

            int y = S(16);
            y = PaintHeader(g, y);

            if (_snap == null || _cfg == null) { Str(g, L.S("panel.starting"), _f9, Theme.Muted, Lx, y); return y + S(40); }
            if (!_snap.LoggedIn) return PaintLoginNeeded(g, y);
            if (!_snap.HasData) return PaintWaiting(g, y);

            y = PaintFive(g, y);
            y = PaintChart(g, y);
            y = PaintSeven(g, y);
            y = PaintModels(g, y);
            return y + S(8);
        }

        private int PaintHeader(Graphics g, int y)
        {
            using (var b = new SolidBrush(Theme.Accent))
            {
                var sm = g.SmoothingMode; g.SmoothingMode = SmoothingMode.AntiAlias;
                g.FillEllipse(b, Lx, y + S(4), S(9), S(9));
                g.SmoothingMode = sm;
            }
            Str(g, "Claudometer", _f11b, Theme.Text, Lx + S(15), y - S(1));
            float titleW = g.MeasureString("Claudometer", _f11b).Width;
            Str(g, "v" + Updater.Version, _f7, Theme.Faint, Lx + S(15) + titleW + S(4), y + S(5));

            int bx = Rx - S(28);
            bx = IconButton(g, "close", "✕", bx, y - S(3));
            bx = IconButton(g, "settings", "⚙", bx - S(6), y - S(3));
            bx = IconButton(g, "refresh", "↻", bx - S(6), y - S(3));
            bx = IconButton(g, "compact", "–", bx - S(10), y - S(3));
            PinButton(g, bx - S(6), y - S(3));

            y += S(30);
            Line(g, y);
            return y + S(14);
        }

        private int IconButton(Graphics g, string key, string glyph, int x, int y)
        {
            var r = new Rectangle(x, y, S(28), S(26));
            _zones[key] = r;
            if (_hot == key) Theme.FillRound(g, r, S(6), Theme.Hover);
            var fmt = new StringFormat(); fmt.Alignment = StringAlignment.Center; fmt.LineAlignment = StringAlignment.Center;
            using (var br = new SolidBrush(_hot == key ? Theme.Text : Theme.Muted)) g.DrawString(glyph, _f9b, br, r, fmt);
            return x - S(28);
        }

        /// <summary>A drawn push-pin: tilted and hollow when loose, upright and accent-filled when pinned.</summary>
        private int PinButton(Graphics g, int x, int y)
        {
            var r = new Rectangle(x, y, S(28), S(26));
            _zones["pin"] = r;
            bool on = Pinned;
            if (_hot == "pin" || on) Theme.FillRound(g, r, S(6), on ? Theme.Card : Theme.Hover);
            Color c = on ? Theme.Accent : (_hot == "pin" ? Theme.Text : Theme.Muted);

            GraphicsState st = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TranslateTransform(r.X + r.Width / 2f, r.Y + r.Height / 2f);
            if (!on) g.RotateTransform(40);
            float u = _s;
            using (var p = new Pen(c, 1.6f * u))
            using (var b = new SolidBrush(c))
            {
                var head = new RectangleF(-4f * u, -8f * u, 8f * u, 7f * u);
                if (on) g.FillEllipse(b, head); else g.DrawEllipse(p, head);
                g.DrawLine(p, -5.5f * u, -0.5f * u, 5.5f * u, -0.5f * u);   // the collar
                g.DrawLine(p, 0, -0.5f * u, 0, 8f * u);                      // the needle
            }
            g.Restore(st);
            return x - S(28);
        }

        private int PaintLoginNeeded(Graphics g, int y)
        {
            int cx = _w / 2;
            y += S(48);
            CenterStr(g, L.S("login.title"), _f11b, Theme.Text, cx, y); y += S(28);
            CenterStr(g, L.S("login.desc1"), _f8, Theme.Muted, cx, y); y += S(18);
            CenterStr(g, L.S("login.desc2"), _f8, Theme.Muted, cx, y); y += S(30);

            string label = L.S("login.button");
            SizeF m = g.MeasureString(label, _f9b);
            int bw = (int)m.Width + S(40), bh = S(34);
            var r = new Rectangle(cx - bw / 2, y, bw, bh);
            _zones["login"] = r;
            Theme.FillRound(g, r, S(7), Theme.Accent);
            var fmt = new StringFormat(); fmt.Alignment = StringAlignment.Center; fmt.LineAlignment = StringAlignment.Center;
            using (var br = new SolidBrush(Theme.OnAccent)) g.DrawString(label, _f9b, br, r, fmt);
            return r.Bottom + S(40);
        }

        private int PaintWaiting(Graphics g, int y)
        {
            int cx = _w / 2;
            y += S(56);
            string msg = string.IsNullOrEmpty(_snap.ApiStatus) ? L.S("panel.fetching") : _snap.ApiStatus;
            CenterStr(g, msg, _f9, Theme.Muted, cx, y);
            return y + S(60);
        }

        private int PaintFive(Graphics g, int y)
        {
            Snapshot s = _snap;
            Color txt = FiveText(s), fill = LevelFill(s.FivePct);

            // No resets_at (no usage in the last five hours, so no window yet) arrives as MinValue;
            // subtracting the window from that underflows, so only derive the start when it's real.
            string head = L.S("win5.title");
            if (s.FiveResetUtc > DateTime.MinValue)
                head += "  " + Fmt.LocalTime(s.FiveResetUtc - Analytics.Window) + " → " + Fmt.LocalTime(s.FiveResetUtc);
            Str(g, head, _f9b, Theme.Muted, Lx, y + S(12));

            string pct = P(s.FivePct);
            SizeF pm = g.MeasureString(pct, _f26b);
            Str(g, pct, _f26b, txt, Rx - pm.Width, y - S(4));

            y += S(38);
            RoundBar(g, new Rectangle(Lx, y, _w - 2 * Lx, S(9)), s.FivePct / 100.0, fill);
            y += S(19);

            PaintPill(g, y);
            string reset = s.FiveResetUtc == DateTime.MinValue ? L.S("win5.idle")
                : s.FiveResetUtc > s.NowUtc ? L.F("reset.in", Fmt.Duration(s.ToReset))
                : L.S("reset.done");
            Str(g, reset, _f8, Theme.Muted, Lx, y + S(1));
            y += S(24);

            string verdict = Verdict(s, false);
            if (verdict != null)
            {
                Color vc = s.ForecastHits ? Theme.WarnText : Theme.Muted;
                Dot(g, s.ForecastHits ? Theme.WarnFill : Theme.OkFill, Lx + S(1), y + S(5));
                Str(g, verdict, _f8, vc, Lx + S(12), y - S(1));
                y += S(22);
            }
            return y;
        }

        private void PaintPill(Graphics g, int y)
        {
            Snapshot s = _snap;
            int age = (int)(s.NowUtc - s.ApiLiveUtc).TotalSeconds;
            bool fresh = age < 8 * 60;
            string text; Color dot, fg;
            if (fresh) { text = age < 75 ? L.S("pill.now") : L.F("pill.ago", Fmt.Duration(TimeSpan.FromSeconds(age))); dot = Theme.OkFill; fg = Theme.OkText; }
            else { text = L.F("pill.stale", Fmt.Duration(TimeSpan.FromSeconds(age))); dot = Theme.WarnFill; fg = Theme.WarnText; }

            SizeF tm = g.MeasureString(text, _f7);
            int w = S(18) + (int)Math.Ceiling(tm.Width) + S(10), h = S(17);
            var pill = new Rectangle(Rx - w, y - S(1), w, h);
            Theme.FillRound(g, pill, h / 2f, Theme.Card);
            Dot(g, dot, pill.X + S(8), pill.Y + h / 2 - S(3));
            Str(g, text, _f7, fg, pill.X + S(18), pill.Y + (h - tm.Height) / 2f);
        }

        private int PaintChart(Graphics g, int y)
        {
            Str(g, L.S("chart.title"), _f9b, Theme.Text, Lx, y);
            y += S(20);
            var area = new Rectangle(Lx, y, _w - 2 * Lx, S(146));
            ProjectionRenderer.Draw(g, area, _snap, _f7, _s);
            y += area.Height + S(6);

            float x = Lx;
            x = Swatch(g, x, y, ProjectionRenderer.ActualColor(_snap), L.S("legend.actual"), false);
            x = Swatch(g, x + S(8), y, ProjectionRenderer.ActualColor(_snap), L.S("legend.forecast"), true);
            x = Swatch(g, x + S(8), y, ProjectionRenderer.PaceColor, L.S("legend.pace"), false);
            Swatch(g, x + S(8), y, ProjectionRenderer.CeilingColor, L.S("legend.ceiling"), true);

            y += S(18);
            Line(g, y);
            return y + S(14);
        }

        private int PaintSeven(Graphics g, int y)
        {
            Snapshot s = _snap;
            Color txt = LevelText(s.SevenPct), fill = LevelFill(s.SevenPct);
            string head = L.S("win7.title");
            if (s.SevenResetUtc > DateTime.MinValue) head += "  " + L.F("reset.at", Fmt.LocalDate(s.SevenResetUtc, "ddd HH:mm"));
            Str(g, head, _f9b, Theme.Muted, Lx, y);
            string pct = P(s.SevenPct);
            SizeF pm = g.MeasureString(pct, _f9b);
            Str(g, pct, _f9b, txt, Rx - pm.Width, y);
            y += S(20);

            var bar = new Rectangle(Lx, y, _w - 2 * Lx, S(7));
            RoundBar(g, bar, s.SevenPct / 100.0, fill);
            bool pace = !double.IsNaN(s.SevenPacePct);
            if (pace)
            {
                // Where an even week would be by now: fill left of the tick = ahead of budget.
                float tx = bar.X + (float)(bar.Width * Math.Min(100, s.SevenPacePct) / 100.0);
                using (var p = new Pen(Theme.Muted, 2f * _s)) g.DrawLine(p, tx, bar.Y - S(3), tx, bar.Bottom + S(3));
            }
            y += S(14);
            if (s.SevenResetUtc > s.NowUtc)
            {
                string line = L.F("reset.in", Fmt.Duration(s.ToWeekReset));
                if (pace) line += "  ·  " + L.F("win7.pace", (int)Math.Round(s.SevenPacePct));
                Str(g, line, _f8, Theme.Muted, Lx, y);
            }
            return y + S(20);
        }

        /// <summary>Per-model weekly rows - only when the API returned them; otherwise nothing at all.</summary>
        private int PaintModels(Graphics g, int y)
        {
            Snapshot s = _snap;
            bool hasOpus = !double.IsNaN(s.OpusPct), hasSonnet = !double.IsNaN(s.SonnetPct);
            if (!hasOpus && !hasSonnet) return y;
            Line(g, y);
            y += S(14);
            Str(g, L.S("models.title"), _f9b, Theme.Text, Lx, y);
            y += S(22);
            if (hasOpus) y = ModelRow(g, "Opus", s.OpusPct, y);
            if (hasSonnet) y = ModelRow(g, "Sonnet", s.SonnetPct, y);
            return y;
        }

        private int ModelRow(Graphics g, string name, double pct100, int y)
        {
            Str(g, name, _f8, Theme.Text, Lx, y);
            string amt = P(pct100);
            SizeF am = g.MeasureString(amt, _f8);
            Str(g, amt, _f8, Theme.Muted, Rx - am.Width, y);
            var track = new Rectangle(Lx + S(70), y + S(6), _w - Lx - S(70) - Lx - (int)am.Width - S(10), S(5));
            if (track.Width > S(10)) RoundBar(g, track, pct100 / 100.0, LevelFill(pct100));
            return y + S(20);
        }

        /// <summary>
        /// The mini bar: the 5-hour number and countdown, the forecast in brief, the week, and a
        /// thin 5-hour bar. Drag it anywhere; the expand button brings the full panel back.
        /// </summary>
        private int PaintCompact(Graphics g)
        {
            int y = S(10);
            int bx = Rx - S(22);
            bx = IconButton(g, "close", "✕", bx, y - S(3));
            IconButton(g, "compact", "□", bx - S(2), y - S(3));
            int textRight = bx - S(6);

            Snapshot s = _snap;
            if (s == null || _cfg == null || !s.LoggedIn || !s.HasData)
            {
                string msg = s == null || _cfg == null ? L.S("panel.starting")
                           : !s.LoggedIn ? L.S("chart.loginfirst")
                           : string.IsNullOrEmpty(s.ApiStatus) ? L.S("panel.fetching") : s.ApiStatus;
                Str(g, msg, _f9, Theme.Muted, Lx, y + S(1));
                return y + S(32);
            }

            string pct = P(s.FivePct);
            Str(g, pct, _f11b, FiveText(s), Lx, y - S(1));
            float px = Lx + g.MeasureString(pct, _f11b).Width + S(4);
            string sub = L.S("win5.short");
            if (s.FiveResetUtc > s.NowUtc) sub += " · " + L.F("reset.in", Fmt.Duration(s.ToReset));
            StrClip(g, sub, _f8, Theme.Muted, px, y + S(3), textRight - px);

            y += S(26);
            string week = L.S("win7.short") + " " + P(s.SevenPct);
            SizeF wm = g.MeasureString(week, _f8);
            Str(g, week, _f8, LevelText(s.SevenPct), Rx - wm.Width, y);
            string verdict = Verdict(s, true);
            if (verdict != null)
            {
                Dot(g, s.ForecastHits ? Theme.WarnFill : Theme.OkFill, Lx + S(1), y + S(5));
                StrClip(g, verdict, _f8, s.ForecastHits ? Theme.WarnText : Theme.Muted,
                        Lx + S(12), y, Rx - wm.Width - S(8) - (Lx + S(12)));
            }

            y += S(22);
            RoundBar(g, new Rectangle(Lx, y, _w - 2 * Lx, S(4)), s.FivePct / 100.0, LevelFill(s.FivePct));
            return y + S(4) + S(12);
        }

        // ---- primitives --------------------------------------------------------------

        private void Str(Graphics g, string s, Font f, Color c, float x, float y)
        { using (var b = new SolidBrush(c)) g.DrawString(s, f, b, x, y); }

        /// <summary>Single-line text cut with an ellipsis to fit <paramref name="maxW"/>.</summary>
        private void StrClip(Graphics g, string s, Font f, Color c, float x, float y, float maxW)
        {
            if (maxW <= 0) return;
            var fmt = new StringFormat(StringFormatFlags.NoWrap) { Trimming = StringTrimming.EllipsisCharacter };
            using (var b = new SolidBrush(c))
                g.DrawString(s, f, b, new RectangleF(x, y, maxW, f.GetHeight(g) + 2), fmt);
        }

        private void CenterStr(Graphics g, string s, Font f, Color c, int cx, float y)
        { SizeF m = g.MeasureString(s, f); Str(g, s, f, c, cx - m.Width / 2f, y); }

        private void Line(Graphics g, int y)
        { using (var p = new Pen(Theme.Divider)) g.DrawLine(p, Lx, y, Rx, y); }

        private void Dot(Graphics g, Color c, float x, float y)
        {
            var sm = g.SmoothingMode; g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(c)) g.FillEllipse(b, x, y, S(6), S(6));
            g.SmoothingMode = sm;
        }

        private float Swatch(Graphics g, float x, int y, Color c, string label, bool dashed)
        {
            var sm = g.SmoothingMode; g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var p = new Pen(c, 2.4f * _s)) { if (dashed) p.DashStyle = DashStyle.Dash; g.DrawLine(p, x, y + S(7), x + S(13), y + S(7)); }
            g.SmoothingMode = sm;
            Str(g, label, _f7, Theme.Muted, x + S(15), y);
            return x + S(15) + g.MeasureString(label, _f7).Width;
        }

        private void RoundBar(Graphics g, Rectangle r, double frac, Color fill)
        {
            float rad = r.Height / 2f;
            Theme.FillRound(g, r, rad, Theme.Track);
            if (frac <= 0 || double.IsNaN(frac)) return;
            double f = Math.Min(1.0, frac);
            int w = (int)Math.Round(r.Width * f);
            if (w < r.Height) w = r.Height;
            Theme.FillRound(g, new Rectangle(r.X, r.Y, w, r.Height), rad, fill);
            if (frac > 1.0)
                using (var hb = new HatchBrush(HatchStyle.WideUpwardDiagonal, Theme.WarnFill, fill))
                using (var path = Theme.RoundRect(r, rad))
                { var sm = g.SmoothingMode; g.SmoothingMode = SmoothingMode.AntiAlias; g.FillPath(hb, path); g.SmoothingMode = sm; }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _f7.Dispose(); _f8.Dispose(); _f9.Dispose(); _f9b.Dispose(); _f11b.Dispose(); _f26b.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
