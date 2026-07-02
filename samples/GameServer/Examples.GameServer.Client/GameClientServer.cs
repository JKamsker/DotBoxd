namespace DotBoxD.Kernels.Game.Client;

[GeneratePluginServer(
    Context = typeof(GameClientContext),
    ControlService = typeof(IGameClientControlService))]
public partial class GameClientServer : IGameWorldView;

public sealed partial class GameClientContext;
