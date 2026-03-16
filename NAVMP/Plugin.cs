using HarmonyLib;
using IPA;
using IPA.Loader;
using SiraUtil.Zenject;
using NAVMP.MpMenu;
using IpaLogger = IPA.Logging.Logger;

namespace NAVMP;

[Plugin(RuntimeOptions.DynamicInit)]
internal class Plugin
{
    public const string ID = "lol.dolphay.navmp";
    internal static IpaLogger Logger { get; private set; } = null!;

    private readonly Harmony _harmony;
    private readonly PluginMetadata _metadata;

    // Methods with [Init] are called when the plugin is first loaded by IPA.
    // All the parameters are provided by IPA and are optional.
    // The constructor is called before any method with [Init]. Only use [Init] with one constructor.
    [Init]
    public Plugin(IpaLogger logger, PluginMetadata pluginMetadata, Zenjector zenjector)
    {
        _harmony = new Harmony(ID);
        _metadata = pluginMetadata;

        Logger = logger;

        zenjector.UseMetadataBinder<Plugin>();
        zenjector.UseLogger(logger);
        zenjector.UseHttpService();
        zenjector.Install<MpMenuInstaller>(Location.Menu);

        Logger.Info($"{pluginMetadata.Name} {pluginMetadata.HVersion} initialized.");
    }

    [OnStart]
    public void OnApplicationStart()
    {
        _harmony.PatchAll(_metadata.Assembly);
    }

    [OnExit]
    public void OnApplicationQuit()
    {
        _harmony.UnpatchSelf();
    }
}