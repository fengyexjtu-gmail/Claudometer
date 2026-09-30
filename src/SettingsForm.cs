using System;
using System.Drawing;
using System.Windows.Forms;

namespace TokenMeter
{
    public class SettingsForm : Form
    {
        private static Color Bg { get { return Theme.Bg; } }
        private static Color Fg { get { return Theme.Text; } }
        private static Color Muted { get { return Theme.Muted; } }
        private static Color Field { get { return Theme.Card; } }
        private const string FontName = "Microsoft YaHei UI";

        private readonly AppConfig _cfg;

        private NumericUpDown _warn, _danger, _refresh;
        private CheckBox _notify, _autostart, _autoupdate, _glass;
        private ComboBox _tz, _theme, _lang;

        public SettingsForm(AppConfig cfg)
        {
            _cfg = cfg;
            Build();
        }

        private void Build()
        {
            Text = L.S("settings.title");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Bg;
            ForeColor = Fg;
            Font = new Font(FontName, 9f);
            ClientSize = new Size(470, 456);

            int y = 14;
            AddNote(L.S("settings.note"), ref y);
            y += 6;

            _lang = Combo(L.S("settings.lang"), ref y);
            foreach (string n in L.Names) _lang.Items.Add(n);
            _lang.SelectedIndex = L.IndexOf(_cfg.Language);

            _tz = Combo(L.S("settings.tz"), ref y);
            _tz.DropDownWidth = 400;   // Windows' zone names are long: "(UTC+08:00) Beijing, Chongqing, ..."
            FillZones(false);
            _tz.SelectedIndexChanged += delegate
            {
                var it = _tz.SelectedItem as ZoneItem;
                if (it != null && it.Id == null)
                    BeginInvoke((MethodInvoker)delegate { FillZones(true); _tz.DroppedDown = true; });
            };

            _theme = Combo(L.S("settings.theme"), ref y);
            _theme.Items.Add(L.S("settings.theme.light"));
            _theme.Items.Add(L.S("settings.theme.dark"));
            _theme.SelectedIndex = string.Equals(_cfg.ThemeMode, "dark", StringComparison.OrdinalIgnoreCase) ? 1 : 0;

            _warn = AddNum(L.S("settings.warn"), (decimal)Math.Round(_cfg.WarnPct * 100), 5, 98, 5, ref y);
            _danger = AddNum(L.S("settings.danger"), (decimal)Math.Round(_cfg.DangerPct * 100), 10, 99, 5, ref y);
            _refresh = AddNum(L.S("settings.poll"), _cfg.PollSeconds, 60, 900, 30, ref y);

            y += 6;
            _notify = AddCheck(L.S("settings.notify"), _cfg.Notify, ref y);
            _autoupdate = AddCheck(L.S("settings.autoupdate"), _cfg.AutoUpdate, ref y);
            _autostart = AddCheck(L.S("settings.autostart"), Autostart.IsEnabled(), ref y);
            _glass = AddCheck(L.S("settings.glass"), _cfg.Glass && Glass.Supported, ref y);
            _glass.Enabled = Glass.Supported;   // needs the Windows 11 22H2 backdrop API

            y += 10;
            var ok = new Button();
            ok.Text = L.S("settings.save");
            ok.SetBounds(ClientSize.Width - 190, y, 84, 28);
            ok.FlatStyle = FlatStyle.Flat;
            ok.BackColor = Theme.Accent;
            ok.ForeColor = Fg;
            ok.FlatAppearance.BorderColor = Theme.Accent;
            ok.Click += OnSave;
            Controls.Add(ok);

            var cancel = new Button();
            cancel.Text = L.S("settings.cancel");
            cancel.SetBounds(ClientSize.Width - 98, y, 84, 28);
            cancel.FlatStyle = FlatStyle.Flat;
            cancel.BackColor = Field;
            cancel.ForeColor = Fg;
            cancel.FlatAppearance.BorderColor = Theme.Border;
            cancel.DialogResult = DialogResult.Cancel;
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;
        }

        /// <summary>A zone in the dropdown: shown by Windows' localized name, stored by its id.</summary>
        private class ZoneItem
        {
            public string Id;     // null = the "More time zones..." entry
            public string Text;
            public override string ToString() { return Text; }
        }

        /// <summary>
        /// "Follow system", then the common zones (or every zone), then "More..." while the list is
        /// short. The saved zone is always listed so the selection never silently changes.
        /// </summary>
        private void FillZones(bool all)
        {
            var cur = _tz.SelectedItem as ZoneItem;
            string want = cur != null && cur.Id != null ? cur.Id : _cfg.TimeZoneId;
            if (string.IsNullOrEmpty(want)) want = Tz.DefaultId;

            _tz.BeginUpdate();
            _tz.Items.Clear();
            _tz.Items.Add(new ZoneItem { Id = Tz.LocalId, Text = L.F("settings.tz.local", Tz.Offset(TimeZoneInfo.Local)) });
            bool listed = want == Tz.LocalId;
            foreach (TimeZoneInfo z in TimeZoneInfo.GetSystemTimeZones())   // already ordered by offset
            {
                if (!all && Array.IndexOf(Tz.CommonIds, z.Id) < 0 && z.Id != want) continue;
                _tz.Items.Add(new ZoneItem { Id = z.Id, Text = z.DisplayName });
                if (z.Id == want) listed = true;
            }
            if (!all) _tz.Items.Add(new ZoneItem { Id = null, Text = L.S("settings.tz.more") });
            if (!listed) want = Tz.LocalId;
            foreach (ZoneItem it in _tz.Items)
                if (it.Id == want) { _tz.SelectedItem = it; break; }
            _tz.EndUpdate();
        }

        private ComboBox Combo(string label, ref int y)
        {
            var l = new Label();
            l.Text = label;
            l.SetBounds(16, y + 4, 210, 22);
            l.ForeColor = Fg;
            Controls.Add(l);
            var c = new ComboBox();
            c.DropDownStyle = ComboBoxStyle.DropDownList;
            c.SetBounds(240, y, 210, 24);
            c.BackColor = Field;
            c.ForeColor = Fg;
            Controls.Add(c);
            y += 32;
            return c;
        }

        private void AddNote(string s, ref int y)
        {
            var l = new Label();
            l.Text = s;
            l.SetBounds(16, y, ClientSize.Width - 24, 68);
            l.ForeColor = Muted;
            l.UseMnemonic = false;   // "/usage" etc. must not be read as an accelerator
            Controls.Add(l);
            y += 60;
        }

        private void Style(Button b, Color back)
        {
            b.FlatStyle = FlatStyle.Flat;
            b.BackColor = back;
            b.ForeColor = b.Enabled ? Fg : Muted;
            b.FlatAppearance.BorderColor = Theme.Border;
        }

        private NumericUpDown AddNum(string label, decimal value, decimal min, decimal max,
                                     decimal step, ref int y)
        {
            var l = new Label();
            l.Text = label;
            l.SetBounds(16, y + 4, 210, 22);
            l.ForeColor = Fg;
            Controls.Add(l);

            var n = new NumericUpDown();
            n.SetBounds(240, y, 100, 24);
            n.Minimum = min;
            n.Maximum = max;
            n.Increment = step;
            n.Value = Math.Min(max, Math.Max(min, value));
            n.BackColor = Field;
            n.ForeColor = Fg;
            n.BorderStyle = BorderStyle.FixedSingle;
            Controls.Add(n);

            y += 32;
            return n;
        }

        private CheckBox AddCheck(string label, bool value, ref int y)
        {
            var c = new CheckBox();
            c.Text = label;
            c.SetBounds(16, y, ClientSize.Width - 32, 24);
            c.Checked = value;
            c.ForeColor = Fg;
            Controls.Add(c);
            y += 26;
            return c;
        }

        private static decimal Clamp(NumericUpDown n, decimal v)
        {
            return Math.Min(n.Maximum, Math.Max(n.Minimum, v));
        }

        private void OnSave(object sender, EventArgs e)
        {
            _cfg.WarnPct = (double)_warn.Value / 100.0;
            _cfg.DangerPct = (double)_danger.Value / 100.0;
            _cfg.PollSeconds = (int)_refresh.Value;
            _cfg.Notify = _notify.Checked;
            _cfg.AutoUpdate = _autoupdate.Checked;
            if (_lang.SelectedIndex >= 0) _cfg.Language = L.Codes[_lang.SelectedIndex];
            var zone = _tz.SelectedItem as ZoneItem;
            if (zone != null && zone.Id != null) _cfg.TimeZoneId = zone.Id;
            _cfg.ThemeMode = _theme.SelectedIndex == 1 ? "dark" : "light";
            if (_glass.Enabled) _cfg.Glass = _glass.Checked;
            try { _cfg.Save(); }
            catch (Exception ex)
            {
                MessageBox.Show(this, L.F("settings.saveerr", ex.Message), "Claudometer",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            try { Autostart.Set(_autostart.Checked); }
            catch (Exception ex)
            {
                MessageBox.Show(this, L.F("settings.autostarterr", ex.Message), "Claudometer",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    /// <summary>Run-key autostart. A registry value is reversible and needs no COM shortcut plumbing.</summary>
    public static class Autostart
    {
        private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string Name = "Claudometer";
        private const string Legacy = "TokenMeter";

        private static string Command
        {
            get { return "\"" + Application.ExecutablePath + "\""; }
        }

        public static bool IsEnabled()
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(Key, false))
                    return k != null && (k.GetValue(Name) != null || k.GetValue(Legacy) != null);
            }
            catch (Exception) { return false; }
        }

        /// <summary>
        /// Repair the Run entry at startup. The value is a literal path, so renaming or moving the
        /// exe (or the pre-Claudometer key name) leaves it pointing at something that no longer
        /// exists - Windows then fails silently at boot while the checkbox still reads "on". Point
        /// it at wherever this exe actually is, and retire the legacy name.
        /// </summary>
        public static void Sync()
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(Key, true))
                {
                    if (k == null) return;
                    bool wanted = k.GetValue(Name) != null || k.GetValue(Legacy) != null;
                    if (k.GetValue(Legacy) != null) k.DeleteValue(Legacy, false);
                    if (!wanted) return;
                    string cur = k.GetValue(Name) as string;
                    if (!string.Equals(cur, Command, StringComparison.OrdinalIgnoreCase))
                        k.SetValue(Name, Command);
                }
            }
            catch (Exception) { }
        }

        public static void Set(bool on)
        {
            using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(Key, true))
            {
                if (k == null) return;
                if (k.GetValue(Legacy) != null) k.DeleteValue(Legacy, false);   // drop the pre-rename key
                if (on)
                    k.SetValue(Name, Command);
                else if (k.GetValue(Name) != null)
                    k.DeleteValue(Name, false);
            }
        }
    }
}
