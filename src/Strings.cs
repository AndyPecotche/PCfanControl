using System.Collections.Generic;
using System.Globalization;

namespace FanCurve
{
    // Textos de la interfaz en ingles (indice 0) y espanol (indice 1).
    public static class L
    {
        public static readonly string[] Languages = { "en", "es" };
        public static readonly string[] LanguageNames = { "English", "Español" };

        static int _lang;

        public static string Lang
        {
            get { return Languages[_lang]; }
            set { int i = System.Array.IndexOf(Languages, value); _lang = i < 0 ? 0 : i; }
        }

        // Idioma guardado o, si no hay, el del sistema.
        public static string Detect(string saved)
        {
            if (!string.IsNullOrEmpty(saved) && System.Array.IndexOf(Languages, saved) >= 0) return saved;
            return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "es" ? "es" : "en";
        }

        public static string T(string key)
        {
            string[] v;
            return D.TryGetValue(key, out v) ? v[_lang] : key;
        }

        public static string F(string key, params object[] args)
        {
            return string.Format(T(key), args);
        }

        static readonly Dictionary<string, string[]> D = new Dictionary<string, string[]>
        {
            { "tab.rules",       new[] { "Rules", "Reglas" } },
            { "tab.sensors",     new[] { "Sensors", "Sensores" } },
            { "btn.save",        new[] { "Save and apply", "Guardar y aplicar" } },
            { "chk.autostart",   new[] { "Start with Windows (in the background)", "Iniciar con Windows (en segundo plano)" } },
            { "msg.autostart",   new[] { "Could not modify the startup task.", "No se pudo modificar la tarea de inicio." } },
            { "lbl.language",    new[] { "Language", "Idioma" } },
            { "btn.add",         new[] { "Add rule", "Agregar regla" } },
            { "btn.remove",      new[] { "Remove", "Quitar" } },
            { "chk.enabled",     new[] { "Rule enabled", "Regla activa" } },
            { "lbl.name",        new[] { "Name", "Nombre" } },
            { "lbl.output",      new[] { "Output (fan)", "Salida (ventilador)" } },
            { "lbl.tempsource",  new[] { "Temperature source", "Fuente de temperatura" } },
            { "lbl.hint",        new[] { "Drag points to move them · double-click to add one · right-click a point to remove it",
                                         "Arrastrá los puntos · doble clic para agregar uno · clic derecho sobre un punto para quitarlo" } },
            { "lbl.rise",        new[] { "Reaction to rising temp", "Reacción a subida de temp." } },
            { "lbl.fall",        new[] { "Reaction to falling temp", "Reacción a bajada de temp." } },
            { "react.0",         new[] { "Immediate", "Inmediato" } },
            { "react.0.5",       new[] { "0.5 s", "0,5 s" } },
            { "react.1",         new[] { "1 s", "1 s" } },
            { "react.2",         new[] { "2 s", "2 s" } },
            { "chk.showdefault", new[] { "Show default curve (dotted black line, learned while the board controls the fan)",
                                         "Mostrar curva por defecto (línea negra punteada, aprendida mientras la placa controla el ventilador)" } },
            { "status.nodefault", new[] { "No default-curve data yet for this output and source.", "Aún no hay datos de la curva por defecto para esta salida y fuente." } },
            { "lbl.sampling",    new[] { "Temperature sampling rate", "Frecuencia de muestreo" } },
            { "sample.250",      new[] { "0.25 s", "0,25 s" } },
            { "sample.500",      new[] { "0.5 s", "0,5 s" } },
            { "sample.1000",     new[] { "1 s", "1 s" } },
            { "sample.2000",     new[] { "2 s", "2 s" } },
            { "sample.5000",     new[] { "5 s", "5 s" } },
            { "col.type",        new[] { "Type", "Tipo" } },
            { "col.value",       new[] { "Value", "Valor" } },
            { "col.mode",        new[] { "Mode", "Modo" } },
            { "type.temp",       new[] { "Temp", "Temp" } },
            { "type.fan",        new[] { "Fan", "Ventilador" } },
            { "type.control",    new[] { "Control", "Control" } },
            { "btn.test",        new[] { "Test output at 100% (8 s)", "Probar salida al 100% (8 s)" } },
            { "btn.canceltest",  new[] { "Cancel test", "Cancelar prueba" } },
            { "lbl.sensortip",   new[] { "Select a \"Control\" row and watch which fan (RPM) reacts. \"Mode\" should switch to Software.",
                                         "Elegí una fila «Control» y mirá qué ventilador (RPM) reacciona. «Modo» debe pasar a Software." } },
            { "tray.open",       new[] { "Open", "Abrir" } },
            { "tray.exit",       new[] { "Exit (fans back to automatic)", "Salir (ventiladores a automático)" } },
            { "item.unavailable",new[] { "(not available) ", "(no disponible) " } },
            { "rule.new",        new[] { "Rule {0}", "Regla {0}" } },
            { "rule.inactive",   new[] { " (inactive)", " (inactiva)" } },
            { "conflict.msg",    new[] { "The output \"{0}\" already has an active rule: \"{1}\".\n\nIf you enable \"{2}\", \"{1}\" will be disabled (it stays saved, but inactive).\n\nContinue?",
                                         "La salida «{0}» ya tiene una regla activa: «{1}».\n\nSi activás «{2}», se desactivará «{1}» (queda guardada, pero inactiva).\n\n¿Continuar?" } },
            { "conflict.title",  new[] { "Output already in use", "Salida ya en uso" } },
            { "dup.msg",         new[] { "The output \"{0}\" has more than one active rule. Keep only one enabled.",
                                         "La salida «{0}» tiene más de una regla activa. Dejá solo una activa." } },
            { "danger.title",    new[] { "Dangerous configuration", "Configuración peligrosa" } },
            { "danger.head",     new[] { "DANGER: fan speed below 30% above 70 °C", "PELIGRO: velocidad menor al 30% por encima de 70 °C" } },
            { "danger.list",     new[] { "These rules leave the fan below 30% at temperatures above 70 °C:",
                                         "Estas reglas dejan el ventilador por debajo del 30% con más de 70 °C:" } },
            { "danger.advice",   new[] { "Check which fan you are assigning this curve to. If it belongs to a GPU or CPU, this setting could be dangerous (overheating) or reduce performance. If it is a case fan, it is safe to continue.",
                                         "Revisá el ventilador al que estás asignando la curva. Si es el de una GPU o un CPU, esta configuración podría ser peligrosa (sobrecalentamiento) o reducir el rendimiento. Si es un ventilador de gabinete, es seguro continuar." } },
            { "btn.understand",  new[] { "I understand", "Entiendo" } },
            { "btn.cancel",      new[] { "Cancel", "Cancelar" } },
            { "status.cancelled",new[] { "Cancelled: the configuration was not applied.", "Cancelado: la configuración no se aplicó." } },
            { "status.applied",  new[] { "Configuration applied.", "Configuración aplicada." } },
            { "status.notapplied", new[] { "Rule not applied — blue line: current output speed", "Regla no aplicada — línea azul: velocidad actual de la salida" } },
            { "status.dirty",    new[] { "⚠ Unsaved changes (not applied)", "⚠ Cambios sin guardar (no aplicados)" } },
            { "status.now",     new[] { "Now: {0:0.#} °C  →  {1:0} %", "Ahora: {0:0.#} °C  →  {1:0} %" } },
            { "msg.savefail",    new[] { "Could not save the configuration:\n", "No se pudo guardar la configuración:\n" } },
            { "msg.hwfail",      new[] { "Could not start hardware monitoring:\n\n", "No se pudo iniciar la lectura de hardware:\n\n" } },
            { "svc.nooutput",    new[] { "Fan output not available", "Salida de ventilador no disponible" } },
            { "svc.noreading",   new[] { "No temperature reading, using {0}%", "Sin lectura de temperatura, usando {0}%" } },
        };
    }
}
