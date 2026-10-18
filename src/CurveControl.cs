using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace FanCurve
{
    // Editor grafico: arrastrar puntos, doble clic para agregar, clic derecho para quitar.
    public class CurveControl : Control
    {
        const double TMin = 20, TMax = 100;
        const int Hit = 9;

        public List<CurvePoint> Points = new List<CurvePoint>();
        public double? CurrentTemp;
        public double? CurrentPercent;   // regla aplicada: punto sobre la curva
        public double? OutputPercent;
        public Func<double, double> RpmForPercent;   // calibracion: RPM esperadas para un % (eje derecho); null = sin calibrar
        public double FloorPercent;                  // calibracion: % por debajo del cual el ventilador no baja mas
        public List<CurvePoint> DefaultCurve;   // curva aprendida de la placa (linea negra punteada fina)    // regla no aplicada: velocidad real de la salida (linea azul)
        public event EventHandler Changed;

        int _drag = -1;
        string _tip;

        public CurveControl()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
            MinimumSize = new Size(300, 200);
        }

        Rectangle Plot => new Rectangle(42, 12, Math.Max(10, Width - 112), Math.Max(10, Height - 42)); // margen derecho para el eje de RPM

        PointF ToPx(double t, double p)
        {
            var r = Plot;
            return new PointF((float)(r.Left + (t - TMin) / (TMax - TMin) * r.Width),
                              (float)(r.Bottom - p / 100.0 * r.Height));
        }

        void FromPx(Point px, out double t, out double p)
        {
            var r = Plot;
            t = TMin + (px.X - r.Left) / (double)r.Width * (TMax - TMin);
            p = (r.Bottom - px.Y) / (double)r.Height * 100.0;
            t = Math.Round(Math.Max(TMin, Math.Min(TMax, t)));
            p = Math.Round(Math.Max(0, Math.Min(100, p)));
        }

        int HitTest(Point px)
        {
            for (int i = 0; i < Points.Count; i++)
            {
                var c = ToPx(Points[i].Temp, Points[i].Percent);
                if (Math.Abs(c.X - px.X) <= Hit && Math.Abs(c.Y - px.Y) <= Hit) return i;
            }
            return -1;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            int i = HitTest(e.Location);
            if (e.Button == MouseButtons.Left && i >= 0) { _drag = i; Capture = true; }
            else if (e.Button == MouseButtons.Right && i >= 0 && Points.Count > 2)
            {
                Points.RemoveAt(i);
                Invalidate();
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_drag < 0) { Cursor = HitTest(e.Location) >= 0 ? Cursors.Hand : Cursors.Default; return; }
            double t, p;
            FromPx(e.Location, out t, out p);
            double lo = _drag > 0 ? Points[_drag - 1].Temp + 1 : TMin;
            double hi = _drag < Points.Count - 1 ? Points[_drag + 1].Temp - 1 : TMax;
            t = Math.Max(lo, Math.Min(hi, t));
            Points[_drag].Temp = t;
            Points[_drag].Percent = p;
            _tip = t + " °C  →  " + p + " %";
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_drag >= 0)
            {
                _drag = -1; _tip = null; Capture = false;
                Invalidate();
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Button != MouseButtons.Left || HitTest(e.Location) >= 0 || !Plot.Contains(e.Location)) return;
            double t, p;
            FromPx(e.Location, out t, out p);
            if (Points.Any(x => x.Temp == t)) return;
            Points.Add(new CurvePoint { Temp = t, Percent = p });
            Points.Sort((a, b) => a.Temp.CompareTo(b.Temp));
            Invalidate();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(SystemColors.Window);
            var r = Plot;

            using (var grid = new Pen(Color.FromArgb(225, 225, 225)))
            using (var txt = new SolidBrush(SystemColors.GrayText))
            {
                for (int t = (int)TMin; t <= TMax; t += 10)
                {
                    var a = ToPx(t, 0);
                    g.DrawLine(grid, a.X, r.Top, a.X, r.Bottom);
                    g.DrawString(t + "°", Font, txt, a.X - 10, r.Bottom + 4);
                }
                for (int p = 0; p <= 100; p += 20)
                {
                    var a = ToPx(TMin, p);
                    g.DrawLine(grid, r.Left, a.Y, r.Right, a.Y);
                    g.DrawString(p + "%", Font, txt, 2, a.Y - 7);
                }
            }
            // zona "sin efecto": por debajo del piso real del ventilador
            if (RpmForPercent != null && FloorPercent > 0)
            {
                var top = ToPx(TMin, FloorPercent).Y;
                using (var br = new SolidBrush(Color.FromArgb(40, 120, 120, 120)))
                    g.FillRectangle(br, r.Left, top, r.Width, r.Bottom - top);
            }
            g.DrawRectangle(Pens.Gray, r);

            // eje derecho: RPM segun la calibracion de la salida
            if (RpmForPercent != null)
            {
                using (var b = new SolidBrush(Color.SteelBlue))
                {
                    g.DrawString("RPM", Font, b, r.Right + 6, r.Top - 11);
                    for (int p = 0; p <= 100; p += 20)
                    {
                        var a = ToPx(TMax, p);
                        g.DrawString(((int)Math.Round(RpmForPercent(p))).ToString(), Font, b, r.Right + 6, a.Y - 7);
                    }
                }
            }

            // curva por defecto: por detras de la personalizada
            if (DefaultCurve != null && DefaultCurve.Count > 0)
            {
                using (var pen = new Pen(Color.Black, 1f) { DashStyle = DashStyle.Dot })
                {
                    var seg = new List<PointF>();
                    double lastT = double.NaN;
                    Action flush = () => { if (seg.Count > 1) g.DrawLines(pen, seg.ToArray()); seg.Clear(); };
                    foreach (var d in DefaultCurve.OrderBy(x => x.Temp))
                    {
                        if (!double.IsNaN(lastT) && d.Temp - lastT > 5) flush(); // no unir tramos sin datos
                        seg.Add(ToPx(d.Temp, d.Percent));
                        lastT = d.Temp;
                    }
                    flush();
                }
            }

            var pts = Points.OrderBy(x => x.Temp).ToList();
            if (pts.Count > 0)
            {
                var line = new List<PointF> { ToPx(TMin, pts[0].Percent) };
                line.AddRange(pts.Select(x => ToPx(x.Temp, x.Percent)));
                line.Add(ToPx(TMax, pts[pts.Count - 1].Percent));
                using (var pen = new Pen(Color.SteelBlue, 2.5f))
                    if (line.Count > 1) g.DrawLines(pen, line.ToArray());

                foreach (var pt in pts)
                {
                    var c = ToPx(pt.Temp, pt.Percent);
                    g.FillEllipse(Brushes.White, c.X - 5, c.Y - 5, 10, 10);
                    g.DrawEllipse(Pens.SteelBlue, c.X - 5, c.Y - 5, 10, 10);
                }
            }

            if (CurrentTemp.HasValue)
            {
                var x = ToPx(Math.Max(TMin, Math.Min(TMax, CurrentTemp.Value)), 0).X;
                using (var pen = new Pen(Color.Red, 1.5f) { DashStyle = DashStyle.Dash })
                    g.DrawLine(pen, x, r.Top, x, r.Bottom);
                if (CurrentPercent.HasValue)
                {
                    var c = ToPx(CurrentTemp.Value, CurrentPercent.Value);
                    g.FillEllipse(Brushes.Black, c.X - 4, c.Y - 4, 8, 8);
                }
            }

            if (OutputPercent.HasValue)
            {
                var y = ToPx(TMin, Math.Max(0, Math.Min(100, OutputPercent.Value))).Y;
                using (var pen = new Pen(Color.RoyalBlue, 1.5f))
                    g.DrawLine(pen, r.Left, y, r.Right, y);
                if (CurrentTemp.HasValue)
                {
                    var c = ToPx(Math.Max(TMin, Math.Min(TMax, CurrentTemp.Value)), OutputPercent.Value);
                    g.FillEllipse(Brushes.Black, c.X - 4, c.Y - 4, 8, 8);
                }
            }

            if (_tip != null)
                g.DrawString(_tip, Font, Brushes.Black, r.Left + 6, r.Top + 4);
        }
    }
}
