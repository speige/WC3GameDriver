using Microsoft.Win32;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Reflection;
using War3Net.Build;
using War3Net.IO.Mpq;

namespace WC3GameDriver
{

    public partial class WC3LuaBridge : IDisposable
    {
        const string REGISTRY_KEY = @"HKEY_CURRENT_USER\SOFTWARE\Blizzard Entertainment\Warcraft III";
        const string REGISTRY_VALUE = "Allow Local Files";

        protected static class SCRIPT_TEMPLATE_REPLACEMENTS
        {
            public static string UNIQUE_SAFETY_PREFIX = "WC3GameDriver_WC3LuaBridge";
            public static string PRELOAD_INPUT_FILENAME_PREFIX = "PreloadInput_";
            public static string PRELOAD_OUTPUT_FILENAME_PREFIX = "PreloadOutput_";
            public static string PRELOAD_FILE_EXTENSION = ".txt";
        }

        protected static string _instrumentationLuaScript;
        protected static string _preloadScript;

        public delegate void LuaResponseHandler(int fileCounter, string response);
        public event LuaResponseHandler LuaResponseReceived;

        protected Guid _gameInstance;
        protected string PreloadFolderName
        {
            get
            {
                return SCRIPT_TEMPLATE_REPLACEMENTS.UNIQUE_SAFETY_PREFIX + _gameInstance;
            }
        }

        protected string FullPreloadFolderPath
        {
            get
            {
                return Path.Combine(_customDataFolder, PreloadFolderName);
            }
        }

        protected FileSystemWatcher _watcher;
        protected readonly string _gameExePath;
        protected readonly string _customDataFolder;
        protected readonly string _preloadBridge_AbilityCode;
        protected object _oldEnableLocalFiles = null;
        protected readonly string _originalScript;
        protected readonly MpqArchive _originalArchive;
        protected ConcurrentHashSet<int> _alreadyProcessedResponses = new ConcurrentHashSet<int>();

        protected int _preloadFileCounter = -1;
        protected Process _process;
        protected string _instrumentedMapFileName;
        public bool BridgeEstablished { get; protected set; }
        protected TaskCompletionSource<bool> _bridgeEstablishedTcs;

        static WC3LuaBridge()
        {
            _instrumentationLuaScript = File.ReadAllText("WC3LuaBridge.lua");
            _preloadScript = File.ReadAllText("Preload.j");
        }

        protected string ReplaceScriptTemplateVariables(string instrumentationScript)
        {
            var script = instrumentationScript;
            foreach (var property in typeof(SCRIPT_TEMPLATE_REPLACEMENTS).GetFields(BindingFlags.Static | BindingFlags.Public))
            {
                script = script.Replace("{{" + property.Name + "}}", property.GetValue(null)?.ToString());
            }

            script = script.Replace("{{PreloadFolderName}}", PreloadFolderName);
            script = script.Replace("{{_preloadBridge_AbilityCode}}", _preloadBridge_AbilityCode);
            return script;
        }

        public async Task RestartGameAsync(int? timeoutMilliseconds = null)
        {
            _watcher.EnableRaisingEvents = false;
            BridgeEstablished = false;
            _alreadyProcessedResponses = new ConcurrentHashSet<int>();
            _bridgeEstablishedTcs = new TaskCompletionSource<bool>();

            KillProcess();

            _gameInstance = Guid.NewGuid();
            Directory.CreateDirectory(FullPreloadFolderPath);
            _watcher.Path = FullPreloadFolderPath;
            _watcher.EnableRaisingEvents = true;

            //todo: generate custom ability code & inject into ObjectEditor data, instead of using preloadBridge_AbilityCode            

            _instrumentedMapFileName = Path.ChangeExtension(Path.GetTempFileName(), ".w3x");

            var script = _originalScript + ReplaceScriptTemplateVariables(_instrumentationLuaScript);
            using (var stream = new MemoryStream())
            {
                var writer = new StreamWriter(stream);
                writer.Write(script);
                writer.Flush();
                stream.Position = 0;
                var builder = new MpqArchiveBuilder(_originalArchive);
                builder.AddFile(MpqFile.New(stream, "war3map.lua"));
                builder.SaveTo(_instrumentedMapFileName);
            }

            _oldEnableLocalFiles ??= Registry.GetValue(REGISTRY_KEY, REGISTRY_VALUE, null);
            Registry.SetValue(REGISTRY_KEY, REGISTRY_VALUE, 1);

            _preloadFileCounter = -1;

            _process = Utils.ExecuteCommand(_gameExePath, $"-launch -nowfpause -loadfile \"{_instrumentedMapFileName}\"");

            var completedTask = await Task.WhenAny(
                _bridgeEstablishedTcs.Task,
                Task.Delay(timeoutMilliseconds ?? int.MaxValue)
            );

            if (completedTask != _bridgeEstablishedTcs.Task)
            {
                throw new TimeoutException("Timed out waiting for WC3LuaBridge to be established.");
            }

            BridgeEstablished = _bridgeEstablishedTcs.Task.Result;
            if (!BridgeEstablished)
            {
                KillProcess();
                throw new Exception("Failed to establish WC3LuaBridge");
            }
        }


        ~WC3LuaBridge()
        {
            Registry.SetValue(REGISTRY_KEY, REGISTRY_VALUE, _oldEnableLocalFiles);
            KillProcess();
        }

        public WC3LuaBridge(string mapFileName, string gameExePath = null, string customDataFolder = null, string preloadBridge_AbilityCode = "Agyb")
        {
            _gameExePath = gameExePath ?? @"c:\\Program Files (x86)\\Warcraft III\\_retail_\\x86_64\\Warcraft III.exe";
            _customDataFolder = customDataFolder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), @"Warcraft III\CustomMapData");
            _preloadBridge_AbilityCode = preloadBridge_AbilityCode;
            _watcher = new FileSystemWatcher();
            _watcher.Filter = SCRIPT_TEMPLATE_REPLACEMENTS.PRELOAD_OUTPUT_FILENAME_PREFIX + "*.*";
            _watcher.Changed += OnLuaResponse;

            if (!Map.TryOpen(mapFileName, out var map))
            {
                throw new Exception("Invalid or protected map");
            }

            if (map.Info.ScriptLanguage != War3Net.Build.Info.ScriptLanguage.Lua)
            {
                //todo: auto-transpile
                throw new Exception("Map must be transpiled to lua first");
            }

            _originalScript = map.Script;
            _originalArchive = MpqArchive.Open(mapFileName);
        }

        public void ExecuteMain()
        {
            InjectAndExecuteLuaCode(SCRIPT_TEMPLATE_REPLACEMENTS.UNIQUE_SAFETY_PREFIX + "main()");
        }

        public int InjectAndExecuteLuaCode(string luaFunctionBody)
        {
            var counter = Interlocked.Increment(ref _preloadFileCounter);

            string fullPath = Path.Combine(FullPreloadFolderPath, $"{SCRIPT_TEMPLATE_REPLACEMENTS.PRELOAD_INPUT_FILENAME_PREFIX}{counter}{SCRIPT_TEMPLATE_REPLACEMENTS.PRELOAD_FILE_EXTENSION}");
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            var script = _preloadScript;
            var luaFunctionBody_AsString = "\"" + luaFunctionBody.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
            script = script.Replace("{{luaFunctionBody_AsString}}", luaFunctionBody_AsString);
            script = script.Replace("{{_preloadBridge_AbilityCode}}", _preloadBridge_AbilityCode);
            File.WriteAllText(fullPath, script);

            return counter;
        }

        protected void OnBridgeEstablished()
        {
            Task.Run(() =>
            {

                try
                {
                    if (_bridgeEstablishedTcs != null && !_bridgeEstablishedTcs.Task.IsCompleted)
                    {
                        _bridgeEstablishedTcs.TrySetResult(true);
                    }
                }
                catch
                {
                    //swallow exceptions
                }
            });
        }

        protected void OnLuaResponse(object sender, FileSystemEventArgs e)
        {
            OnBridgeEstablished();

            if (!int.TryParse(Path.GetFileNameWithoutExtension(e.FullPath).Substring(SCRIPT_TEMPLATE_REPLACEMENTS.PRELOAD_OUTPUT_FILENAME_PREFIX.Length), out var fileCounter))
            {
                throw new Exception("Preload filename generated with invalid fileCounter");
            }

            Task.Run(async () =>
            {
                for (var retryCount = 0; retryCount < 5; retryCount++)
                {
                    try
                    {
                        string result = File.ReadAllText(e.FullPath);
                        var matches = Regex.Matches(result, "call Preload\\( \\\"(.*?)\\\" \\)");
                        string concatenatedResult = "";
                        foreach (Match match in matches)
                        {
                            concatenatedResult += match.Groups[1].Value;
                        }

                        if (_alreadyProcessedResponses.Add(fileCounter))
                        {
                            LuaResponseReceived?.Invoke(fileCounter, concatenatedResult);
                        }
                    }
                    catch
                    {
                        await Task.Delay((int)TimeSpan.FromSeconds(1).TotalMilliseconds); // wait until WC3 releases file lock
                        //swallow exceptions
                    }
                }
            });
        }

        protected void CleanupPreloadFiles()
        {
            try
            {
                Directory.Delete(FullPreloadFolderPath, true);
            }
            catch { }
        }

        protected void KillProcess()
        {
            if (_process != null && !_process.HasExited)
            {
                _process.Kill();
                _process = null;

                CleanupPreloadFiles();
            }
        }

        public void Dispose()
        {
            if (_watcher != null)
            {
                _watcher.Dispose();
                _watcher = null;
            }
        }
    }
}
