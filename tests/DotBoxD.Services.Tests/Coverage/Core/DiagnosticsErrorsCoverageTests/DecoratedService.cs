using DotBoxD.Services.Attributes;

namespace DotBoxD.Services.Tests.Coverage.Core;

[RpcService(Name = "decorated-wire")]
internal interface IDecoratedService
{
    [RpcMethod(Name = "WireMethod")]
    Task RenamedAsync(CancellationToken ct = default);
}
