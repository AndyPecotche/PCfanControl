using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace FanCurve
{
    public class CalPoint
    {
        public double Duty { get; set; }   // % escrito en la salida
        public double Rpm { get; set; }    // RPM medidas una vez asentado el ventilador
    }

    // Tabla duty -> RPM de una salida, medida con un barrido de calibracion.
    public class Calibration
    {
        public string FanId { get; set; }     // sensor de RPM que responde a esta salida
        public string Date { get; set; }
        public string Mode { get; set; }      // "precise" | "fast"
        public List<CalPoint> Points { get; set; } = new List<CalPoint>();

        // Envolvente monotona (RPM creciente con el duty) para poder invertir la tabla pese al ruido.
        List<CalPoint> Env()
        {
            var env = new List<CalPoint>();
            double max = 0;
            foreach (var p in Points.OrderBy(x => x.Duty))
            {
                max = Math.Max(max, p.Rpm);
                env.Add(new CalPoint { Duty = p.Duty, Rpm = max });
            }
            return env;
        }

        static double Tol(List<CalPoint> env)
        {
            double range = env[env.Count - 1].Rpm - env[0].Rpm;
            return Math.Max(15, 0.015 * range);
        }

        public bool IsValid() { return Points != null && Points.Count >= 2; }

        public double MaxRpm() { var e = Env(); return e[e.Count - 1].Rpm; }

        // RPM esperadas para un duty dado.
        public double RpmAt(double duty)
        {
            var e = Env();
            if (duty <= e[0].Duty) return e[0].Rpm;
            if (duty >= e[e.Count - 1].Duty) return e[e.Count - 1].Rpm;
            for (int i = 1; i < e.Count; i++)
            {
                if (duty <= e[i].Duty)
                {
                    double span = e[i].Duty - e[i - 1].Duty;
                    double f = span <= 0 ? 1 : (duty - e[i - 1].Duty) / span;
                    return e[i - 1].Rpm + f * (e[i].Rpm - e[i - 1].Rpm);
                }
            }
            return e[e.Count - 1].Rpm;
        }

        // Duty mas alto que todavia no cambia las RPM respecto de 0% (el "piso" del ventilador).
        public double FloorPercent()
        {
            var e = Env();
            double tol = Tol(e);
            int i = 0;
            while (i + 1 < e.Count && e[i + 1].Rpm <= e[0].Rpm + tol) i++;
            return e[i].Duty;
        }

        // El ventilador se detiene del todo con duty bajo (RPM ~ 0 en el primer punto).
        public bool CanStop() { return Env()[0].Rpm < 30; }

        // Duty "equivalente" a unas RPM medidas. En la zona plana del piso devuelve el extremo alto de la meseta.
        public double EquivalentPercent(double rpm)
        {
            var e = Env();
            double tol = Tol(e);
            if (rpm <= e[0].Rpm + tol) return CanStop() ? 0 : FloorPercent();
            int i = 0;
            while (i + 1 < e.Count && e[i + 1].Rpm <= rpm + tol) i++;
            if (i + 1 >= e.Count) return e[e.Count - 1].Duty;
            double d = e[i + 1].Rpm - e[i].Rpm;
            double f = d <= 0 ? 0 : Math.Max(0, Math.Min(1, (rpm - e[i].Rpm) / d));
            return e[i].Duty + f * (e[i + 1].Duty - e[i].Duty);
        }
    }

    public class CalibrationStore
    {
        public Dictionary<string, Calibration> Outputs { get; set; } = new Dictionary<string, Calibration>();

        static string FilePath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FanCurve", "calibrations.json"); }
        }

        public static CalibrationStore Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var s = new JavaScriptSerializer().Deserialize<CalibrationStore>(File.ReadAllText(FilePath));
                    if (s != null && s.Outputs != null) return s;
                }
            }
            catch { }
            return new CalibrationStore();
        }

        public void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            File.WriteAllText(FilePath, new JavaScriptSerializer().Serialize(this));
        }
    }

    // Parametros del barrido (los tiempos se muestran al usuario antes de empezar).
    public static class CalibPlan
    {
        public const double InitialSettleSeconds = 6;   // el ventilador viene de otra velocidad

        public static double StepPercent(bool precise) { return precise ? 1 : 5; }
        public static double WaitSeconds(bool precise) { return precise ? 4 : 5; }
        public static int StepCount(bool precise) { return (int)(100 / StepPercent(precise)) + 1; }

        public static double TotalSeconds(bool precise)
        {
            return InitialSettleSeconds + StepCount(precise) * WaitSeconds(precise);
        }

        public static string Format(double seconds)
        {
            int s = (int)Math.Round(seconds);
            return s >= 60 ? L.F("calib.minsec", s / 60, s % 60) : L.F("calib.sec", s);
        }
    }

    public class CalibStatus
    {
        public string State = "idle";   // idle | running | done | failed | aborted | cancelled
        public string ControlId;
        public double Progress;         // 0..1
        public double CurrentDuty;
        public double? CurrentRpm;
        public string Message;          // detalle para done / failed / aborted
    }
}
