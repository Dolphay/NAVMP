using Zenject;

namespace NAVMP.MpMenu;

internal class MpMenuInstaller : MonoInstaller
{
    public override void InstallBindings()
    {
        Container.BindInterfacesAndSelfTo<MpMenu>().AsSingle();

        Container.Inject(Container.Resolve<NetworkPlayerEntitlementChecker>());
    }

}