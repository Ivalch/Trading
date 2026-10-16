using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using TradingBacktest.Abstractions;

namespace TradingBacktest.Services
{
    /// <summary>Where a plug-in came from, and whether it worked.</summary>
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

        public bool Failed
        {
            get { return Status.Length > 0 && Status.IndexOf(',') < 0; }
        }
    }

    /// <summary>
    /// Finds and starts every indicator plug-in at startup.
    ///
    /// Every <c>Ind*.dll</c> beside the executable - and in an "Indicators" sub-folder if
    /// there is one - is loaded, and every public type implementing
    /// <see cref="IIndicatorFactory"/> is constructed. A DLL that will not load, or a factory
    /// whose constructor throws, is reported and skipped: one broken plug-in must never stop
    /// the app from starting.
    ///
    /// This is the composition root and the only place in the app that knows plug-ins exist.
    /// Everything downstream works against <see cref="IIndicatorFactory"/>, so the dependency
    /// points from the host to the contracts and back from the plug-ins to the contracts -
    /// neither side references the other. Adding an indicator means dropping a DLL in the
    /// folder; no project file, no registration, no edit to this class.
    /// </summary>
    public sealed class IndicatorPluginLoader
    {
        public const string FilePattern = "Ind*.dll";
        public const string PluginFolder = "Indicators";

        private readonly List<IIndicatorFactory> _factories = new List<IIndicatorFactory>();
        private readonly List<PluginLoadResult> _results = new List<PluginLoadResult>();

        /// <summary>One factory per indicator found, ordered by file name then type name.</summary>
        public IList<IIndicatorFactory> Factories
        {
            get { return _factories; }
        }

        /// <summary>One line per DLL, for the status bar.</summary>
        public IList<PluginLoadResult> Results
        {
            get { return _results; }
        }

        /// <summary>Loads the plug-ins. Safe to call once at startup.</summary>
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
            _factories.Clear();
            _results.Clear();

            foreach (string file in FindPluginFiles(directory))
            {
                StartAssembly(file);
            }

            _factories.Sort(delegate (IIndicatorFactory a, IIndicatorFactory b)
            {
                int byName = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                return byName != 0 ? byName : string.Compare(a.Id, b.Id, StringComparison.OrdinalIgnoreCase);
            });
        }

        /// <summary>The DLLs that will be tried, de-duplicated by file name.</summary>
        public static IList<string> FindPluginFiles(string directory)
        {
            var files = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            AddFrom(directory, files, seen);

            string sub = string.IsNullOrEmpty(directory) ? null : Path.Combine(directory, PluginFolder);
            if (sub != null && Directory.Exists(sub))
            {
                AddFrom(sub, files, seen);
            }

            files.Sort(StringComparer.OrdinalIgnoreCase);
            return files;
        }

        private static void AddFrom(string directory, List<string> files, HashSet<string> seen)
        {
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                return;
            }

            string[] found;
            try
            {
                found = Directory.GetFiles(directory, FilePattern);
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

        private void StartAssembly(string file)
        {
            string fileName = Path.GetFileName(file);

            Assembly assembly;
            try
            {
                assembly = Assembly.LoadFrom(file);
            }
            catch (Exception ex)
            {
                _results.Add(new PluginLoadResult(fileName, Flatten(ex)));
                return;
            }

            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                // Partially loadable: use whatever did load and note the rest.
                types = ex.Types ?? new Type[0];
            }
            catch (Exception ex)
            {
                _results.Add(new PluginLoadResult(fileName, Flatten(ex)));
                return;
            }

            var started = new List<string>();

            for (int i = 0; i < types.Length; i++)
            {
                Type type = types[i];
                if (!IsFactory(type))
                {
                    continue;
                }

                try
                {
                    // "Start its constructor": this is the only moment the host touches a
                    // plug-in type directly.
                    var factory = (IIndicatorFactory)Activator.CreateInstance(type);
                    if (factory == null || string.IsNullOrEmpty(factory.Id))
                    {
                        _results.Add(new PluginLoadResult(fileName, type.FullName + " has no Id."));
                        continue;
                    }

                    if (_factories.Contains(factory))
                    {
                        _results.Add(new PluginLoadResult(fileName, type.FullName + " is a duplicate Id."));
                        continue;
                    }

                    _factories.Add(factory);
                    started.Add(factory.Name);
                }
                catch (Exception ex)
                {
                    _results.Add(new PluginLoadResult(fileName, type.FullName + ": " + Flatten(ex)));
                }
            }

            _results.Add(new PluginLoadResult(fileName, started.Count == 0 ? "no IIndicatorFactory" : string.Join(", ", started.ToArray())));
        }

        private static bool IsFactory(Type type)
        {
            if (type == null || !type.IsClass || type.IsAbstract || type.IsInterface)
            {
                return false;
            }

            // A public parameterless constructor, otherwise Activator would fail anyway.
            return type.GetConstructor(Type.EmptyTypes) != null
                && typeof(IIndicatorFactory).IsAssignableFrom(type);
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

        /// <summary>"4 plug-ins: Bollinger, EMA, RSI, SMA" - the status line.</summary>
        public string Summary()
        {
            if (_factories.Count == 0)
            {
                return "no indicators loaded";
            }

            var names = new List<string>();
            for (int i = 0; i < _factories.Count; i++)
            {
                names.Add(_factories[i].Name);
            }

            return _factories.Count.ToString(CultureInfo.CurrentCulture)
                + (_factories.Count == 1 ? " indicator: " : " indicators: ")
                + string.Join(", ", names.ToArray());
        }
    }
}