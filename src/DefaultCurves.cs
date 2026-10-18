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
        public double P { get; set; }   // velocidad media observada (%)
        public int N { get; set; }      // muestras acumuladas (tope para que siga adaptandose)
    }

    // Registro de la curva "de fabrica": cuanto gira una salida segun una fuente de temperatura
    // mientras NINGUNA regla de FanCurve la controla (es decir, manda la placa).
    public class DefaultCurveStore
    {
        const int MaxSamplesPerBin = 200;

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
            if (bin == null) { bin = new SpeedBin { T = t, P = percent, N = 0 }; bins.Add(bin); }
            bin.N = Math.Min(bin.N + 1, MaxSamplesPerBin);
            bin.P += (percent - bin.P) / bin.N; // media incremental
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
