using System;
using System.Collections.Generic;
using System.Linq;
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
        public HardwareType HwType;
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
        const int MaxIntervalMs = 500; // intervalo inicial hasta leer la configuracion
        readonly object _lock = new object();
        readonly Dictionary<string, ISensor> _byId = new Dictionary<string, ISensor>();
        readonly Dictionary<string, double> _smooth = new Dictionary<string, double>();
        readonly Dictionary<string, DateTime> _tests = new Dictionary<string, DateTime>();
        HashSet<string> _driven = new HashSet<string>();
        readonly Timer _timer;
        AppConfig _cfg;
        Snapshot _snap = new Snapshot();
        bool _disposed;
        DateTime _lastUpdate = DateTime.MinValue;
        readonly DefaultCurveStore _defaults = DefaultCurveStore.Load();
        readonly Dictionary<string, DateTime> _released = new Dictionary<string, DateTime>();
        readonly Dictionary<string, double> _lastSpeed = new Dictionary<string, double>();
        readonly Dictionary<string, int> _stableTicks = new Dictionary<string, int>();
        bool _defaultsDirty;
        DateTime _lastDefaultsSave = DateTime.UtcNow;
        readonly CalibrationStore _calibs = CalibrationStore.Load();
        CalibRun _calib;
        CalibStatus _calibStatus = new CalibStatus();

        class CalibRun
        {
            public string ControlId;
            public bool Precise;
            public double[] Duties;
            public int Idx;
            public DateTime StepStart;
            public bool Cancel;
            public List<ISensor> Fans = new List<ISensor>();
            public Dictionary<string, List<double>> Rpms = new Dictionary<string, List<double>>();
        }

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
            _timer = new Timer(Tick, null, MaxIntervalMs, Timeout.Infinite);
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
                    next = _calib != null ? 250 : Math.Max(250, _cfg.IntervalMs); // durante la calibracion se lee rapido
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
            var now = DateTime.UtcNow;
            double dt = _lastUpdate == DateTime.MinValue ? MaxIntervalMs / 1000.0 : (now - _lastUpdate).TotalSeconds;
            dt = Math.Max(0.01, Math.Min(5, dt));
            _lastUpdate = now;

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

                if (_calib != null && rule.ControlId == _calib.ControlId)
                {
                    st.Message = L.T("svc.calibrating");
                    continue;
                }

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
                    // Filtro de 1er orden con tiempo de reaccion distinto para subida y bajada.
                    // "Tiempo de reaccion" T = lo que tarda en cubrir ~95% del cambio (constante de tiempo = T/3).
                    double s;
                    if (!_smooth.TryGetValue(rule.Id, out s)) s = t.Value;
                    else
                    {
                        double react = t.Value >= s ? rule.RiseSeconds : rule.FallSeconds;
                        if (react <= 0) s = t.Value;
                        else s += (t.Value - s) * (1 - Math.Exp(-dt / (react / 3.0)));
                    }
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
                if (_calib != null && kv.Key == _calib.ControlId) continue;
                ISensor ctl;
                if (_byId.TryGetValue(kv.Key, out ctl) && ctl.Control != null)
                {
                    Apply(ctl, 100);
                    driven.Add(kv.Key);
                }
            }
            foreach (var k in expired) _tests.Remove(k);

            AdvanceCalibration(now, snap, driven);

            // Lo que antes controlabamos y ya no: devolver al control automatico de la placa.
            foreach (var id in _driven)
            {
                ISensor ctl;
                if (!driven.Contains(id) && _byId.TryGetValue(id, out ctl) && ctl.Control != null)
                {
                    ctl.Control.SetDefault();
                    _released[id] = now;
                }
            }
            _driven = driven;
            _snap = snap;
            RecordDefaults(now);
        }

        // Mientras una salida no la controla ninguna regla (manda la placa), registra
        // cuanto gira segun la fuente de temperatura de cada regla que la menciona.
        void RecordDefaults(DateTime now)
        {
            // Solo se registra con la velocidad "asentada" (sin cambios en varias lecturas seguidas):
            // asi no se anotan los valores intermedios mientras el ventilador acelera o frena.
            var ctlSeen = new HashSet<string>();
            foreach (var rule in _cfg.Rules)
            {
                if (string.IsNullOrEmpty(rule.ControlId) || !ctlSeen.Add(rule.ControlId)) continue;
                ISensor c;
                double last;
                if (_byId.TryGetValue(rule.ControlId, out c) && c.Value.HasValue)
                {
                    double v = c.Value.Value;
                    int n;
                    _stableTicks.TryGetValue(rule.ControlId, out n);
                    _stableTicks[rule.ControlId] = (_lastSpeed.TryGetValue(rule.ControlId, out last) && Math.Abs(v - last) <= 1) ? n + 1 : 0;
                    _lastSpeed[rule.ControlId] = v;
                }
                else _stableTicks[rule.ControlId] = 0;
            }

            var seen = new HashSet<string>();
            foreach (var rule in _cfg.Rules)
            {
                if (string.IsNullOrEmpty(rule.ControlId) || string.IsNullOrEmpty(rule.TempId)) continue;
                string key = DefaultCurveStore.Key(rule.ControlId, rule.TempId);
                if (!seen.Add(key) || _driven.Contains(rule.ControlId)) continue;

                ISensor ctl, tmp;
                if (!_byId.TryGetValue(rule.ControlId, out ctl) || !_byId.TryGetValue(rule.TempId, out tmp)) continue;
                if (ctl.Control == null || ctl.Control.ControlMode == ControlMode.Software) continue;
                DateTime rel;
                if (_released.TryGetValue(rule.ControlId, out rel) && (now - rel).TotalSeconds < 8) continue; // dejar que la placa retome
                if (!ctl.Value.HasValue || !tmp.Value.HasValue) continue;
                int stable;
                if (!_stableTicks.TryGetValue(rule.ControlId, out stable) || stable < 3) continue;

                ISensor fan;
                double rpm = 0;
                if (_byId.TryGetValue(FanIdFor(rule.ControlId), out fan) && fan.Value.HasValue) rpm = fan.Value.Value;
                _defaults.Add(key, tmp.Value.Value, ctl.Value.Value, rpm);
                _defaultsDirty = true;
            }
            if (_defaultsDirty && (now - _lastDefaultsSave).TotalSeconds >= 30) SaveDefaults(now);
        }

        void SaveDefaults(DateTime now)
        {
            try { _defaults.Save(); _defaultsDirty = false; } catch { }
            _lastDefaultsSave = now;
        }

        // Curva por defecto aprendida para (salida, fuente); vacia si todavia no hay datos suficientes.
        // Si la salida esta calibrada, la velocidad se expresa como el % equivalente a las RPM reales.
        public List<CurvePoint> GetDefaultCurve(string controlId, string tempId)
        {
            lock (_lock)
            {
                Calibration cal;
                _calibs.Outputs.TryGetValue(controlId, out cal);
                bool useRpm = cal != null && cal.IsValid();
                return _defaults.Get(DefaultCurveStore.Key(controlId, tempId), 3)
                    .Select(b => new CurvePoint { Temp = b.T, Percent = (useRpm && b.R > 0) ? cal.EquivalentPercent(b.R) : b.P }).ToList();
            }
        }

        // ---------- calibracion ----------

        public Calibration GetCalibration(string controlId)
        {
            lock (_lock)
            {
                Calibration cal;
                return (controlId != null && _calibs.Outputs.TryGetValue(controlId, out cal) && cal.IsValid()) ? cal : null;
            }
        }

        // Sensor de RPM asociado a una salida: el detectado en la calibracion o, si no hay, el del mismo numero.
        public string GetFanId(string controlId) { lock (_lock) return FanIdFor(controlId); }

        string FanIdFor(string controlId)
        {
            Calibration cal;
            if (controlId != null && _calibs.Outputs.TryGetValue(controlId, out cal) && !string.IsNullOrEmpty(cal.FanId)) return cal.FanId;
            return (controlId ?? "").Replace("/control/", "/fan/");
        }

        public CalibStatus GetCalibStatus() { lock (_lock) return _calibStatus; }

        public bool StartCalibration(string controlId, bool precise, out string error)
        {
            lock (_lock)
            {
                error = null;
                ISensor ctl;
                if (_calib != null) { error = L.T("calib.err.busy"); return false; }
                if (!_byId.TryGetValue(controlId, out ctl) || ctl.Control == null) { error = L.T("svc.nooutput"); return false; }

                var run = new CalibRun { ControlId = controlId, Precise = precise, StepStart = DateTime.UtcNow };
                int n = CalibPlan.StepCount(precise);
                run.Duties = Enumerable.Range(0, n).Select(i => i * CalibPlan.StepPercent(precise)).ToArray();

                // candidatos a tacometro: sensores de RPM del mismo chip/hardware que la salida
                string hw = ctl.Hardware.Identifier.ToString();
                foreach (var s in _byId.Values)
                    if (s.SensorType == SensorType.Fan && s.Hardware.Identifier.ToString() == hw)
                    {
                        run.Fans.Add(s);
                        run.Rpms[s.Identifier.ToString()] = new List<double>();
                    }
                if (run.Fans.Count == 0) { error = L.T("calib.err.nofans"); return false; }

                _calib = run;
                _calibStatus = new CalibStatus { State = "running", ControlId = controlId };
                return true;
            }
        }

        public void CancelCalibration() { lock (_lock) { if (_calib != null) _calib.Cancel = true; } }

        static bool IsCpuOrGpu(HardwareType t)
        {
            return t == HardwareType.Cpu || t == HardwareType.GpuNvidia || t == HardwareType.GpuAmd || t == HardwareType.GpuIntel;
        }

        void AdvanceCalibration(DateTime now, Snapshot snap, HashSet<string> driven)
        {
            var run = _calib;
            if (run == null) return;
            ISensor ctl;
            if (!_byId.TryGetValue(run.ControlId, out ctl) || ctl.Control == null) { FinishCalibration("failed", L.T("svc.nooutput")); return; }

            if (run.Cancel) { FinishCalibration("cancelled", null); return; }

            // corte de seguridad: no seguir si CPU/GPU se calientan
            const double LimitC = 80;
            var hot = snap.Sensors.Where(s => s.Type == SensorType.Temperature && s.Value.HasValue && IsCpuOrGpu(s.HwType) && s.Value.Value < 150)
                                  .OrderByDescending(s => s.Value.Value).FirstOrDefault();
            if (hot != null && hot.Value.Value >= LimitC)
            {
                FinishCalibration("aborted", L.F("calib.err.hot", hot.FullName, hot.Value.Value));
                return;
            }

            double duty = run.Duties[run.Idx];
            Apply(ctl, duty);
            driven.Add(run.ControlId);

            double wait = CalibPlan.WaitSeconds(run.Precise) + (run.Idx == 0 ? CalibPlan.InitialSettleSeconds : 0);
            double elapsed = (now - run.StepStart).TotalSeconds;

            string mainFan = FanIdFor(run.ControlId);
            ISensor shown = run.Fans.FirstOrDefault(f => f.Identifier.ToString() == mainFan) ?? run.Fans[0];
            _calibStatus = new CalibStatus
            {
                State = "running",
                ControlId = run.ControlId,
                CurrentDuty = duty,
                CurrentRpm = shown.Value,
                Progress = Math.Min(1, (run.Idx + Math.Min(1, elapsed / wait)) / run.Duties.Length),
            };

            if (elapsed < wait) return;

            foreach (var f in run.Fans) run.Rpms[f.Identifier.ToString()].Add(f.Value ?? 0);
            run.Idx++;
            run.StepStart = now;
            if (run.Idx >= run.Duties.Length) CompleteCalibration(run);
        }

        void CompleteCalibration(CalibRun run)
        {
            // el tacometro de esta salida es el que mas cambia a lo largo del barrido
            string bestId = null;
            double bestRange = 0;
            foreach (var kv in run.Rpms)
            {
                double range = kv.Value.Max() - kv.Value.Min();
                if (range > bestRange) { bestRange = range; bestId = kv.Key; }
            }
            if (bestId == null || bestRange < 150)
            {
                FinishCalibration("failed", L.T("calib.err.norpm"));
                return;
            }

            var cal = new Calibration
            {
                FanId = bestId,
                Date = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                Mode = run.Precise ? "precise" : "fast",
                Points = run.Duties.Select((d, i) => new CalPoint { Duty = d, Rpm = run.Rpms[bestId][i] }).ToList(),
            };
            _calibs.Outputs[run.ControlId] = cal;
            try { _calibs.Save(); } catch { }

            string tach = _byId.ContainsKey(bestId) ? _byId[bestId].Name : bestId;
            string msg = L.F("calib.done", tach, cal.MaxRpm(), cal.RpmAt(0), cal.FloorPercent());
            FinishCalibration("done", msg);
        }

        void FinishCalibration(string state, string message)
        {
            var run = _calib;
            _calib = null; // la salida deja de estar manejada: el ciclo normal la devuelve a la placa o a su regla
            _calibStatus = new CalibStatus { State = state, ControlId = run != null ? run.ControlId : null, Progress = state == "done" ? 1 : _calibStatus.Progress, Message = message };
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
                list.Add(new SensorInfo { Id = id, Hardware = h.Name, Name = s.Name, Type = s.SensorType, Value = s.Value, HwType = h.HardwareType, Mode = s.Control != null ? s.Control.ControlMode.ToString() : null });
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
                if (_defaultsDirty) SaveDefaults(DateTime.UtcNow);
                try { _pc.Close(); } catch { }
            }
        }
    }
}
