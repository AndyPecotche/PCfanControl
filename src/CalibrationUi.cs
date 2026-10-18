using System;
using System.Drawing;
using System.Windows.Forms;

namespace FanCurve
{
    // Dialogos de calibracion: confirmacion (explicacion + modo) y progreso.
    public static class CalibrationUi
    {
        public static void Run(IWin32Window owner, Font font, FanService svc, string controlId, string controlName)
        {
            bool precise;
            if (!Confirm(owner, font, controlName, svc.GetCalibration(controlId) != null, out precise)) return;

            string error;
            if (!svc.StartCalibration(controlId, precise, out error))
            {
                MessageBox.Show(error, L.T("calib.title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            ShowProgress(owner, font, svc, controlName);

            var st = svc.GetCalibStatus();
            switch (st.State)
            {
                case "done":
                    MessageBox.Show(st.Message, L.T("calib.title"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                    break;
                case "cancelled":
                    MessageBox.Show(L.T("calib.cancelled"), L.T("calib.title"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                    break;
                default: // failed / aborted
                    MessageBox.Show(st.Message, L.T("calib.title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    break;
            }
        }

        static bool Confirm(IWin32Window owner, Font font, string controlName, bool hasCalibration, out bool precise)
        {
            using (var dlg = new Form())
            {
                dlg.Text = L.T("calib.title") + " — " + controlName;
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.MaximizeBox = dlg.MinimizeBox = false;
                dlg.ShowInTaskbar = false;
                dlg.ClientSize = new Size(560, 400);
                dlg.Font = font;

                dlg.Controls.Add(new PictureBox { Image = SystemIcons.Warning.ToBitmap(), SizeMode = PictureBoxSizeMode.CenterImage, Left = 12, Top = 12, Width = 48, Height = 48 });
                dlg.Controls.Add(new Label
                {
                    Left = 70, Top = 12, Width = 480, Height = 250,
                    Text = L.T("calib.explain") + (hasCalibration ? L.T("calib.replace") : ""),
                });

                var rbPrecise = new RadioButton { Left = 70, Top = 268, Width = 480, Text = L.F("calib.precise", CalibPlan.Format(CalibPlan.TotalSeconds(true))) };
                var rbFast = new RadioButton { Left = 70, Top = 294, Width = 480, Text = L.F("calib.fast", CalibPlan.Format(CalibPlan.TotalSeconds(false))), Checked = true };
                dlg.Controls.Add(rbPrecise);
                dlg.Controls.Add(rbFast);

                var ok = new Button { Text = L.T("calib.start"), DialogResult = DialogResult.OK, Left = 340, Top = 350, Width = 100 };
                var cancel = new Button { Text = L.T("btn.cancel"), DialogResult = DialogResult.Cancel, Left = 450, Top = 350, Width = 100 };
                dlg.Controls.Add(ok);
                dlg.Controls.Add(cancel);
                dlg.AcceptButton = cancel; // opcion segura por defecto
                dlg.CancelButton = cancel;

                bool accepted = dlg.ShowDialog(owner) == DialogResult.OK;
                precise = rbPrecise.Checked;
                return accepted;
            }
        }

        static void ShowProgress(IWin32Window owner, Font font, FanService svc, string controlName)
        {
            using (var dlg = new Form())
            {
                dlg.Text = L.T("calib.title") + " — " + controlName;
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.MaximizeBox = dlg.MinimizeBox = false;
                dlg.ControlBox = false;
                dlg.ShowInTaskbar = false;
                dlg.ClientSize = new Size(460, 140);
                dlg.Font = font;

                var title = new Label { Left = 14, Top = 14, Width = 430, Height = 22, Font = new Font(font, FontStyle.Bold) };
                var bar = new ProgressBar { Left = 14, Top = 42, Width = 430, Height = 22, Minimum = 0, Maximum = 1000 };
                var detail = new Label { Left = 14, Top = 72, Width = 430, Height = 22, ForeColor = SystemColors.GrayText };
                var cancel = new Button { Text = L.T("btn.cancel"), Left = 344, Top = 100, Width = 100 };
                cancel.Click += (s, e) => { cancel.Enabled = false; svc.CancelCalibration(); };
                dlg.Controls.AddRange(new Control[] { title, bar, detail, cancel });

                var timer = new Timer { Interval = 250 };
                timer.Tick += (s, e) =>
                {
                    var st = svc.GetCalibStatus();
                    if (st.State != "running") { timer.Stop(); dlg.DialogResult = DialogResult.OK; return; }
                    bar.Value = (int)Math.Max(0, Math.Min(1000, st.Progress * 1000));
                    title.Text = L.F("calib.running", st.Progress * 100);
                    detail.Text = L.F("calib.step", st.CurrentDuty,
                        st.CurrentRpm.HasValue ? L.F("calib.rpm", st.CurrentRpm.Value) : "—");
                };
                timer.Start();
                dlg.ShowDialog(owner);
                timer.Dispose();
            }
        }
    }
}
