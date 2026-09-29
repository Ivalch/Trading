using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace TradingBacktest.Services
{
    /// <summary>
    /// Tiny key=value store under %LOCALAPPDATA%, used to remember the last data folder,
    /// ticker, cost and period between runs. Written by hand instead of with a serialiser
    /// so the file stays readable and the project keeps its zero-dependency setup.
    /// </summary>
    public sealed class SettingsStore
    {
        private const string FileName = "TradingBacktest.settings";

        private readonly Dictionary<string, string> _values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public SettingsStore()
        {
            Load();
        }

        public static string FilePath
        {
            get
            {
                string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (string.IsNullOrEmpty(root))
                {
                    root = Path.GetDirectoryName(AssemblyEntryPath);
                }

                return Path.Combine(root, "TradingBacktest", FileName);
            }
        }

        private static string AssemblyEntryPath
        {
            get
            {
                try
                {
                    return System.Reflection.Assembly.GetEntryAssembly().Location;
                }
                catch (Exception)
                {
                    return AppDomain.CurrentDomain.BaseDirectory;
                }
            }
        }

        public string Get(string key)
        {
            string value;
            return _values.TryGetValue(key, out value) ? value : null;
        }

        public double GetDouble(string key, double fallback)
        {
            string text = Get(key);
            double value;
            if (text == null || !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                return fallback;
            }

            return value;
        }

        public DateTime? GetDate(string key)
        {
            string text = Get(key);
            DateTime value;
            if (text == null || !DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out value))
            {
                return null;
            }

            return value;
        }

        public void Set(string key, string value)
        {
            if (value == null)
            {
                _values.Remove(key);
                return;
            }

            _values[key] = value;
        }

        public void Set(string key, double value)
        {
            Set(key, value.ToString("R", CultureInfo.InvariantCulture));
        }

        public void Set(string key, DateTime? value)
        {
            Set(key, value.HasValue ? value.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null);
        }

        public void Save()
        {
            try
            {
                string path = FilePath;
                string folder = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder))
                {
                    Directory.CreateDirectory(folder);
                }

                var text = new StringBuilder();
                foreach (KeyValuePair<string, string> pair in _values)
                {
                    text.Append(pair.Key).Append('=').Append(pair.Value).Append('\n');
                }

                File.WriteAllText(path, text.ToString(), Encoding.UTF8);
            }
            catch (Exception)
            {
                // A settings file that cannot be written is not worth interrupting a run for.
            }
        }

        private void Load()
        {
            try
            {
                string path = FilePath;
                if (!File.Exists(path))
                {
                    return;
                }

                string[] lines = File.ReadAllLines(path, Encoding.UTF8);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (line.Length == 0 || line[0] == '#')
                    {
                        continue;
                    }

                    int separator = line.IndexOf('=');
                    if (separator <= 0)
                    {
                        continue;
                    }

                    _values[line.Substring(0, separator).Trim()] = line.Substring(separator + 1).Trim();
                }
            }
            catch (Exception)
            {
                _values.Clear();
            }
        }
    }
}
