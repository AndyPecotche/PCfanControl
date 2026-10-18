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
        readonly AppConfig _cfg;       // copia de trabajo (se aplica con "Guardar y aplicar")
        bool _startHidden, _loading, _exit;

        ListBox _list;
        TextBox _name;
        CheckBox _enabled, _autostart;
        ComboBox _ctl, _temp;
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

            try { _svc = new FanService(_cfg.Clone()); }
            catch (Exception ex)
            {
                MessageBox.Show("No se pudo iniciar la lectura de hardware:\n\n" + ex.Message, "FanCurve",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                Environment.Exit(1);
            }

            BuildUi();
            BindRules();

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
            var tabRules = new TabPage("Reglas") { Padding = new Padding(8) };
            var tabSensors = new TabPage("Sensores");
            tabs.TabPages.Add(tabRules);
            tabs.TabPages.Add(tabSensors);

            // --- barra inferior ---
            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 46, Padding = new Padding(10, 8, 10, 8) };
            var save = new Button { Text = "Guardar y aplicar", Width = 150, Dock = DockStyle.Right };
            save.Click += (s, e) => SaveAndApply();
            _autostart = new CheckBox { Text = "Iniciar con Windows (en segundo plano)", Dock = DockStyle.Left, AutoSize = true, CheckAlign = ContentAlignment.MiddleLeft };
            _autostart.Checked = Autostart.IsEnabled();
            _autostart.CheckedChanged += (s, e) =>
            {
                bool ok = _autostart.Checked ? Autostart.Enable() : Autostart.Disable();
                if (!ok) MessageBox.Show("No se pudo modificar la tarea de inicio.", "FanCurve", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            };
            bottom.Controls.Add(save);
            bottom.Controls.Add(_autostart);
            Controls.Add(tabs);
            Controls.Add(bottom);

            // --- pestaña reglas: izquierda (lista) ---
            _list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
            _list.SelectedIndexChanged += (s, e) => { if (!_loading) LoadRule(); };
            var add = new Button { Text = "Agregar regla", Dock = DockStyle.Left, Width = 100 };
            var del = new Button { Text = "Quitar", Dock = DockStyle.Right, Width = 80 };
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
            _enabled = new CheckBox { Text = "Regla activa", AutoSize = true, Checked = true };
            _enabled.CheckedChanged += (s, e) => { if (!_loading && Current != null) OnEnabledChanged(); };
            _ctl = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            _ctl.SelectedIndexChanged += (s, e) => { if (!_loading && Current != null && _ctl.SelectedItem != null) OnControlChanged(((Item)_ctl.SelectedItem).Id); };
            _temp = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            _temp.SelectedIndexChanged += (s, e) => { if (!_loading && Current != null && _temp.SelectedItem != null) Current.TempId = ((Item)_temp.SelectedItem).Id; };

            var form = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 4 };
            form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Action<string, Control> row = (label, ctrl) =>
            {
                form.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 0, 6) });
                ctrl.Margin = new Padding(0, 3, 0, 3);
                form.Controls.Add(ctrl);
            };
            row("Nombre", _name);
            row("Salida (ventilador)", _ctl);
            row("Fuente de temperatura", _temp);
            row("", _enabled);

            _curve = new CurveControl { Dock = DockStyle.Fill };

            var hint = new Label { Text = "Arrastrá los puntos · doble clic para agregar uno · clic derecho sobre un punto para quitarlo",
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
            _sensors.Columns.Add("Tipo", 70);
            _sensors.Columns.Add("Hardware", 210);
            _sensors.Columns.Add("Sensor", 190);
            _sensors.Columns.Add("Valor", 80);
            _sensors.Columns.Add("Id", 240);
            _sensors.Columns.Add("Modo", 90);
            var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, Padding = new Padding(6) };
            var test = new Button { Text = "Probar salida al 100% (8 s)", AutoSize = true };
            var stop = new Button { Text = "Cancelar prueba", AutoSize = true };
            var tip = new Label { Text = "Elegí una fila «Control» y mirá qué ventilador (RPM) reacciona. «Modo» debe pasar a Software.", AutoSize = true, Padding = new Padding(8, 6, 0, 0), ForeColor = SystemColors.GrayText };
            test.Click += (s, e) => { var id = SelectedControlId(); if (id != null) _svc.TestControl(id, 8); };
            stop.Click += (s, e) => { var id = SelectedControlId(); if (id != null) _svc.CancelTest(id); };
            bar.Controls.AddRange(new Control[] { test, stop, tip });
            tabSensors.Controls.Add(_sensors);
            tabSensors.Controls.Add(bar);

            // --- bandeja ---
            var menu = new ContextMenuStrip();
            menu.Items.Add("Abrir", null, (s, e) => ShowWindow());
            menu.Items.Add("Salir (ventiladores a automático)", null, (s, e) => { _exit = true; Close(); });
            _tray = new NotifyIcon { Icon = SystemIcons.Application, Text = "FanCurve", Visible = true, ContextMenuStrip = menu };
            _tray.DoubleClick += (s, e) => ShowWindow();
        }

        string SelectedControlId()
        {
            if (_sensors.SelectedItems.Count == 0) return null;
            var it = _sensors.SelectedItems[0];
            return it.SubItems[0].Text == "Control" ? it.SubItems[4].Text : null;
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
                sel = new Item { Id = selectedId, Text = "(no disponible) " + selectedId };
                cb.Items.Add(sel);
            }
            cb.SelectedItem = sel;
        }

        void LoadRule()
        {
            _loading = true;
            var r = Current;
            bool has = r != null;
            _name.Enabled = _enabled.Enabled = _ctl.Enabled = _temp.Enabled = _curve.Enabled = has;
            if (has)
            {
                _name.Text = r.Name;
                _enabled.Checked = r.Enabled;
                Fill(_ctl, _ctlItems, r.ControlId);
                Fill(_temp, _tempItems, r.TempId);
                _curve.Points = r.Points;
            }
            else
            {
                _name.Text = "";
                _ctl.Items.Clear(); _temp.Items.Clear();
                _curve.Points = new List<CurvePoint>();
            }
            _curve.CurrentTemp = null; _curve.CurrentPercent = null;
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
                "La salida «" + ControlName(r.ControlId) + "» ya tiene una regla activa: «" + other.Name + "».\n\n" +
                "Si activás «" + r.Name + "», se desactivará «" + other.Name + "» (queda guardada, pero inactiva).\n\n¿Continuar?",
                "Salida ya en uso", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
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
            var r = new FanRule { Name = "Regla " + (_cfg.Rules.Count + 1) };
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
                dlg.Text = "Configuración peligrosa";
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
                    Text = "PELIGRO: velocidad menor al 30% por encima de 70 °C\n\n" +
                           "Estas reglas dejan el ventilador por debajo del 30% con más de 70 °C:\n\n" +
                           string.Join("\n", risky.Select(r => "  •  " + r.Name + "  →  " + ControlName(r.ControlId))) + "\n\n" +
                           "Revisá el ventilador al que estás asignando la curva. Si es el de una GPU o un CPU, " +
                           "esta configuración podría ser peligrosa (sobrecalentamiento) o reducir el rendimiento. " +
                           "Si es un ventilador de gabinete, es seguro continuar.",
                });
                var ok = new Button { Text = "Entiendo", DialogResult = DialogResult.OK, Left = 320, Top = 280, Width = 100 };
                var cancel = new Button { Text = "Cancelar", DialogResult = DialogResult.Cancel, Left = 430, Top = 280, Width = 100 };
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
                MessageBox.Show("La salida «" + ControlName(dup.Key) + "» tiene más de una regla activa. Dejá solo una activa.",
                    "FanCurve", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!ConfirmRisky()) { _status.Text = "Cancelado: la configuración no se aplicó."; return; }

            try { _cfg.Save(); }
            catch (Exception ex) { MessageBox.Show("No se pudo guardar la configuración:\n" + ex.Message); return; }
            _svc.SetConfig(_cfg.Clone());
            _status.Text = "Configuración aplicada.";
        }

        // ---------- lectura en vivo ----------

        void RefreshLive()
        {
            if (!Visible) return;
            var snap = _svc.GetSnapshot();

            var r = Current;
            RuleStatus st;
            if (r != null && snap.Rules.TryGetValue(r.Id, out st))
            {
                _curve.CurrentTemp = st.Temp;
                _curve.CurrentPercent = st.Percent;
                _curve.Invalidate();
                if (st.Message != null) _status.Text = st.Message;
                else if (st.Temp.HasValue && st.Percent.HasValue)
                    _status.Text = string.Format("Ahora: {0:0.#} °C  →  {1:0} %", st.Temp, st.Percent);
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
                else _sensors.Items.Add(new ListViewItem(new[] { s.Type == SensorType.Control ? "Control" : s.Type == SensorType.Fan ? "Fan" : "Temp", s.Hardware, s.Name, val, s.Id, s.Mode ?? "" }));
            }
            _sensors.EndUpdate();
        }
    }
}
