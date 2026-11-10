using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Interface.Windowing;
using Dalamud.Game.Text;
using Dalamud.Plugin.Services;
using EchoXIV.Outgoing;
using EchoXIV.Services;
using EchoXIV.GameFunctions;
using EchoXIV.UI;
using EchoXIV.Properties;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Shell;
using FFXIVClientStructs.FFXIV.Client.System.String;
using System.Text.RegularExpressions;
using System.Linq;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace EchoXIV
{
    /// <summary>
    /// Main chat translation plugin
    /// </summary>
    public sealed class Plugin : IDalamudPlugin
    {
        public static FontManager? FontManager { get; private set; }
        private const string CommandName = "/translate";
        private const string CommandNameShort = "/tl";
        private const string ConfigCommandName = "/kotodama";
        
        [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
        [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
        [PluginService] internal static IChatGui ChatGui { get;  private set; } = null!;
        [PluginService] internal static IFramework Framework { get; private set; } = null!;
        [PluginService] internal static IClientState ClientState { get; private set; } = null!;
        [PluginService] internal static IPluginLog PluginLog { get; private set; } = null!;
        [PluginService] internal static ICondition Condition { get; private set; } = null!;
        [PluginService] internal static IGameInteropProvider GameInteropProvider { get; private set; } = null!;
        [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
        [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
        [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
        [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
        [PluginService] internal static IPartyList PartyList { get; private set; } = null!;
        [PluginService] internal static IKeyState KeyState { get; private set; } = null!;
        
        private Configuration _configuration = null!;
        private ITranslationService _translatorService = null!;
        private GoogleTranslatorService _googleTranslator = null!;
        private PapagoTranslatorService _papagoTranslator = null!;
        private LocalAiTranslatorService _localAiTranslator = null!;
        private LibreTranslateTranslatorService _libreTranslateTranslator = null!;
        private YandexTranslatorService _yandexTranslator = null!;
        private List<ITranslationService> _instanceServices = new();
        // Shared by every PLaMo instance; owns its HttpClient and is disposed on unload.
        private readonly Plamo.PlamoClient _plamoClient = new();
        private string? _lastPlamoWarmUpKey;
        private string _profilesRoot = string.Empty;
        private Profiles.ProfileStore _profileStore = new(string.Empty);
        private FileSystemWatcher? _profileWatcher;
        private Timer? _profileReloadTimer;
        // Names of the local player and party members, refreshed on the framework thread and read by
        // PLaMo requests on worker threads.
        private IReadOnlyList<string> _gameNames = Array.Empty<string>();
        private long _lastGameNamesRefreshMs;
        // Fallback translator for the incoming/outgoing failover paths. Comes only from
        // EngineSelection (never a hardcoded cloud engine); null when the policy allows no fallback.
        private ITranslationService? _secondaryTranslator;
        private EngineSelectionBlock? _lastSelectionBlock;
        private OutgoingQueue? _outgoingQueue;
        private readonly ConfirmationGate _confirmationGate = new();
        private GameFunctions.PreviewKeyHandler? _previewKeyHandler;
        private volatile bool _imGuiWantsText;
        private readonly HashSet<Guid> _echoedIncoming = new();
        private GameFunctions.ChatSender? _chatSender;
        private GlossaryService _glossaryService = null!;
        private TranslationCache _translationCache = null!;
        private IRomanizationService? RomanizationService
        {
            get
            {
                if (_translatorService is IRomanizationService direct)
                    return direct;

                if (_translatorService is TranslationRouter router)
                    return router.GetLastUsedTranslator() as IRomanizationService;

                return null;
            }
        }
        private WindowSystem _windowSystem = null!;
        private ConfigWindow? _configWindow = null;
        private TranslatedChatWindow? _translatedChatWindow = null;
        private WelcomeWindow? _welcomeWindow = null;
        private GameFunctions.ChatBoxHook? _chatBoxHook = null;
        private IncomingMessageHandler? _incomingMessageHandler = null;
        private MessageHistoryManager _historyManager = null!;
        private static bool _chatVisible = false;
        private string _activeUiCulture = string.Empty;
        private bool _showWelcomeWindow;
        private readonly CancellationTokenSource _shutdownCts = new();
        
        public Plugin()
        {
            try
            {
                // Load configuration
                _configuration = LoadConfiguration();
                FontManager = new Services.FontManager(_configuration, PluginInterface);
                FontManager.BuildFonts();
                _historyManager = new MessageHistoryManager(_configuration, PluginInterface.GetPluginConfigDirectory());

                // Initialize new services
                _glossaryService = new GlossaryService(PluginInterface.GetPluginConfigDirectory());
                _translationCache = new TranslationCache(PluginInterface.GetPluginConfigDirectory());

                // Default profiles: created only where missing, so user edits survive upgrades.
                _profilesRoot = Path.Combine(PluginInterface.GetPluginConfigDirectory(), "profiles");
                var install = Profiles.ProfileInstaller.Install(_profilesRoot, DefaultProfiles.Load());
                foreach (var created in install.Created)
                    PluginLog.Info($"Created default profile file {created}");
                foreach (var error in install.Errors)
                    PluginLog.Warning($"Could not install default profile file: {error}");
                _profileStore = new Profiles.ProfileStore(_profilesRoot);
                LogProfileWarnings(_profileStore.Reload());
                StartProfileWatcher();

                _showWelcomeWindow = _configuration.FirstRun;
                if (_showWelcomeWindow)
                {
                    var uiLang = NormalizeTranslationLanguage(PluginInterface.UiLanguage);
                    if (!string.IsNullOrWhiteSpace(uiLang))
                    {
                        _configuration.SourceLanguage = uiLang;
                        _configuration.TargetLanguage = "en";
                        // Keep FirstRun=true so the welcome window shows
                        _configuration.Save();
                    }
                }

                ApplyResourceCulture();
                
                // Initialize translation engines
                _googleTranslator = new GoogleTranslatorService();
                _papagoTranslator = new PapagoTranslatorService();
                _localAiTranslator = new LocalAiTranslatorService(_configuration);
                _libreTranslateTranslator = new LibreTranslateTranslatorService();
                _yandexTranslator = new YandexTranslatorService();

                // Initialize translation service
                UpdateTranslationService();
                
                // Initialize window system
                _windowSystem = new WindowSystem("Kotodama");
                

                // Create config window
                _configWindow = CreateConfigWindow();

                // Show welcome window while FirstRun
                if (_configuration.FirstRun)
                {
                    _welcomeWindow = CreateWelcomeWindow();
                    _welcomeWindow.IsOpen = true;
                }

                // Create incoming message handler. The secondary translator was chosen by
                // UpdateTranslationService from EngineSelection; it is null when no fallback is allowed.
                var secondaryTranslator = _secondaryTranslator;

                _incomingMessageHandler = new IncomingMessageHandler(
                    _configuration,
                    _translatorService,
                    secondaryTranslator,
                    _glossaryService,
                    _translationCache,
                    ChatGui,
                    ClientState,
                    PlayerState,
                    ObjectTable,
                    PluginLog
                );
                // Wire events
                _incomingMessageHandler.OnTranslationStarted += OnTranslationStarted;
                _incomingMessageHandler.OnMessageTranslated += OnMessageTranslated;
                _incomingMessageHandler.OnOutgoingMessage += OnOutgoingMessage;
                _incomingMessageHandler.OnRequestEngineFailover += HandleEngineFailure;
                
                // Outgoing queue: strict FIFO, clean sends with explicit channel prefixes.
                _chatSender = new GameFunctions.ChatSender(Framework, ChatGui, PluginLog, _incomingMessageHandler, _shutdownCts.Token);
                _outgoingQueue = new OutgoingQueue(
                    new OutgoingTranslator(() => _translatorService, _glossaryService, _translationCache, _configuration, PluginLog, HandleEngineFailure),
                    _chatSender,
                    new OutgoingQueueOptions
                    {
                        Timeout = () => TimeSpan.FromSeconds(_configuration.OutgoingTimeoutSeconds),
                        FailurePolicy = () => _configuration.OutgoingFailurePolicy,
                        Confirmer = _confirmationGate,
                        RequireConfirmation = () => _configuration.OutgoingPreview,
                        ConfirmationTimeout = () => TimeSpan.FromSeconds(_configuration.OutgoingPreviewTimeoutSeconds),
                    });
                _windowSystem.AddWindow(new PreviewWindow(_confirmationGate, () => _outgoingQueue?.PendingCount ?? 0));
                _previewKeyHandler = new GameFunctions.PreviewKeyHandler(Framework, KeyState, _confirmationGate, () => _imGuiWantsText, PluginLog);

                // Initialize native hook (outgoing translation)
                try
                {
                    _chatBoxHook = new GameFunctions.ChatBoxHook(
                        _configuration,
                        () => _outgoingQueue,
                        _incomingMessageHandler!,
                        PluginLog,
                        ChatGui,
                        GameInteropProvider);
                    _chatBoxHook.Enable();
                }
                catch (Exception ex)
                {
                    PluginLog.Error(ex, "Failed to enable Chat Hook (outgoing translation disabled).");
                    // Don't rethrow so the rest of the plugin keeps working
                }
                
                // Register WindowSystem
                PluginInterface.UiBuilder.Draw += DrawUi;
                PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUI;
                PluginInterface.LanguageChanged += OnLanguageChanged;
                
                // Register commands
                // /tl and /translate only translate; every setting lives under /kotodama.
                CommandManager.AddHandler(CommandName, new CommandInfo(OnTranslateCommand)
                {
                    HelpMessage = "Translate and send: /translate [/channel] <message>"
                });

                CommandManager.AddHandler(CommandNameShort, new CommandInfo(OnTranslateCommand)
                {
                    HelpMessage = "Translate and send: /tl [/channel] <message>"
                });

                CommandManager.AddHandler(ConfigCommandName, new CommandInfo(OnConfigCommand)
                {
                    HelpMessage = "Kotodama settings: /kotodama [mode off|explicit|auto | profile <name> | reload | chat | reset | help]"
                });
                
                // Register main UI callback ("Open" opens config)
                PluginInterface.UiBuilder.OpenMainUi += ToggleConfigUI;

                // Subscribe to Framework update for thread-safe visibility cache
                Framework.Update += OnFrameworkUpdate;

                ClientState.Login += OnClientLogin;
                ClientState.Logout += OnClientLogout;

                // Initialize immediately when already logged in.
                if (ClientState.IsLoggedIn)
                    OnLogin();
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "Fatal error while initializing the plugin. Cleaning up to avoid dangling state...");
                Dispose(); 
                throw;
            }
        }

        private ConfigWindow CreateConfigWindow()
        {
            var configWindow = new ConfigWindow(_configuration, _historyManager)
            {
                ProfilesProvider = () => _profileStore.Profiles,
                ProfilesFolder = _profilesRoot,
            };
            configWindow.OnProfilesReloadRequested += () => ReloadProfiles(announce: true);
            configWindow.OnTranslationEngineChanged += OnTranslationEngineChangedHandler;
            configWindow.OnUiLanguageChanged += OnUiLanguageChangedHandler;
            _windowSystem.AddWindow(configWindow);
            return configWindow;
        }

        private WelcomeWindow CreateWelcomeWindow()
        {
            var welcomeWindow = new WelcomeWindow(_configuration);
            welcomeWindow.OnConfigurationComplete += OnWelcomeConfigurationComplete;
            _windowSystem.AddWindow(welcomeWindow);
            return welcomeWindow;
        }

        private TranslatedChatWindow CreateTranslatedChatWindow()
        {
            var translatedChatWindow = new TranslatedChatWindow(_configuration, _historyManager);
            translatedChatWindow.OnRequestTranslation += OnTranslatedChatRequestTranslation;
            translatedChatWindow.OnRequestSettings += ToggleConfigUI;
            _windowSystem.AddWindow(translatedChatWindow);
            return translatedChatWindow;
        }

        private void DrawUi()
        {
            FontManager?.BuildFonts();
            using var font = FontManager?.Push();
            _imGuiWantsText = Dalamud.Bindings.ImGui.ImGui.GetIO().WantTextInput;
            _windowSystem.Draw();
        }

        private void OnLanguageChanged(string langCode)
        {
            if (ApplyResourceCulture())
            {
                RefreshLocalizedWindows();
            }
        }

        private bool ApplyResourceCulture()
        {
            // Use the manually chosen language, otherwise follow Dalamud.
            var effectiveLanguage = !string.IsNullOrWhiteSpace(_configuration.UiLanguage)
                ? _configuration.UiLanguage
                : PluginInterface.UiLanguage;

            var cultureName = NormalizeUiLanguage(effectiveLanguage);
            var changed = !string.Equals(_activeUiCulture, cultureName, StringComparison.OrdinalIgnoreCase);

            try
            {
                var culture = new CultureInfo(cultureName);
                Resources.Culture = culture;
                CultureInfo.DefaultThreadCurrentUICulture = culture;
                CultureInfo.DefaultThreadCurrentCulture = culture;
                CultureInfo.CurrentUICulture = culture;
                CultureInfo.CurrentCulture = culture;
                Thread.CurrentThread.CurrentUICulture = culture;
                Thread.CurrentThread.CurrentCulture = culture;
                _activeUiCulture = culture.Name;
            }
            catch
            {
                var fallbackCulture = new CultureInfo("en");
                Resources.Culture = fallbackCulture;
                CultureInfo.DefaultThreadCurrentUICulture = fallbackCulture;
                CultureInfo.DefaultThreadCurrentCulture = fallbackCulture;
                CultureInfo.CurrentUICulture = fallbackCulture;
                CultureInfo.CurrentCulture = fallbackCulture;
                Thread.CurrentThread.CurrentUICulture = fallbackCulture;
                Thread.CurrentThread.CurrentCulture = fallbackCulture;
                changed = !string.Equals(_activeUiCulture, fallbackCulture.Name, StringComparison.OrdinalIgnoreCase);
                _activeUiCulture = fallbackCulture.Name;
            }

            return changed;
        }

        private static string NormalizeUiLanguage(string? uiLanguage)
        {
            if (string.IsNullOrWhiteSpace(uiLanguage))
            {
                return "en";
            }

            var normalized = uiLanguage.Trim();

            if (normalized.StartsWith("zh-hant", StringComparison.OrdinalIgnoreCase)) return "zh-Hant";
            if (normalized.StartsWith("zh-hans", StringComparison.OrdinalIgnoreCase)) return "zh-Hans";
            if (normalized.StartsWith("tw", StringComparison.OrdinalIgnoreCase)) return "zh-Hant";
            if (normalized.StartsWith("zh-tw", StringComparison.OrdinalIgnoreCase)) return "zh-Hant";
            if (normalized.StartsWith("zh", StringComparison.OrdinalIgnoreCase)) return "zh-Hans";
            if (normalized.StartsWith("es", StringComparison.OrdinalIgnoreCase)) return "es";
            if (normalized.StartsWith("ja", StringComparison.OrdinalIgnoreCase)) return "ja";
            if (normalized.StartsWith("fr", StringComparison.OrdinalIgnoreCase)) return "fr";
            if (normalized.StartsWith("de", StringComparison.OrdinalIgnoreCase)) return "de";
            if (normalized.StartsWith("it", StringComparison.OrdinalIgnoreCase)) return "it";
            if (normalized.StartsWith("ko", StringComparison.OrdinalIgnoreCase)) return "ko";
            if (normalized.StartsWith("no", StringComparison.OrdinalIgnoreCase)) return "no";
            if (normalized.StartsWith("ru", StringComparison.OrdinalIgnoreCase)) return "ru";
            if (normalized.StartsWith("tr", StringComparison.OrdinalIgnoreCase)) return "tr";
            if (normalized.StartsWith("pt-br", StringComparison.OrdinalIgnoreCase)) return "pt-BR";
            if (normalized.StartsWith("pt", StringComparison.OrdinalIgnoreCase)) return "pt-BR";
            if (normalized.StartsWith("pl", StringComparison.OrdinalIgnoreCase)) return "pl";
            if (normalized.StartsWith("uk", StringComparison.OrdinalIgnoreCase)) return "uk";
            if (normalized.StartsWith("cs", StringComparison.OrdinalIgnoreCase)) return "cs";
            if (normalized.StartsWith("nl", StringComparison.OrdinalIgnoreCase)) return "nl";
            if (normalized.StartsWith("en", StringComparison.OrdinalIgnoreCase)) return "en";

            return "en";
        }

        private static string NormalizeTranslationLanguage(string? uiLanguage)
        {
            if (string.IsNullOrWhiteSpace(uiLanguage))
            {
                return "en";
            }

            var normalized = uiLanguage.Trim();

            if (normalized.StartsWith("zh-hant", StringComparison.OrdinalIgnoreCase)) return "zh-TW";
            if (normalized.StartsWith("zh-hans", StringComparison.OrdinalIgnoreCase)) return "zh-CN";
            if (normalized.StartsWith("zh-tw", StringComparison.OrdinalIgnoreCase)) return "zh-TW";
            if (normalized.StartsWith("tw", StringComparison.OrdinalIgnoreCase)) return "zh-TW";
            if (normalized.StartsWith("zh", StringComparison.OrdinalIgnoreCase)) return "zh-CN";
            if (normalized.StartsWith("es", StringComparison.OrdinalIgnoreCase)) return "es";
            if (normalized.StartsWith("ja", StringComparison.OrdinalIgnoreCase)) return "ja";
            if (normalized.StartsWith("fr", StringComparison.OrdinalIgnoreCase)) return "fr";
            if (normalized.StartsWith("de", StringComparison.OrdinalIgnoreCase)) return "de";
            if (normalized.StartsWith("it", StringComparison.OrdinalIgnoreCase)) return "it";
            if (normalized.StartsWith("ko", StringComparison.OrdinalIgnoreCase)) return "ko";
            if (normalized.StartsWith("ru", StringComparison.OrdinalIgnoreCase)) return "ru";
            if (normalized.StartsWith("no", StringComparison.OrdinalIgnoreCase)) return "no";
            if (normalized.StartsWith("en", StringComparison.OrdinalIgnoreCase)) return "en";

            return "en";
        }

        private void OnLogin()
        {
            ApplyResourceCulture();

            if (_translatedChatWindow == null)
            {
                _translatedChatWindow = CreateTranslatedChatWindow();
            }

            _translatedChatWindow.IsOpen = _configuration.OverlayVisible;
        }

        private void OnLogout()
        {
        }
    
        
        public void Dispose()
        {
            _shutdownCts.Cancel();

            // Stop outgoing first: unhook so no new lines are queued, then stop the queue so
            // nothing reaches the game after unload starts.
            _chatBoxHook?.Dispose();
            _outgoingQueue?.Dispose();
            _previewKeyHandler?.Dispose();

            // Stop incoming message handler
            _incomingMessageHandler?.Dispose();
            FontManager?.Dispose();

            // Stop translation engines
            _googleTranslator?.Dispose();
            if (_papagoTranslator is IDisposable papagoDisp) papagoDisp.Dispose();
            _localAiTranslator?.Dispose();
            if (_libreTranslateTranslator is IDisposable libreDisp) libreDisp.Dispose();
            if (_yandexTranslator is IDisposable yandexDisp) yandexDisp.Dispose();
            foreach (var svc in _instanceServices)
            {
                if (svc is IDisposable d) d.Dispose();
            }
            _instanceServices.Clear();
            _plamoClient.Dispose();
            _profileWatcher?.Dispose();
            _profileReloadTimer?.Dispose();

            // Remove windows
            _windowSystem?.RemoveAllWindows();

            if (_configWindow != null)
            {
                DetachConfigWindowHandlers(_configWindow);
                _configWindow.Dispose();
            }

            if (_welcomeWindow != null)
            {
                _welcomeWindow.OnConfigurationComplete -= OnWelcomeConfigurationComplete;
            }

            if (_translatedChatWindow != null)
            {
                _translatedChatWindow.OnRequestTranslation -= OnTranslatedChatRequestTranslation;
                _translatedChatWindow.OnRequestSettings -= ToggleConfigUI;
            }

            if (_incomingMessageHandler != null)
            {
                _incomingMessageHandler.OnTranslationStarted -= OnTranslationStarted;
                _incomingMessageHandler.OnMessageTranslated -= OnMessageTranslated;
                _incomingMessageHandler.OnOutgoingMessage -= OnOutgoingMessage;
                _incomingMessageHandler.OnRequestEngineFailover -= HandleEngineFailure;
            }
            
            _translationCache.Dispose();
            _configuration.SaveNow();
            _historyManager?.Dispose();
            
            // Remove commands
            CommandManager.RemoveHandler(CommandName);
            CommandManager.RemoveHandler(CommandNameShort);
            CommandManager.RemoveHandler(ConfigCommandName);
            
            // Unhook events
            Framework.Update -= OnFrameworkUpdate;
            if (_windowSystem != null)
            {
                PluginInterface.UiBuilder.Draw -= DrawUi;
            }
            PluginInterface.LanguageChanged -= OnLanguageChanged;
            PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUI;
            PluginInterface.UiBuilder.OpenMainUi -= ToggleConfigUI;
            ClientState.Login -= OnClientLogin;
            ClientState.Logout -= OnClientLogout;
        }

        private void RefreshLocalizedWindows()
        {
            var configWasOpen = _configWindow?.IsOpen ?? false;
            if (_configWindow != null)
            {
                _windowSystem.RemoveWindow(_configWindow);
                DetachConfigWindowHandlers(_configWindow);
                _configWindow.Dispose();
            }

            _configWindow = CreateConfigWindow();
            _configWindow.IsOpen = configWasOpen;

            if (_welcomeWindow != null)
            {
                var welcomeWasOpen = _welcomeWindow.IsOpen;
                _windowSystem.RemoveWindow(_welcomeWindow);
                _welcomeWindow.OnConfigurationComplete -= OnWelcomeConfigurationComplete;

                _welcomeWindow = CreateWelcomeWindow();
                _welcomeWindow.IsOpen = welcomeWasOpen;
            }
            else if (_showWelcomeWindow)
            {
                _welcomeWindow = CreateWelcomeWindow();
                _welcomeWindow.IsOpen = false;
            }

            if (_translatedChatWindow != null)
            {
                var chatWasOpen = _translatedChatWindow.IsOpen;
                _windowSystem.RemoveWindow(_translatedChatWindow);
                _translatedChatWindow.OnRequestTranslation -= OnTranslatedChatRequestTranslation;
                _translatedChatWindow.Dispose();
                _translatedChatWindow = CreateTranslatedChatWindow();
                _translatedChatWindow.IsOpen = chatWasOpen;
            }
        }

        private void DetachConfigWindowHandlers(ConfigWindow configWindow)
        {
            configWindow.OnTranslationEngineChanged -= OnTranslationEngineChangedHandler;
            configWindow.OnUiLanguageChanged -= OnUiLanguageChangedHandler;
        }

        private void OnTranslationEngineChangedHandler(TranslationEngine engine)
        {
            // A manual selection away from Local AI cancels automatic restoration.
            // The failover path sets the flag directly and does not come through here.
            if (engine != TranslationEngine.LocalAI && _configuration.LocalAiUnavailable)
            {
                _configuration.LocalAiUnavailable = false;
                _configuration.Save();
            }
            UpdateTranslationService();
        }

        private void OnUiLanguageChangedHandler()
        {
            if (ApplyResourceCulture())
            {
                RefreshLocalizedWindows();
            }
        }

        private void OnWelcomeConfigurationComplete()
        {
            _showWelcomeWindow = false;
            ApplyResourceCulture();
            RefreshLocalizedWindows();
            UpdateTranslationService();
        }

        private void OnTranslatedChatRequestTranslation(TranslatedChatMessage message)
        {
            _ = _incomingMessageHandler?.ProcessMessageAsync(message);
        }

        private void OnTranslationStarted(TranslatedChatMessage message)
        {
            _historyManager.AddMessage(message);
        }

        private void OnMessageTranslated(TranslatedChatMessage message)
        {
            _historyManager.UpdateMessage(message);
            EchoIncomingToChat(message);
        }

        /// <summary>
        /// Prints an incoming translation as a local-only "[KT] …" line below the original, in the
        /// same channel and with the same sender so it lands in the same chat tab (stock chat and
        /// ChatTwo). Printed once per message; the original line is never changed.
        /// </summary>
        private void EchoIncomingToChat(TranslatedChatMessage message)
        {
            if (!_configuration.ShowIncomingInChat || message.IsTranslating)
                return;

            var line = IncomingChatEcho.Format(message.OriginalText, message.TranslatedText, message.TranslationFailed, message.IsOutgoing);
            if (line == null)
                return;

            lock (_echoedIncoming)
            {
                if (_echoedIncoming.Count > 1000)
                    _echoedIncoming.Clear();
                if (!_echoedIncoming.Add(message.Id))
                    return;
            }

            _ = Framework.RunOnFrameworkThread(() =>
            {
                if (_shutdownCts.IsCancellationRequested)
                    return;

                _incomingMessageHandler?.RegisterOwnPrint(line);
                ChatGui.Print(new XivChatEntry
                {
                    Type = message.ChatType,
                    Name = message.SenderSeString ?? Dalamud.Game.Text.SeStringHandling.SeString.Empty,
                    Message = line,
                });
            });
        }

        private void OnOutgoingMessage(TranslatedChatMessage message)
        {
            // Dedicated path for sent messages to avoid duplicating incoming write logic
            _historyManager.AddMessage(message);
        }

        private void OnClientLogin()
        {
            OnLogin();
        }

        private void OnClientLogout(int type, int code)
        {
            OnLogout();
        }
        
        /// <summary>/tl and /translate: translate the text and send it. No keywords.</summary>
        private void OnTranslateCommand(string command, string args)
        {
            if (string.IsNullOrWhiteSpace(args))
            {
                ChatGui.Print($"Usage: {command} [/p | /fc | /t Name@World | …] <message>. Settings: /kotodama");
                return;
            }

            if (_configuration.OutgoingMode == OutgoingMode.Off)
            {
                ChatGui.PrintError("[Kotodama] Outgoing translation is off. Turn it on in /kotodama or with /kotodama mode explicit.");
                return;
            }

            if (!TryResolveOutgoing(args, out var resolved, out var error))
            {
                ChatGui.PrintError($"[Kotodama] {error}");
                return;
            }

            EnqueueOutgoing(resolved);
        }

        /// <summary>/kotodama: settings window and plugin commands.</summary>
        private void OnConfigCommand(string command, string args)
        {
            var parts = args.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            var verb = parts.Length > 0 ? parts[0].ToLowerInvariant() : string.Empty;

            switch (verb)
            {
                case "":
                case "config":
                case "settings":
                    ToggleConfigUI();
                    break;

                case "mode":
                    SetOutgoingModeCommand(parts.Length > 1 ? parts[1].ToLowerInvariant() : string.Empty);
                    break;

                case "chat":
                case "c":
                    ToggleTranslatedChatWindow();
                    break;

                case "profile":
                    SetActiveProfileCommand(parts.Length > 1 ? string.Join(' ', parts.Skip(1)) : string.Empty);
                    break;

                case "reload":
                    ReloadProfiles(announce: true);
                    break;

                case "reset":
                    ResetTranslatedChatWindowPosition();
                    break;

                case "help":
                case "?":
                    ChatGui.Print("Kotodama commands:");
                    ChatGui.Print("/tl <message> - translate and send to the channel that was active when you typed it.");
                    ChatGui.Print("/tl /p <message>, /tl /fc …, /tl /t First Last@World … - translate and send to that channel.");
                    ChatGui.Print("/kotodama - open settings.");
                    ChatGui.Print("/kotodama mode off|explicit|auto - outgoing translation mode.");
                    ChatGui.Print("/kotodama profile [name] - show or switch the translation profile (Raid, Casual, …).");
                    ChatGui.Print("/kotodama reload - reload profile files after editing them.");
                    ChatGui.Print("/kotodama chat - show or hide the translated chat window.");
                    ChatGui.Print("/kotodama reset - reset the translated chat window position.");
                    break;

                default:
                    ChatGui.PrintError($"[Kotodama] Unknown command '{verb}'. Try /kotodama help.");
                    break;
            }
        }

        private void SetOutgoingModeCommand(string value)
        {
            OutgoingMode? mode = value switch
            {
                "off" => OutgoingMode.Off,
                "explicit" or "explicitonly" => OutgoingMode.ExplicitOnly,
                "auto" or "autointercept" => OutgoingMode.AutoIntercept,
                _ => null,
            };

            if (mode == null)
            {
                ChatGui.Print($"Outgoing mode: {DescribeOutgoingMode(_configuration.OutgoingMode)}. Use /kotodama mode off|explicit|auto.");
                return;
            }

            _configuration.OutgoingMode = mode.Value;
            _configuration.Save();
            ChatGui.Print($"[Kotodama] Outgoing mode: {DescribeOutgoingMode(mode.Value)}.");
        }

        internal static string DescribeOutgoingMode(OutgoingMode mode) => mode switch
        {
            OutgoingMode.Off => "off (no outgoing translation)",
            OutgoingMode.AutoIntercept => "auto (every typed line is translated)",
            _ => "explicit (only /tl is translated)",
        };

        private void ToggleConfigUI()
        {
            if (_configWindow != null)
            {
                _configWindow.Toggle();
            }
        }
        

        
        /// <summary>
        /// Resets the translated chat window position to center
        /// </summary>
        private void ResetTranslatedChatWindowPosition()
        {
            if (_translatedChatWindow == null)
            {
                _translatedChatWindow = CreateTranslatedChatWindow();
            }

            if (_translatedChatWindow != null)
            {
                _translatedChatWindow.ResetPosition();
                ChatGui.Print(GetResourceText("Command_PositionReset", "Window position reset to (100, 100). It should be visible now."));
            }
        }

        /// <summary>
        /// Queues a resolved /tl line. Excluded phrases are sent unchanged, but still through the
        /// queue so they keep their place in line. Framework thread.
        /// </summary>
        private void EnqueueOutgoing(ResolvedOutgoing resolved)
        {
            var excluded = _configuration.ExcludedMessages.Contains(resolved.Message);
            var item = new OutgoingItem
            {
                Kind = excluded ? OutgoingItemKind.PassThrough : OutgoingItemKind.Translate,
                Text = resolved.Message,
                Prefix = resolved.Prefix,
                SaveToHistory = false,
            };

            if (_outgoingQueue == null || !_outgoingQueue.TryEnqueue(item))
                ChatGui.PrintError($"[Kotodama] Not sent: too many messages are waiting to be translated. Your message: {resolved.Message}");
        }

        /// <summary>
        /// An outgoing line whose channel was resolved when it was entered. <see cref="Prefix"/> is
        /// always an explicit channel command, so sending never depends on the active channel later.
        /// </summary>
        private sealed record ResolvedOutgoing(string Prefix, string Message);

        /// <summary>
        /// Parses /tl arguments and resolves the channel. Must run on the framework thread (it reads
        /// the active chat channel and the last tell partner from game memory).
        /// </summary>
        private bool TryResolveOutgoing(string args, out ResolvedOutgoing resolved, out string error)
        {
            resolved = null!;
            var parsed = ChannelPrefixParser.Parse(args);
            if (parsed.IsError)
            {
                error = parsed.Error switch
                {
                    ChannelParseError.UnsupportedCommand => $"'{parsed.Command}' is not a chat channel Kotodama can send to. Use /p, /fc, /a, /s, /sh, /y, /l1-8, /cwl1-8, /t, /r or /e.",
                    ChannelParseError.MissingTellRecipient => "Tell needs a recipient: /tl /t First Last@World message.",
                    _ => "No message to translate.",
                };
                return false;
            }

            var target = parsed.Target ?? CaptureActiveChannel();
            if (target == null)
            {
                error = "The active chat channel can't be addressed explicitly. Add a channel, e.g. /tl /p message.";
                return false;
            }

            if (target.Kind == ChatChannelKind.Reply)
            {
                target = ResolveReplyTarget();
                if (target == null)
                {
                    error = "Nobody to reply to yet.";
                    return false;
                }
            }

            resolved = new ResolvedOutgoing(target.Prefix, parsed.Body);
            error = string.Empty;
            return true;
        }

        /// <summary>The active chat input channel as an explicit target. Framework thread only.</summary>
        internal static unsafe ChannelTarget? CaptureActiveChannel()
        {
            var shell = RaptureShellModule.Instance();
            if (shell == null)
                return null;

            return ChannelPrefixParser.FromInputChannel(shell->ChatType, shell->TellName.ToString(), shell->TellWorld.ToString());
        }

        /// <summary>Resolves "/r" to the last tell partner, at entry time. Framework thread only.</summary>
        private static unsafe ChannelTarget? ResolveReplyTarget()
        {
            var agent = AgentChatLog.Instance();
            if (agent == null)
                return null;

            var name = agent->TellPlayerName.ToString();
            if (string.IsNullOrWhiteSpace(name))
                return null;

            string? world = null;
            if (agent->TellWorldId != 0 &&
                DataManager.GetExcelSheet<Lumina.Excel.Sheets.World>().TryGetRow(agent->TellWorldId, out var row))
            {
                world = row.Name.ExtractText();
            }

            return ChannelTarget.Tell(name, world);
        }

        private Configuration LoadConfiguration()
        {
            var loaded = PluginInterface.GetPluginConfig() as Configuration;
            if (loaded == null)
            {
                PluginLog.Warning("Saved configuration could not be loaded (deserialization failed). Starting with default engines.");
                loaded = new Configuration();
            }
            var config = loaded;
            config.Initialize(PluginInterface);
            var hasChanges = config.NormalizeDefaults();
            if (hasChanges)
            {
                config.Save();
            }
            return config;
        }

        public void ToggleTranslatedChatWindow()
        {
            if (_translatedChatWindow == null)
            {
                _translatedChatWindow = CreateTranslatedChatWindow();
            }

            _translatedChatWindow.IsOpen = !_translatedChatWindow.IsOpen;
            _configuration.OverlayVisible = _translatedChatWindow.IsOpen;
            
            _configuration.Save();
            if (_configuration.ShowChatToggleLog)
            {
                ChatGui.Print(GetResourceText(
                    _configuration.OverlayVisible ? "Command_ChatVisible" : "Command_ChatHidden",
                    _configuration.OverlayVisible ? "Translated chat: visible" : "Translated chat: hidden"));
            }
        }

        private void UpdateTranslationService()
        {
            // Dispose previous instance services (per-instance cloud LLM translators).
            foreach (var svc in _instanceServices)
            {
                if (svc is IDisposable d) d.Dispose();
            }
            _instanceServices = new List<ITranslationService>();
            if (_translatorService is IDisposable disposable)
            {
                disposable.Dispose();
            }

            // Build the ordered instance list ONLY from the policy-filtered selection
            // (primary first, then allowed fallbacks in list order).
            var selection = EngineSelection.Select(_configuration.Engines, _configuration.PrimaryEngineId, _configuration.FallbackPolicy, IsEngineConfigured);
            ReportSelectionBlock(selection);
            var ordered = new List<(Guid Id, ITranslationService Translator, string DisplayName)>();
            var cacheVariants = new Dictionary<Guid, string>();
            foreach (var instance in selection.Engines)
            {
                // Local AI reads its model from the global setting; other engines from the instance.
                if (instance.Type == TranslationEngine.Plamo)
                {
                    // The profile shapes PLaMo's output: switching or editing it changes the key.
                    var profile = _profileStore.Get(_configuration.ActiveProfile);
                    cacheVariants[instance.Id] = CacheKey.Variant(instance.Type, instance.Model, profile.Name, profile.ContentHash);
                }
                else
                {
                    cacheVariants[instance.Id] = CacheKey.Variant(
                        instance.Type,
                        instance.Type == TranslationEngine.LocalAI ? _configuration.LocalAiModel : instance.Model);
                }

                ITranslationService svc;
                switch (instance.Type)
                {
                    case TranslationEngine.Google: svc = _googleTranslator; break;
                    case TranslationEngine.Papago: svc = _papagoTranslator; break;
                    case TranslationEngine.LibreTranslate:
                        svc = new LibreTranslateTranslatorService(instance);
                        _instanceServices.Add(svc);
                        break;
                    case TranslationEngine.Yandex: svc = _yandexTranslator; break;
                    case TranslationEngine.LocalAI: svc = _localAiTranslator; break;
                    case TranslationEngine.ChatGPT:
                    case TranslationEngine.DeepSeek:
                    case TranslationEngine.OpenRouter:
                        svc = new OpenAiCompatibleTranslatorService(_configuration, instance);
                        _instanceServices.Add(svc);
                        break;
                    case TranslationEngine.Gemini:
                        svc = new GeminiTranslatorService(_configuration, instance, PluginLog);
                        _instanceServices.Add(svc);
                        break;
                    case TranslationEngine.Claude:
                        svc = new ClaudeTranslatorService(_configuration, instance);
                        _instanceServices.Add(svc);
                        break;
                    case TranslationEngine.Plamo:
                    {
                        var plamoInstance = instance;
                        svc = new PlamoTranslatorService(_plamoClient, () => PlamoSettingsFor(plamoInstance), PlamoExtrasFor);
                        _instanceServices.Add(svc);
                        break;
                    }
                    default: continue;
                }
                var displayName = string.IsNullOrWhiteSpace(instance.Name) ? instance.Type.ToString() : instance.Name;
                ordered.Add((instance.Id, svc, displayName));
            }

            _translatorService = new TranslationRouter(
                selection.Primary?.Id ?? Guid.Empty,
                ordered,
                _configuration.EngineRecoveryCheckMinutes,
                PluginLog,
                _shutdownCts.Token,
                cacheVariants);

            if (_configuration.VerboseLogging)
                PluginLog.Info($"Translation engine active: {_configuration.PrimaryInstanceName}");

            var secondaryTranslator = ordered.Count > 1 ? ordered[1].Translator : null;
            _secondaryTranslator = secondaryTranslator;

            WarmUpPlamoIfNeeded(selection.Primary);

            // Update translators (romanization always same engine)
            if (_incomingMessageHandler != null)
            {
                _incomingMessageHandler.UpdateTranslator(_translatorService);
                _incomingMessageHandler.UpdateSecondaryTranslator(secondaryTranslator);
            }
            // The outgoing queue reads _translatorService for every item; nothing to update there.
        }

        private void ReportSelectionBlock(EngineSelectionResult selection)
        {
            var block = selection.BlockedReason;
            if (block == _lastSelectionBlock)
                return;
            _lastSelectionBlock = block;
            if (block == null)
                return;

            var message = GetSelectionBlockMessage(block.Value);
            PluginLog.Warning($"Translation unavailable: {message}");
            _ = Framework.RunOnFrameworkThread(() => ChatGui.PrintError($"[Kotodama] {message}"));
        }

        /// <summary>Whether an instance has the settings it needs before any request may be sent.</summary>
        internal bool IsEngineConfigured(EngineInstance instance) => IsEngineConfigured(_configuration, instance);

        internal static bool IsEngineConfigured(Configuration configuration, EngineInstance instance) => instance.Type switch
        {
            // Local AI reads the global endpoint setting; there is deliberately no default URL.
            TranslationEngine.LocalAI => !string.IsNullOrWhiteSpace(configuration.LocalAiEndpoint),
            TranslationEngine.Plamo => !string.IsNullOrWhiteSpace(instance.Endpoint) && !string.IsNullOrWhiteSpace(instance.Model),
            _ => true,
        };

        /// <summary>
        /// Loads the PLaMo model in the background when it becomes the primary engine (or its
        /// server/model/keep-alive changes), so the first real translation doesn't pay the ~3 s load.
        /// </summary>
        private void WarmUpPlamoIfNeeded(EngineInstance? primary)
        {
            if (primary?.Type != TranslationEngine.Plamo || !IsEngineConfigured(primary))
                return;

            var settings = PlamoSettingsFor(primary);
            var key = $"{settings.BaseUrl}|{settings.Model}|{settings.KeepAlive}";
            if (key == _lastPlamoWarmUpKey)
                return;

            _lastPlamoWarmUpKey = key;
            var token = _shutdownCts.Token;
            _ = Task.Run(() => PlamoConnectionTest.WarmUpAsync(_plamoClient, settings, token), token);
        }

        /// <summary>Active profile's context and matching glossary terms for one PLaMo request (worker thread).</summary>
        private PlamoPromptExtras PlamoExtrasFor(Plamo.TranslationDirection direction, string text)
        {
            var keep = Volatile.Read(ref _gameNames).Concat(_configuration.KeepAsWritten.ToArray());
            var identity = Profiles.TokenGuard.IdentityTerms(text, keep);
            var prompt = Profiles.ProfileStore.PromptFor(_profileStore.Get(_configuration.ActiveProfile), direction, text, identity);
            return new PlamoPromptExtras(prompt.Context, prompt.Glossary, null);
        }

        private void StartProfileWatcher()
        {
            try
            {
                Directory.CreateDirectory(_profilesRoot);
                _profileReloadTimer = new Timer(_ => ReloadProfiles(announce: false), null, Timeout.Infinite, Timeout.Infinite);
                _profileWatcher = new FileSystemWatcher(_profilesRoot)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size,
                };
                FileSystemEventHandler changed = (_, e) => OnProfileFileChanged(e.FullPath);
                _profileWatcher.Changed += changed;
                _profileWatcher.Created += changed;
                _profileWatcher.Deleted += changed;
                _profileWatcher.Renamed += (_, e) => OnProfileFileChanged(e.FullPath);
                _profileWatcher.EnableRaisingEvents = true;
            }
            catch (Exception ex)
            {
                // Watching is a convenience; /kotodama reload still works.
                PluginLog.Warning(ex, "Could not watch the profiles folder for changes.");
            }
        }

        private void OnProfileFileChanged(string path)
        {
            if (path.Contains(Path.DirectorySeparatorChar + Profiles.ProfileInstaller.DefaultsDirectoryName, StringComparison.Ordinal))
                return;

            // Debounce: editors write files in several steps.
            _profileReloadTimer?.Change(500, Timeout.Infinite);
        }

        /// <summary>Reloads profile files off the framework thread, then rebuilds engines on it.</summary>
        private void ReloadProfiles(bool announce)
        {
            if (_shutdownCts.IsCancellationRequested)
                return;

            _ = Task.Run(() =>
            {
                try
                {
                    var profiles = _profileStore.Reload();
                    LogProfileWarnings(profiles);
                    _ = Framework.RunOnFrameworkThread(() =>
                    {
                        if (_shutdownCts.IsCancellationRequested)
                            return;
                        UpdateTranslationService();
                        var active = _profileStore.Get(_configuration.ActiveProfile);
                        if (announce)
                            ChatGui.Print($"[Kotodama] Profiles reloaded: {string.Join(", ", profiles.Select(p => p.Name))}. Active: {DescribeProfile(active)}.");
                        if (active.Warnings.Count > 0)
                            ChatGui.PrintError($"[Kotodama] Profile {active.Name}: {active.Warnings.Count} glossary line(s) ignored. See /xllog.");
                    });
                }
                catch (Exception ex)
                {
                    PluginLog.Error(ex, "Could not reload translation profiles.");
                }
            });
        }

        private void LogProfileWarnings(IReadOnlyList<Profiles.TranslationProfile> profiles)
        {
            foreach (var profile in profiles)
            {
                foreach (var warning in profile.Warnings)
                    PluginLog.Warning($"Profile {profile.Name}: {warning}");
            }
        }

        private string DescribeProfile(Profiles.TranslationProfile profile) =>
            ReferenceEquals(profile, Profiles.TranslationProfile.Empty)
                ? $"{_configuration.ActiveProfile} (not found, no profile used)"
                : $"{profile.Name} ({profile.Glossary.Count} glossary entries)";

        private void SetActiveProfileCommand(string name)
        {
            var profiles = _profileStore.Profiles;
            if (string.IsNullOrWhiteSpace(name))
            {
                ChatGui.Print($"[Kotodama] Active profile: {DescribeProfile(_profileStore.Get(_configuration.ActiveProfile))}. Available: {string.Join(", ", profiles.Select(p => p.Name))}. Folder: {_profilesRoot}");
                return;
            }

            var match = profiles.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            if (match == null)
            {
                ChatGui.PrintError($"[Kotodama] No profile named '{name}'. Available: {string.Join(", ", profiles.Select(p => p.Name))}.");
                return;
            }

            SetActiveProfile(match.Name);
            ChatGui.Print($"[Kotodama] Active profile: {DescribeProfile(match)}.");
        }

        private void SetActiveProfile(string name)
        {
            _configuration.ActiveProfile = name;
            _configuration.Save();
            UpdateTranslationService();
        }

        internal static Plamo.PlamoSettings PlamoSettingsFor(EngineInstance instance) => new(
            instance.Endpoint,
            string.IsNullOrWhiteSpace(instance.Model) ? Plamo.PlamoSettings.DefaultModel : instance.Model.Trim(),
            instance.KeepAlive ?? string.Empty,
            Math.Clamp(instance.Temperature, 0f, 1f),
            instance.MaxTokens > 0 ? instance.MaxTokens : Plamo.PlamoSettings.DefaultNumPredict,
            TimeSpan.FromSeconds(Math.Clamp(instance.TimeoutSeconds, 1, 60)));

        internal static string GetSelectionBlockMessage(EngineSelectionBlock block) => block switch
        {
            EngineSelectionBlock.PrimaryNotConfigured => GetResourceText(
                "Engines_BlockedNotConfigured",
                "Translation is off: the primary engine has no server URL yet. Enter it in /kotodama → Engines."),
            EngineSelectionBlock.CloudPrimaryNotAllowed => GetResourceText(
                "Engines_BlockedCloudPrimary",
                "Translation is off: the primary engine is a cloud service, but the fallback policy only allows local engines. Choose a local primary engine or change the policy in /kotodama → Engines."),
            _ => GetResourceText(
                "Engines_BlockedNoEngine",
                "Translation is off: no translation engine is enabled. Enable one in /kotodama → Engines."),
        };

        /// <summary>
        /// Called from the hook and the incoming handler (worker threads) after an engine failure.
        /// All configuration changes happen on the framework thread.
        /// </summary>
        private void HandleEngineFailure()
        {
            _ = Framework.RunOnFrameworkThread(() =>
            {
                if (!_shutdownCts.IsCancellationRequested)
                    TrySwitchEngineAfterFailure();
            });
        }

        /// <summary>
        /// Persistently switches the primary engine after a failure, if the fallback policy allows
        /// it (only Any, only to an existing enabled Google instance, never re-enabling anything).
        /// Must run on the framework thread. Returns true when the primary engine changed.
        /// </summary>
        private bool TrySwitchEngineAfterFailure()
        {
            var target = FailoverDecision.ChooseFailoverTarget(
                _configuration.FallbackPolicy, _configuration.Engines, _configuration.PrimaryEngineId);
            if (target == null)
            {
                PluginLog.Warning($"Primary engine failed; fallback policy {_configuration.FallbackPolicy} keeps the engine configuration unchanged.");
                return false;
            }

            var wasLocalAi = _configuration.SelectedEngine == TranslationEngine.LocalAI;
            PluginLog.Warning(wasLocalAi
                ? "Local AI server is not responding. Switching automatically to Google Translate."
                : "Primary engine reached its limit. Switching automatically to Google Translate.");

            _configuration.PrimaryEngineId = target.Value;
            _configuration.LocalAiUnavailable = wasLocalAi;
            _configuration.Save();
            UpdateTranslationService();

            var messageKey = wasLocalAi ? "Command_LocalAiFailoverToGoogle" : "Command_FailoverToGoogle";
            var fallbackMessage = wasLocalAi
                ? "Local AI server is not responding. Switching automatically to Google Translate."
                : "Primary engine limit reached. Switching automatically to Google Translate.";
            ChatGui.Print(GetResourceText(messageKey, fallbackMessage));
            return true;
        }

        private static string GetResourceText(string key, string fallback)
        {
            return Resources.ResourceManager.GetString(key, Resources.Culture) ?? fallback;
        }

        private void OnFrameworkUpdate(IFramework framework)
        {
            _chatVisible = UpdateChatVisibilityInternal();

            // Periodically re-check Local AI availability for auto-recovery. This only undoes an
            // automatic switch, which only the Any policy makes; under local policies a stale flag
            // is cleared instead.
            if (_configuration.LocalAiUnavailable && !FailoverDecision.ShouldRunLocalAiRecovery(_configuration.FallbackPolicy, true))
            {
                _configuration.LocalAiUnavailable = false;
                _configuration.Save();
            }
            else if (FailoverDecision.ShouldRunLocalAiRecovery(_configuration.FallbackPolicy, _configuration.LocalAiUnavailable))
            {
                var now = Environment.TickCount64;
                var recoveryIntervalMs = Math.Max(1, _configuration.EngineRecoveryCheckMinutes) * 60_000L;
                if (now - _lastLocalAiCheckMs >= recoveryIntervalMs)
                {
                    _lastLocalAiCheckMs = now;
                    _ = CheckLocalAiRecoveryAsync();
                }
            }

            // Names PLaMo must keep as written: the player and the current party (every 5 s).
            if (Environment.TickCount64 - _lastGameNamesRefreshMs >= 5_000)
            {
                _lastGameNamesRefreshMs = Environment.TickCount64;
                Volatile.Write(ref _gameNames, CollectGameNames());
            }

            // Periodic translation-cache flush (no-op when clean)
            var nowMs = Environment.TickCount64;
            if (nowMs - _lastCacheSaveMs >= 30_000)
            {
                _lastCacheSaveMs = nowMs;
                _translationCache.SaveInBackground();
            }
        }

        /// <summary>Full and first names of the local player and party members. Framework thread only.</summary>
        private static IReadOnlyList<string> CollectGameNames()
        {
            var names = new List<string>();
            void Add(string? fullName)
            {
                if (string.IsNullOrWhiteSpace(fullName))
                    return;
                names.Add(fullName);
                var space = fullName.IndexOf(' ');
                if (space > 0)
                {
                    names.Add(fullName[..space]);
                    names.Add(fullName[(space + 1)..]);
                }
            }

            try
            {
                if (PlayerState.IsLoaded)
                    Add(PlayerState.CharacterName.ToString());
                foreach (var member in PartyList)
                    Add(member.Name.TextValue);
            }
            catch (Exception ex)
            {
                PluginLog.Debug($"Could not read party names: {ex.Message}");
            }

            return names.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private long _lastLocalAiCheckMs = 0;
        private long _lastCacheSaveMs = 0;
        private int _localAiRecoveryCheckInProgress;

        private async Task CheckLocalAiRecoveryAsync()
        {
            if (Interlocked.Exchange(ref _localAiRecoveryCheckInProgress, 1) != 0)
                return;

            try
            {
                var available = await _localAiTranslator.IsAvailableAsync(_shutdownCts.Token);
                if (available)
                {
                    // Config and engine rebuilds happen on the framework thread only.
                    await Framework.RunOnFrameworkThread(() =>
                    {
                        if (_shutdownCts.IsCancellationRequested ||
                            !FailoverDecision.ShouldRunLocalAiRecovery(_configuration.FallbackPolicy, _configuration.LocalAiUnavailable))
                            return;

                        // Restore the Local AI instance as primary if one exists.
                        var localAi = _configuration.Engines.FirstOrDefault(e => e.Type == TranslationEngine.LocalAI && e.Enabled);
                        if (localAi != null)
                        {
                            _configuration.PrimaryEngineId = localAi.Id;
                        }
                        _configuration.LocalAiUnavailable = false;
                        _configuration.Save();
                        UpdateTranslationService();
                        PluginLog.Info("Local AI server is back online. Restoring Local AI engine.");
                        ChatGui.Print(GetResourceText("Command_LocalAiRestored", "Local AI server is back online. Restored Local AI engine."));
                    });
                }
            }
            catch (Exception ex)
            {
                if (_shutdownCts.IsCancellationRequested)
                    return;
                PluginLog.Error(ex, "Error while checking Local AI recovery.");
            }
            finally
            {
                Volatile.Write(ref _localAiRecoveryCheckInProgress, 0);
            }
        }

        /// <summary>
        /// Returns the cached chat visibility state.
        /// </summary>
        public static bool IsChatVisible() => _chatVisible;

        /// <summary>
        /// Chat visibility logic (main thread only)
        /// </summary>
        private static unsafe bool UpdateChatVisibilityInternal()
        {
            if (!ClientState.IsLoggedIn || !PlayerState.IsLoaded) 
            {
                return false;
            }

            if (Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.BetweenAreas] || 
                Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.WatchingCutscene] || 
                Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.OccupiedInCutSceneEvent] || 
                Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.WatchingCutscene78])
            {
                return false;
            }

            var chatLogAddon = (FFXIVClientStructs.FFXIV.Component.GUI.AtkUnitBase*)(nint)GameGui.GetAddonByName("ChatLog");
            bool nativeChatVisible = chatLogAddon != null && chatLogAddon->IsVisible && chatLogAddon->RootNode != null;

            if (nativeChatVisible) return true;

            return true;
        }
    }
}
