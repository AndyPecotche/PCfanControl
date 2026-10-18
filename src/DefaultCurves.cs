using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace FanCurve
{
    public class SpeedBin
    {
        public int T { get; set; }      // temperatura (°C, redondeada)
        public double P { get; set; }   // ultima velocidad estable observada (%)
        public int N { get; set; }      // veces que se registro este punto
    }

    // Registro de la curva "de fabrica": cuanto gira una salida segun una fuente de temperatura
    // mientras NINGUNA regla de FanCurve la controla (es decir, manda la placa).
    public class DefaultCurveStore
    {
        const int MaxCount = 100000;

        public Dictionary<string, List<SpeedBin>> Pairs { get; set; } = new Dictionary<string, List<SpeedBin>>();

        static string FilePath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FanCurve", "default-curves.json"); }
        }

        public static string Key(string controlId, string tempId) { return controlId + "|" + tempId; }

        public static DefaultCurveStore Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var s = new JavaScriptSerializer().Deserialize<DefaultCurveStore>(File.ReadAllText(FilePath));
                    if (s != null && s.Pairs != null) return s;
                }
            }
            catch { }
            return new DefaultCurveStore();
        }

        public void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            File.WriteAllText(FilePath, new JavaScriptSerializer().Serialize(this));
        }

        public void Add(string key, double temp, double percent)
        {
            List<SpeedBin> bins;
            if (!Pairs.TryGetValue(key, out bins)) Pairs[key] = bins = new List<SpeedBin>();
            int t = (int)Math.Round(temp);
            var bin = bins.FirstOrDefault(b => b.T == t);
            if (bin == null) { bin = new SpeedBin { T = t, N = 0 }; bins.Add(bin); }
            bin.N = Math.Min(bin.N + 1, MaxCount);
            bin.P = percent; // sin promediar: si el firmware cambia, el valor nuevo reemplaza al anterior de inmediato
        }

        public List<CurvePoint> Get(string key, int minSamples)
        {
            List<SpeedBin> bins;
            if (!Pairs.TryGetValue(key, out bins)) return new List<CurvePoint>();
            return bins.Where(b => b.N >= minSamples).OrderBy(b => b.T)
                       .Select(b => new CurvePoint { Temp = b.T, Percent = b.P }).ToList();
        }
    }
}
