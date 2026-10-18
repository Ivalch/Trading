using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;

namespace TradingBacktest.Services
{
    /// <summary>Where one DLL came from, and whether it worked.</summary>
    public sealed class PluginLoadResult
    {
        public PluginLoadResult(string file, string status)
        {
            File = file;
            Status = status;
        }

        public string File { get; private set; }

        /// <summary>"Sma, Ema" on success, or the reason it did not load.</summary>
        public string Status { get; private set; }

        /// <summary>True when the DLL produced nothing usable.</summary>
        public bool Failed
        {
            get { return Status.Length == 0 || Status.IndexOf(',') < 0; }
        }
    }

    /// <summary>
    /// Finds and starts every plug-in of one kind at startup.
    ///
    /// Every DLL matching <c>filePattern</c> in the given directory - and in a sub-folder of
    /// that name, if there is one - is loaded, and every public parameterless type
    /// implementing <typeparamref name="TContract"/> is constructed. That construction is the
    /// only moment the host touches a plug-in type directly. A DLL that will not load, or a
    /// factory whose constructor throws, is reported and skipped: one broken plug-in must
    /// never stop the app from starting.
    ///
    /// <see cref="PluginLoader{TContract}"/> is used twice with different patterns - Ind*.dll
    /// for indicators, Str*.dll for strategies - so the assembly walking, the de-duplication
    /// and the error handling exist once rather than twice.
    ///
    /// This is the composition root. Everything downstream works against the contracts, so
    /// the dependency points from the host to the contracts and back from each plug-in to
    /// the contracts, and neither side references the other.
    /// </summary>
    public sealed class PluginLoader<TContract> where TContract : class
    {
        public const string IndicatorPattern = "Ind*.dll";
        public const string StrategyPattern = "Str*.dll";
        public const string IndicatorFolder = "Indicators";
        public const string StrategyFolder = "Strategies";

        private readonly string _pattern;
        private readonly string _folder;
        private readonly string _label;
        private readonly List<TContract> _plugins = new List<TContract>();
        private readonly List<PluginLoadResult> _results = new List<PluginLoadResult>();

        public PluginLoader(string filePattern, string folderName, string label)
        {
            _pattern = filePattern;
            _folder = folderName;
            _label = label;
        }

        /// <summary>One factory per plug-in found, ordered by name then id.</summary>
        public IList<TContract> Plugins
        {
            get { return _plugins; }
        }

        /// <summary>One line per DLL, for the status bar.</summary>
        public IList<PluginLoadResult> Results
        {
            get { return _results; }
        }

        public void Load()
        {
            Load(AppDomain.CurrentDomain.BaseDirectory);
        }

        /// <summary>
        /// Loads every matching DLL under <paramref name="directory"/>. Public so a test or a
        /// tool can point it somewhere else.
        /// </summary>
        public void Load(string directory)
        {
            _plugins.Clear();
            _results.Clear();

            IList<string> files = FindFiles(directory, _pattern, _folder);
            for (int i = 0; i < files.Count; i++)
            {
                StartAssembly(files[i]);
            }

            _plugins.Sort(Compare);
        }

        /// <summary>The DLLs that will be tried, de-duplicated by file name.</summary>
        public static IList<string> FindFiles(string directory, string pattern, string folderName)
        {
            var files = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            AddFrom(directory, pattern, files, seen);

            if (!string.IsNullOrEmpty(directory) && !string.IsNullOrEmpty(folderName))
            {
                AddFrom(Path.Combine(directory, folderName), pattern, files, seen);
            }

            files.Sort(StringComparer.OrdinalIgnoreCase);
            return files;
        }

        private static void AddFrom(string directory, string pattern, List<string> files, HashSet<string> seen)
        {
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                return;
            }

            string[] found;
            try
            {
                found = Directory.GetFiles(directory, pattern);
            }
            catch (Exception)
            {
                return;
            }

            for (int i = 0; i < found.Length; i++)
            {
                if (seen.Add(Path.GetFileName(found[i])))
                {
                    files.Add(found[i]);
                }
            }
        }

        private static void StartAssembly(string file, string pattern, string label, List<TContract> plugins, List<PluginLoadResult> results)
        {
            string fileName = Path.GetFileName(file);

            Assembly assembly;
            try
            {
                assembly = Assembly.LoadFrom(file);
            }
            catch (Exception ex)
            {
                results.Add(new PluginLoadResult(fileName, Flatten(ex)));
                return;
            }

            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                // Partially loadable: use whatever did load, and say so.
                types = ex.Types ?? new Type[0];
                results.Add(new PluginLoadResult(fileName, "loaded in part: " + Flatten(ex)));
            }
            catch (Exception ex)
            {
                results.Add(new PluginLoadResult(fileName, Flatten(ex)));
                return;
            }

            var started = new List<string>();

            for (int i = 0; i < types.Length; i++)
            {
                Type type = types[i];
                if (!IsContract(type))
                {
                    continue;
                }

                try
                {
                    // "Start its constructor": the only place the host touches a plug-in type.
                    var factory = (TContract)Activator.CreateInstance(type);
                    if (factory == null || string.IsNullOrEmpty(Describe(factory)))
                    {
                        results.Add(new PluginLoadResult(fileName, type.FullName + " has no Id."));
                        continue;
                    }

                    if (Contains(plugins, factory))
                    {
                        results.Add(new PluginLoadResult(fileName, type.FullName + " is a duplicate Id."));
                        continue;
                    }

                    plugins.Add(factory);
                    started.Add(Label(factory));
                }
                catch (Exception ex)
                {
                    results.Add(new PluginLoadResult(fileName, type.FullName + ": " + Flatten(ex)));
                }
            }

            results.Add(new PluginLoadResult(fileName, started.Count == 0 ? "no " + label : string.Join(", ", started.ToArray())));
        }

        private void StartAssembly(string file)
        {
            StartAssembly(file, _pattern, _label, _plugins, _results);
        }

        private static bool IsContract(Type type)
        {
            if (type == null || !type.IsClass || type.IsAbstract || type.IsInterface)
            {
                return false;
            }

            // A public parameterless constructor, otherwise Activator would fail anyway.
            return type.GetConstructor(Type.EmptyTypes) != null && typeof(TContract).IsAssignableFrom(type);
        }

        private static bool Contains(List<TContract> plugins, TContract candidate)
        {
            string id = Describe(candidate);
            for (int i = 0; i < plugins.Count; i++)
            {
                if (string.Equals(Describe(plugins[i]), id, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static int Compare(TContract a, TContract b)
        {
            int byLabel = string.Compare(Label(a), Label(b), StringComparison.OrdinalIgnoreCase);
            return byLabel != 0 ? byLabel : string.Compare(Describe(a), Describe(b), StringComparison.OrdinalIgnoreCase);
        }

        // Both contracts expose Id and Name, but nothing forces them to, so ask by name and
        // fall back to an empty string rather than casting.
        private static string Describe(TContract plugin)
        {
            PropertyInfo id = plugin.GetType().GetProperty("Id");
            return id == null ? string.Empty : Convert.ToString(id.GetValue(plugin, null), CultureInfo.InvariantCulture);
        }

        private static string Label(TContract plugin)
        {
            PropertyInfo name = plugin.GetType().GetProperty("Name");
            return name == null ? string.Empty : Convert.ToString(name.GetValue(plugin, null), CultureInfo.InvariantCulture);
        }

        private static string Flatten(Exception ex)
        {
            if (ex == null)
            {
                return "unknown error";
            }

            Exception root = ex;
            while (root.InnerException != null)
            {
                root = root.InnerException;
            }

            string message = root.Message ?? string.Empty;
            return message.Length == 0 ? root.GetType().Name : message;
        }

        /// <summary>"4 indicators: Bollinger, EMA, RSI, SMA" - or "no indicators loaded".</summary>
        public string Summary()
        {
            if (_plugins.Count == 0)
            {
                return "no " + _label + (_label.EndsWith("s", StringComparison.Ordinal) ? string.Empty : "s") + " loaded";
            }

            var names = new List<string>(_plugins.Count);
            for (int i = 0; i < _plugins.Count; i++)
            {
                names.Add(Label(_plugins[i]));
            }

            return _plugins.Count.ToString(CultureInfo.CurrentCulture)
                + " " + _label + (_plugins.Count == 1 ? ": " : "s: ")
                + string.Join(", ", names.ToArray());
        }

        /// <summary>One line per DLL that did not yield anything usable.</summary>
        public string Failures()
        {
            var problems = new List<string>();
            for (int i = 0; i < _results.Count; i++)
            {
                if (_results[i].Failed)
                {
                    problems.Add(_results[i].File + " - " + _results[i].Status);
                }
            }

            return string.Join("; ", problems.ToArray());
        }
    }
}