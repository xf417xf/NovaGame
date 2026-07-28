using NovaCore.BuildingBlocks.Modularity;
using NovaGame.Domain.Shared;

namespace NovaGame.Application.Contracts;

[DependsOn(typeof(NovaGameDomainSharedModule))]
public sealed class NovaGameApplicationContractsModule : NovaCoreModule
{
}
