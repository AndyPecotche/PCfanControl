using System;
using System.Collections.Generic;
using System.Threading;
using LibreHardwareMonitor.Hardware;

namespace FanCurve
{
    public class SensorInfo
    {
        public string Id;
        public string Hardware;
        public string Name;
        public SensorType Type;
        public float? Value;
        public string Mode;
        public string FullName => Hardware + " / " + Name;
    }

    public class RuleStatus
    {
        public float? Temp;
        public double? Percent;
        public string Message;
    }

    public class Snapshot
    {
        public List<SensorInfo> Sensors = new List<SensorInfo>();
        public Dictionary<string, RuleStatus> Rules = new Dictionary<string, RuleStatus>();
    }

    class UpdateVisitor : IVisitor
    {
        public void VisitComputer(IComputer computer) { computer.Traverse(this); }
        public void VisitHardware(IHardware hardware)
        {
            hardware.Update();
            foreach (var sub in hardware.SubHardware) sub.Accept(this);
        }
        public void VisitSensor(ISensor sensor) { }
        public void VisitParameter(IParameter parameter) { }
    }

    public class FanService : IDisposable
    {
        readonly Computer _pc;
        readonly object _lock = new object();
        readonly Dictionary<string, ISensor> _byId = new Dictionary<string, ISensor>();
        readonly Dictionary<string, double> _smooth = new Dictionary<string, double>();
        readonly Dictionary<string, DateTime> _tests = new Dictionary<string, DateTime>();
        HashSet<string> _driven = new HashSet<string>();
        readonly Timer _timer;
        AppConfig _cfg;
        Snapshot _snap = new Snapshot();
        bool _disposed;

        public FanService(AppConfig cfg)
        {
            _cfg = cfg;
            _pc = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsMotherboardEnabled = true,
                IsControllerEnabled = true,
            };
            _pc.Open();
            Tick(null); // primera lectura sincrona para poblar los combos
            _timer = new Timer(Tick, null, cfg.IntervalMs, Timeout.Infinite);
        }

        public Snapshot GetSnapshot() { lock (_lock) return _snap; }

        public void SetConfig(AppConfig cfg)
        {
            lock (_lock)
            {
                _cfg = cfg;
                _smooth.Clear();
            }
        }

        // Fuerza un ventilador al 100% unos segundos para identificar a que canal corresponde.
        public void TestControl(string id, int seconds)
        {
            lock (_lock) _tests[id] = DateTime.UtcNow.AddSeconds(seconds);
        }

        public void CancelTest(string id)
        {
            lock (_lock) _tests.Remove(id);
        }

        void Tick(object state)
        {
            int next = 1000;
            try
            {
                lock (_lock)
                {
                    if (_disposed) return;
                    next = Math.Max(250, _cfg.IntervalMs);
                    Update();
                }
            }
            catch { }
            finally
            {
                try { _timer?.Change(next, Timeout.Infinite); } catch { }
            }
        }

        void Update()
        {
            _pc.Accept(new UpdateVisitor());
            _byId.Clear();
            var snap = new Snapshot();
            foreach (var h in _pc.Hardware) Collect(h, snap.Sensors);

            var driven = new HashSet<string>();

            foreach (var rule in _cfg.Rules)
            {
                if (!rule.Enabled) continue;
                var st = new RuleStatus();
                snap.Rules[rule.Id] = st;

                ISensor ctl;
                if (string.IsNullOrEmpty(rule.ControlId) || !_byId.TryGetValue(rule.ControlId, out ctl) || ctl.Control == null)
                {
                    st.Message = L.T("svc.nooutput");
                    continue;
                }

                ISensor tmp;
                float? t = null;
                if (!string.IsNullOrEmpty(rule.TempId) && _byId.TryGetValue(rule.TempId, out tmp)) t = tmp.Value;

                double pct;
                if (t == null)
                {
                    pct = _cfg.FailsafePercent;
                    st.Message = L.F("svc.noreading", (int)pct);
                }
                else
                {
                    // Sube al instante, baja de a poco: evita que el ventilador oscile.
                    double s;
                    if (!_smooth.TryGetValue(rule.Id, out s) || t.Value >= s) s = t.Value;
                    else s = s + (t.Value - s) * 0.15;
                    _smooth[rule.Id] = s;
                    pct = Curve.Evaluate(rule.Points, s);
                    st.Temp = t;
                }

                st.Percent = Apply(ctl, pct);
                driven.Add(rule.ControlId);
            }

            var expired = new List<string>();
            foreach (var kv in _tests)
            {
                if (kv.Value < DateTime.UtcNow) { expired.Add(kv.Key); continue; }
                ISensor ctl;
                if (_byId.TryGetValue(kv.Key, out ctl) && ctl.Control != null)
                {
                    Apply(ctl, 100);
                    driven.Add(kv.Key);
                }
            }
            foreach (var k in expired) _tests.Remove(k);

            // Lo que antes controlabamos y ya no: devolver al control automatico de la placa.
            foreach (var id in _driven)
            {
                ISensor ctl;
                if (!driven.Contains(id) && _byId.TryGetValue(id, out ctl) && ctl.Control != null)
                    ctl.Control.SetDefault();
            }
            _driven = driven;
            _snap = snap;
        }

        static double Apply(ISensor ctl, double pct)
        {
            double min = ctl.Control.MinSoftwareValue, max = ctl.Control.MaxSoftwareValue;
            if (max <= min) { min = 0; max = 100; }
            pct = Math.Max(min, Math.Min(max, pct));
            ctl.Control.SetSoftware((float)pct);
            return pct;
        }

        void Collect(IHardware h, List<SensorInfo> list)
        {
            foreach (var s in h.Sensors)
            {
                if (s.SensorType != SensorType.Temperature && s.SensorType != SensorType.Fan && s.SensorType != SensorType.Control)
                    continue;
                string id = s.Identifier.ToString();
                _byId[id] = s;
                list.Add(new SensorInfo { Id = id, Hardware = h.Name, Name = s.Name, Type = s.SensorType, Value = s.Value, Mode = s.Control != null ? s.Control.ControlMode.ToString() : null });
            }
            foreach (var sub in h.SubHardware) Collect(sub, list);
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed) return;
                _disposed = true;
                try { _timer?.Dispose(); } catch { }
                foreach (var id in _driven)
                {
                    ISensor ctl;
                    try { if (_byId.TryGetValue(id, out ctl) && ctl.Control != null) ctl.Control.SetDefault(); } catch { }
                }
                try { _pc.Close(); } catch { }
            }
        }
    }
}
