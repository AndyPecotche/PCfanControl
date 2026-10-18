using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace FanCurve
{
    public class CurvePoint
    {
        public double Temp { get; set; }
        public double Percent { get; set; }
    }

    public class FanRule
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "New rule";
        public bool Enabled { get; set; } = true;
        public string ControlId { get; set; } = "";
        public string TempId { get; set; } = "";
        // Tiempo de reaccion (s) ante subida / bajada de temperatura. 0 = inmediato.
        public double RiseSeconds { get; set; } = 0;
        public double FallSeconds { get; set; } = 2;
        public List<CurvePoint> Points { get; set; } = DefaultPoints();

        public override string ToString() { return Enabled ? Name : Name + L.T("rule.inactive"); }

        public static List<CurvePoint> DefaultPoints()
        {
            return new List<CurvePoint>
            {
                new CurvePoint { Temp = 30, Percent = 30 },
                new CurvePoint { Temp = 50, Percent = 40 },
                new CurvePoint { Temp = 65, Percent = 60 },
                new CurvePoint { Temp = 75, Percent = 80 },
                new CurvePoint { Temp = 85, Percent = 100 },
            };
        }
    }

    public class AppConfig
    {
        public List<FanRule> Rules { get; set; } = new List<FanRule>();
        public int IntervalMs { get; set; } = 500;
        // Velocidad a usar si la fuente de temperatura deja de dar lecturas.
        public double FailsafePercent { get; set; } = 100;
        // "en" / "es"; vacio = idioma del sistema.
        public string Language { get; set; }
        // Preferencia de vista: dibujar la curva por defecto de la placa en el grafico.
        public bool ShowDefaultCurve { get; set; }

        static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FanCurve");
        static string FilePath => Path.Combine(Dir, "config.json");

        public static AppConfig Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var cfg = new JavaScriptSerializer().Deserialize<AppConfig>(File.ReadAllText(FilePath));
                    if (cfg != null && cfg.Rules != null) return cfg;
                }
            }
            catch { }
            return new AppConfig();
        }

        public void Save()
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, new JavaScriptSerializer().Serialize(this));
        }

        // Guarda solo el idioma, sin tocar las reglas guardadas.
        public static void SaveLanguage(string lang)
        {
            var c = Load();
            c.Language = lang;
            c.Save();
        }

        // Guarda una preferencia de interfaz sin tocar las reglas guardadas.
        public static void UpdatePrefs(Action<AppConfig> mutate)
        {
            var c = Load();
            mutate(c);
            c.Save();
        }

        public AppConfig Clone()
        {
            var js = new JavaScriptSerializer();
            return js.Deserialize<AppConfig>(js.Serialize(this));
        }
    }

    public static class Curve
    {
        public static double Evaluate(IList<CurvePoint> points, double temp)
        {
            var p = points.OrderBy(x => x.Temp).ToList();
            if (p.Count == 0) return 100;
            if (temp <= p[0].Temp) return p[0].Percent;
            if (temp >= p[p.Count - 1].Temp) return p[p.Count - 1].Percent;
            for (int i = 1; i < p.Count; i++)
            {
                if (temp <= p[i].Temp)
                {
                    double span = p[i].Temp - p[i - 1].Temp;
                    if (span <= 0) return p[i].Percent;
                    double f = (temp - p[i - 1].Temp) / span;
                    return p[i - 1].Percent + f * (p[i].Percent - p[i - 1].Percent);
                }
            }
            return p[p.Count - 1].Percent;
        }
    }
}
