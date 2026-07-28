using System.ComponentModel.DataAnnotations;
using NovaGame.Domain.Shared.Operations.AdminAccess;

namespace NovaGame.Application.Contracts.Operations.AdminAccess;

public sealed class LoginRequest
{
    [Required]
    [StringLength(
        AdminAccessConstants.UserNameMaximumLength,
        MinimumLength = AdminAccessConstants.UserNameMinimumLength)]
    public string UserName { get; init; } = string.Empty;

    [Required]
    [StringLength(256, MinimumLength = 1)]
    public string Password { get; init; } = string.Empty;
}
