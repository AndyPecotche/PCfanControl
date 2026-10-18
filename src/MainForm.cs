using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using LibreHardwareMonitor.Hardware;

namespace FanCurve
{
    public class MainForm : Form
    {
        class Item
        {
            public string Id, Text;
            public override string ToString() { return Text; }
        }

        readonly FanService _svc;
        readonly AppConfig _cfg;       // working copy (applied with "Save and apply")
        bool _startHidden, _loading, _exit;
        string _appliedFingerprint;    // configuracion que esta corriendo en el servicio

        ListBox _list;
        TextBox _name;
        CheckBox _enabled, _autostart;
        ComboBox _ctl, _temp, _rise, _fall;
        static readonly double[] ReactValues = { 0, 0.5, 1, 2 };
        static readonly string[] ReactKeys = { "react.0", "react.0.5", "react.1", "react.2" };
        CurveControl _curve;
        Label _status;
        ListView _sensors;
        NotifyIcon _tray;
        Timer _ui;

        FanRule Current { get { return _list.SelectedItem as FanRule; } }

        public MainForm(bool startHidden)
        {
            _startHidden = startHidden;
            _cfg = AppConfig.Load();
            L.Lang = L.Detect(_cfg.Language);

            try { _svc = new FanService(_cfg.Clone()); }
            catch (Exception ex)
            {
                MessageBox.Show(L.T("msg.hwfail") + ex.Message, "FanCurve",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                Environment.Exit(1);
            }

            BuildUi();
            BindRules();
            _appliedFingerprint = Fingerprint();

            AppDomain.CurrentDomain.ProcessExit += (s, e) => { if (_svc != null) _svc.Dispose(); };
            AppDomain.CurrentDomain.UnhandledException += (s, e) => { if (_svc != null) _svc.Dispose(); };

            _ui = new Timer { Interval = 1000 };
            _ui.Tick += (s, e) => RefreshLive();
            _ui.Start();
        }

        // ---------- UI ----------

        void BuildUi()
        {
            Text = "FanCurve v" + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString(3);
            ClientSize = new Size(900, 600);
            MinimumSize = new Size(780, 540);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9f);
            Icon = SystemIcons.Application;

            var tabs = new TabControl { Dock = DockStyle.Fill };
            var tabRules = new TabPage(L.T("tab.rules")) { Padding = new Padding(8) };
            var tabSensors = new TabPage(L.T("tab.sensors"));
            tabs.TabPages.Add(tabRules);
            tabs.TabPages.Add(tabSensors);

            // --- barra inferior ---
            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 46, Padding = new Padding(10, 8, 10, 8) };
            var save = new Button { Text = L.T("btn.save"), Width = 150, Dock = DockStyle.Right };
            save.Click += (s, e) => SaveAndApply();
            _autostart = new CheckBox { Text = L.T("chk.autostart"), Dock = DockStyle.Left, AutoSize = true, CheckAlign = ContentAlignment.MiddleLeft };
            _autostart.Checked = Autostart.IsEnabled();
            _autostart.CheckedChanged += (s, e) =>
            {
                bool ok = _autostart.Checked ? Autostart.Enable() : Autostart.Disable();
                if (!ok) MessageBox.Show(L.T("msg.autostart"), "FanCurve", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            };
            var langBox = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            langBox.Items.AddRange(L.LanguageNames);
            langBox.SelectedIndex = Array.IndexOf(L.Languages, L.Lang);
            langBox.SelectedIndexChanged += (s, e) => { var l = L.Languages[langBox.SelectedIndex]; BeginInvoke((Action)(() => ChangeLanguage(l))); };
            var langLabel = new Label { Text = L.T("lbl.language"), Dock = DockStyle.Left, Width = 62, TextAlign = ContentAlignment.MiddleRight };
            var langPanel = new Panel { Dock = DockStyle.Right, Width = 230, Padding = new Padding(0, 0, 14, 0) };
            langPanel.Controls.Add(langBox);
            langPanel.Controls.Add(langLabel);
            bottom.Controls.Add(langPanel);
            bottom.Controls.Add(save);
            bottom.Controls.Add(_autostart);
            Controls.Add(tabs);
            Controls.Add(bottom);

            // --- pestaña reglas: izquierda (lista) ---
            _list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
            _list.SelectedIndexChanged += (s, e) => { if (!_loading) LoadRule(); };
            var add = new Button { Text = L.T("btn.add"), Dock = DockStyle.Left, Width = 100 };
            var del = new Button { Text = L.T("btn.remove"), Dock = DockStyle.Right, Width = 80 };
            add.Click += (s, e) => AddRule();
            del.Click += (s, e) => RemoveRule();
            var btns = new Panel { Dock = DockStyle.Bottom, Height = 34, Padding = new Padding(0, 6, 0, 0) };
            btns.Controls.Add(del);
            btns.Controls.Add(add);
            var left = new Panel { Dock = DockStyle.Left, Width = 200, Padding = new Padding(0, 0, 8, 0) };
            left.Controls.Add(_list);
            left.Controls.Add(btns);

            // --- derecha (editor) ---
            _name = new TextBox { Dock = DockStyle.Fill };
            _name.TextChanged += (s, e) => { if (!_loading && Current != null) { Current.Name = _name.Text; RefreshList(); } };
            _enabled = new CheckBox { Text = L.T("chk.enabled"), AutoSize = true, Checked = true };
            _enabled.CheckedChanged += (s, e) => { if (!_loading && Current != null) OnEnabledChanged(); };
            _ctl = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            _ctl.SelectedIndexChanged += (s, e) => { if (!_loading && Current != null && _ctl.SelectedItem != null) OnControlChanged(((Item)_ctl.SelectedItem).Id); };
            _temp = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            _temp.SelectedIndexChanged += (s, e) => { if (!_loading && Current != null && _temp.SelectedItem != null) Current.TempId = ((Item)_temp.SelectedItem).Id; };

            var form = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 6 };
            form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Action<string, Control> row = (label, ctrl) =>
            {
                form.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 0, 6) });
                ctrl.Margin = new Padding(0, 3, 0, 3);
                form.Controls.Add(ctrl);
            };
            row(L.T("lbl.name"), _name);
            row(L.T("lbl.output"), _ctl);
            row(L.T("lbl.tempsource"), _temp);
            _rise = MakeReactCombo(v => { if (Current != null) Current.RiseSeconds = v; });
            _fall = MakeReactCombo(v => { if (Current != null) Current.FallSeconds = v; });
            row(L.T("lbl.rise"), _rise);
            row(L.T("lbl.fall"), _fall);
            row("", _enabled);

            _curve = new CurveControl { Dock = DockStyle.Fill };

            var hint = new Label { Text = L.T("lbl.hint"),
                Dock = DockStyle.Top, Height = 20, ForeColor = SystemColors.GrayText };
            _status = new Label { Dock = DockStyle.Top, Height = 22, Font = new Font(Font, FontStyle.Bold) };
            var info = new Panel { Dock = DockStyle.Bottom, Height = 46, Padding = new Padding(0, 4, 0, 0) };
            info.Controls.Add(_status);
            info.Controls.Add(hint);

            var right = new Panel { Dock = DockStyle.Fill };
            right.Controls.Add(_curve);
            right.Controls.Add(info);
            right.Controls.Add(form);

            tabRules.Controls.Add(right);
            tabRules.Controls.Add(left);

            // --- pestaña sensores ---
            _sensors = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false };
            _sensors.Columns.Add(L.T("col.type"), 70);
            _sensors.Columns.Add("Hardware", 210);
            _sensors.Columns.Add("Sensor", 190);
            _sensors.Columns.Add(L.T("col.value"), 80);
            _sensors.Columns.Add("Id", 240);
            _sensors.Columns.Add(L.T("col.mode"), 90);
            var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, Padding = new Padding(6) };
            var test = new Button { Text = L.T("btn.test"), AutoSize = true };
            var stop = new Button { Text = L.T("btn.canceltest"), AutoSize = true };
            var tip = new Label { Text = L.T("lbl.sensortip"), AutoSize = true, Padding = new Padding(8, 6, 0, 0), ForeColor = SystemColors.GrayText };
            test.Click += (s, e) => { var id = SelectedControlId(); if (id != null) _svc.TestControl(id, 8); };
            stop.Click += (s, e) => { var id = SelectedControlId(); if (id != null) _svc.CancelTest(id); };
            bar.Controls.AddRange(new Control[] { test, stop, tip });
            tabSensors.Controls.Add(_sensors);
            tabSensors.Controls.Add(bar);

            // --- bandeja ---
            var menu = new ContextMenuStrip();
            menu.Items.Add(L.T("tray.open"), null, (s, e) => ShowWindow());
            menu.Items.Add(L.T("tray.exit"), null, (s, e) => { _exit = true; Close(); });
            _tray = new NotifyIcon { Icon = SystemIcons.Application, Text = "FanCurve", Visible = true, ContextMenuStrip = menu };
            _tray.DoubleClick += (s, e) => ShowWindow();
        }

        ComboBox MakeReactCombo(Action<double> onChange)
        {
            var cb = new ComboBox { Dock = DockStyle.Left, Width = 160, DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (var k in ReactKeys) cb.Items.Add(L.T(k));
            cb.SelectedIndex = 0;
            cb.SelectedIndexChanged += (s, e) => { if (!_loading && cb.SelectedIndex >= 0) onChange(ReactValues[cb.SelectedIndex]); };
            return cb;
        }

        static int NearestReact(double v)
        {
            int best = 0;
            for (int i = 1; i < ReactValues.Length; i++)
                if (Math.Abs(ReactValues[i] - v) < Math.Abs(ReactValues[best] - v)) best = i;
            return best;
        }

        string SelectedControlId()
        {
            if (_sensors.SelectedItems.Count == 0) return null;
            var it = _sensors.SelectedItems[0];
            return (it.Tag is SensorType && (SensorType)it.Tag == SensorType.Control) ? it.SubItems[4].Text : null;
        }

        // Reconstruye toda la interfaz con el idioma elegido, conservando las reglas en edicion.
        void ChangeLanguage(string lang)
        {
            if (lang == L.Lang) return;
            L.Lang = lang;
            _cfg.Language = lang;
            try { AppConfig.SaveLanguage(lang); } catch { }

            int sel = _list.SelectedIndex;
            _tray.Visible = false;
            _tray.Dispose();
            var size = ClientSize;
            SuspendLayout();
            foreach (var c in Controls.Cast<Control>().ToList()) { Controls.Remove(c); c.Dispose(); }
            BuildUi();
            ClientSize = size;
            BindRules();
            if (sel >= 0 && sel < _list.Items.Count) _list.SelectedIndex = sel;
            ResumeLayout(true);
        }

        // ---------- ventana oculta / bandeja ----------

        protected override void SetVisibleCore(bool value)
        {
            if (_startHidden)
            {
                _startHidden = false;
                value = false;
                if (!IsHandleCreated) CreateHandle();
            }
            base.SetVisibleCore(value);
        }

        void ShowWindow()
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_exit && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                return;
            }
            _tray.Visible = false;
            _ui.Stop();
            _svc.Dispose();
            base.OnFormClosing(e);
        }

        // ---------- reglas ----------

        void BindRules()
        {
            var snap = _svc.GetSnapshot();
            var temps = snap.Sensors.Where(s => s.Type == SensorType.Temperature).OrderBy(s => s.FullName).ToList();
            var ctls = snap.Sensors.Where(s => s.Type == SensorType.Control).OrderBy(s => s.FullName).ToList();

            _tempItems = temps.Select(s => new Item { Id = s.Id, Text = s.FullName }).ToList();
            _ctlItems = ctls.Select(s => new Item { Id = s.Id, Text = s.FullName }).ToList();

            _list.Items.Clear();
            foreach (var r in _cfg.Rules) _list.Items.Add(r);
            if (_list.Items.Count > 0) _list.SelectedIndex = 0; else LoadRule();
        }

        List<Item> _tempItems = new List<Item>(), _ctlItems = new List<Item>();

        static void Fill(ComboBox cb, List<Item> items, string selectedId)
        {
            cb.Items.Clear();
            cb.Items.AddRange(items.Cast<object>().ToArray());
            var sel = items.FirstOrDefault(i => i.Id == selectedId);
            if (sel == null && !string.IsNullOrEmpty(selectedId))
            {
                sel = new Item { Id = selectedId, Text = L.T("item.unavailable") + selectedId };
                cb.Items.Add(sel);
            }
            cb.SelectedItem = sel;
        }

        void LoadRule()
        {
            _loading = true;
            var r = Current;
            bool has = r != null;
            _name.Enabled = _enabled.Enabled = _ctl.Enabled = _temp.Enabled = _rise.Enabled = _fall.Enabled = _curve.Enabled = has;
            if (has)
            {
                _name.Text = r.Name;
                _enabled.Checked = r.Enabled;
                Fill(_ctl, _ctlItems, r.ControlId);
                Fill(_temp, _tempItems, r.TempId);
                _rise.SelectedIndex = NearestReact(r.RiseSeconds);
                _fall.SelectedIndex = NearestReact(r.FallSeconds);
                _curve.Points = r.Points;
            }
            else
            {
                _name.Text = "";
                _ctl.Items.Clear(); _temp.Items.Clear();
                _curve.Points = new List<CurvePoint>();
            }
            _curve.CurrentTemp = null; _curve.CurrentPercent = null; _curve.OutputPercent = null;
            _curve.Invalidate();
            _status.Text = "";
            _loading = false;
        }

        // Redibuja los nombres de la lista (nombre / estado) sin disparar el cambio de seleccion.
        void RefreshList()
        {
            int sel = _list.SelectedIndex;
            _loading = true;
            try
            {
                for (int i = 0; i < _list.Items.Count; i++) _list.Items[i] = _list.Items[i];
                _list.SelectedIndex = sel;
            }
            finally { _loading = false; }
        }

        string ControlName(string id)
        {
            var it = _ctlItems.FirstOrDefault(i => i.Id == id);
            return it != null ? it.Text : id;
        }

        FanRule ConflictWith(FanRule r)
        {
            if (string.IsNullOrEmpty(r.ControlId)) return null;
            return _cfg.Rules.FirstOrDefault(o => o != r && o.Enabled && o.ControlId == r.ControlId);
        }

        // Una sola regla activa por salida: pide confirmacion y desactiva la otra.
        bool ResolveConflict(FanRule r)
        {
            var other = ConflictWith(r);
            if (other == null) return true;
            var res = MessageBox.Show(
                L.F("conflict.msg", ControlName(r.ControlId), other.Name, r.Name),
                L.T("conflict.title"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (res != DialogResult.Yes) return false;
            foreach (var o in _cfg.Rules.Where(o => o != r && o.Enabled && o.ControlId == r.ControlId)) o.Enabled = false;
            return true;
        }

        void OnEnabledChanged()
        {
            var r = Current;
            r.Enabled = _enabled.Checked;
            if (r.Enabled && !ResolveConflict(r))
            {
                r.Enabled = false;
                _loading = true; _enabled.Checked = false; _loading = false;
            }
            RefreshList();
        }

        void OnControlChanged(string newId)
        {
            var r = Current;
            string old = r.ControlId;
            r.ControlId = newId;
            if (r.Enabled && !ResolveConflict(r))
            {
                r.ControlId = old;
                _loading = true; Fill(_ctl, _ctlItems, old); _loading = false;
            }
            RefreshList();
        }

        void AddRule()
        {
            var r = new FanRule { Name = L.F("rule.new", _cfg.Rules.Count + 1) };
            if (_ctlItems.Count > 0) r.ControlId = _ctlItems[0].Id;
            var hot = _tempItems.FirstOrDefault(i => i.Text.IndexOf("Hot Spot", StringComparison.OrdinalIgnoreCase) >= 0)
                      ?? _tempItems.FirstOrDefault(i => i.Text.IndexOf("Package", StringComparison.OrdinalIgnoreCase) >= 0)
                      ?? _tempItems.FirstOrDefault();
            if (hot != null) r.TempId = hot.Id;
            if (ConflictWith(r) != null) r.Enabled = false; // ya hay otra regla activa en esa salida
            _cfg.Rules.Add(r);
            _list.Items.Add(r);
            _list.SelectedItem = r;
        }

        void RemoveRule()
        {
            var r = Current;
            if (r == null) return;
            int i = _list.SelectedIndex;
            _cfg.Rules.Remove(r);
            _list.Items.Remove(r);
            if (_list.Items.Count > 0) _list.SelectedIndex = Math.Min(i, _list.Items.Count - 1); else LoadRule();
        }

        // Reglas cuya curva deja el ventilador por debajo del 30% con mas de 70 °C.
        static bool IsRisky(FanRule r)
        {
            var temps = new List<double> { 70.01, 100 };
            temps.AddRange(r.Points.Where(p => p.Temp > 70).Select(p => p.Temp));
            return temps.Any(t => Curve.Evaluate(r.Points, t) < 30);
        }

        bool ConfirmRisky()
        {
            var risky = _cfg.Rules.Where(r => r.Enabled && IsRisky(r)).ToList();
            if (risky.Count == 0) return true;

            using (var dlg = new Form())
            {
                dlg.Text = L.T("danger.title");
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.MaximizeBox = dlg.MinimizeBox = false;
                dlg.ShowInTaskbar = false;
                dlg.ClientSize = new Size(540, 320);
                dlg.Font = Font;

                dlg.Controls.Add(new PictureBox { Image = SystemIcons.Warning.ToBitmap(), SizeMode = PictureBoxSizeMode.CenterImage, Left = 14, Top = 14, Width = 48, Height = 48 });
                dlg.Controls.Add(new Label
                {
                    Left = 74, Top = 14, Width = 450, Height = 255,
                    Text = L.T("danger.head") + "\n\n" +
                           L.T("danger.list") + "\n\n" +
                           string.Join("\n", risky.Select(r => "  •  " + r.Name + "  →  " + ControlName(r.ControlId))) + "\n\n" +
                           L.T("danger.advice"),
                });
                var ok = new Button { Text = L.T("btn.understand"), DialogResult = DialogResult.OK, Left = 320, Top = 280, Width = 100 };
                var cancel = new Button { Text = L.T("btn.cancel"), DialogResult = DialogResult.Cancel, Left = 430, Top = 280, Width = 100 };
                dlg.Controls.Add(ok);
                dlg.Controls.Add(cancel);
                dlg.AcceptButton = cancel; // por defecto, la opcion segura
                dlg.CancelButton = cancel;
                return dlg.ShowDialog(this) == DialogResult.OK;
            }
        }

        void SaveAndApply()
        {
            foreach (var r in _cfg.Rules) r.Points = r.Points.OrderBy(p => p.Temp).ToList();

            var dup = _cfg.Rules.Where(r => r.Enabled && !string.IsNullOrEmpty(r.ControlId))
                               .GroupBy(r => r.ControlId).FirstOrDefault(g => g.Count() > 1);
            if (dup != null)
            {
                MessageBox.Show(L.F("dup.msg", ControlName(dup.Key)),
                    "FanCurve", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!ConfirmRisky()) { _status.Text = L.T("status.cancelled"); return; }

            try { _cfg.Save(); }
            catch (Exception ex) { MessageBox.Show(L.T("msg.savefail") + ex.Message); return; }
            _svc.SetConfig(_cfg.Clone());
            _appliedFingerprint = Fingerprint();
            _status.Text = L.T("status.applied");
        }

        // ---------- cambios sin aplicar ----------

        string Fingerprint()
        {
            var c = _cfg.Clone();
            c.Language = null; // el idioma no cuenta como cambio de configuracion
            return new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(c);
        }

        bool IsDirty() { return _appliedFingerprint != null && Fingerprint() != _appliedFingerprint; }

        // ---------- lectura en vivo ----------

        void RefreshLive()
        {
            if (!Visible) return;
            var snap = _svc.GetSnapshot();

            var r = Current;
            if (r != null)
            {
                // Temperatura actual de la fuente elegida en el editor, este aplicada la regla o no.
                SensorInfo tempSensor = snap.Sensors.FirstOrDefault(x => x.Id == r.TempId);
                SensorInfo outSensor = snap.Sensors.FirstOrDefault(x => x.Id == r.ControlId);
                _curve.CurrentTemp = tempSensor != null ? tempSensor.Value : null;

                string text = "";
                RuleStatus st;
                if (snap.Rules.TryGetValue(r.Id, out st))
                {
                    // Regla aplicada: el punto cae sobre la curva.
                    _curve.CurrentPercent = st.Percent;
                    _curve.OutputPercent = null;
                    if (st.Message != null) text = st.Message;
                    else if (st.Temp.HasValue && st.Percent.HasValue) text = L.F("status.now", st.Temp, st.Percent);
                }
                else
                {
                    // Regla no aplicada: linea horizontal con el % real que tiene hoy la salida.
                    _curve.CurrentPercent = null;
                    _curve.OutputPercent = outSensor != null ? outSensor.Value : null;
                    text = L.T("status.notapplied");
                }
                if (IsDirty()) text += (text.Length > 0 ? "  ·  " : "") + L.T("status.dirty");
                _status.Text = text;
                _curve.Invalidate();
            }

            _sensors.BeginUpdate();
            var existing = _sensors.Items.Cast<ListViewItem>().ToDictionary(i => i.SubItems[4].Text);
            foreach (var s in snap.Sensors.OrderBy(s => s.Type).ThenBy(s => s.FullName))
            {
                string val = s.Value.HasValue
                    ? (s.Type == SensorType.Temperature ? s.Value.Value.ToString("0.#") + " °C"
                       : s.Type == SensorType.Fan ? s.Value.Value.ToString("0") + " RPM"
                       : s.Value.Value.ToString("0") + " %")
                    : "-";
                ListViewItem it;
                if (existing.TryGetValue(s.Id, out it)) { it.SubItems[3].Text = val; it.SubItems[5].Text = s.Mode ?? ""; }
                else _sensors.Items.Add(new ListViewItem(new[] { L.T(s.Type == SensorType.Control ? "type.control" : s.Type == SensorType.Fan ? "type.fan" : "type.temp"), s.Hardware, s.Name, val, s.Id, s.Mode ?? "" }) { Tag = s.Type });
            }
            _sensors.EndUpdate();
        }
    }
}
