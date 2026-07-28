using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using NovaCore.BuildingBlocks.Infrastructure;
using NovaCore.Shared.Security;
using NovaGame.Application.Operations.AdminAccess;

namespace NovaGame.Api.Operations.AdminAccess;

public sealed class JwtAccessTokenIssuer : IAccessTokenIssuer
{
    private readonly AdminAccessJwtOptions _options;
    private readonly IGuidGenerator _guidGenerator;

    public JwtAccessTokenIssuer(
        IOptions<AdminAccessJwtOptions> options,
        IGuidGenerator guidGenerator)
    {
        _options = options.Value;
        _guidGenerator = guidGenerator;
    }

    public string Issue(
        Guid administratorId,
        string userName,
        DateTime issuedAtUtc,
        DateTime expiresAtUtc)
    {
        if (administratorId == Guid.Empty)
        {
            throw new ArgumentException("Administrator ID cannot be empty.", nameof(administratorId));
        }
        if (issuedAtUtc.Kind != DateTimeKind.Utc || expiresAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("JWT timestamps must use UTC.");
        }
        if (expiresAtUtc <= issuedAtUtc)
        {
            throw new ArgumentException("JWT expiry must be later than its issue time.", nameof(expiresAtUtc));
        }

        var claims = new[]
        {
            new Claim(SecurityClaimNames.UserId, administratorId.ToString("D")),
            new Claim(SecurityClaimNames.UserName, userName),
            new Claim(JwtRegisteredClaimNames.Jti, _guidGenerator.Create().ToString("N")),
            new Claim(
                JwtRegisteredClaimNames.Iat,
                new DateTimeOffset(issuedAtUtc).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
                ClaimValueTypes.Integer64)
        };
        var signingCredentials = new SigningCredentials(
            new SymmetricSecurityKey(_options.GetSigningKeyBytes()),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            _options.Issuer,
            _options.Audience,
            claims,
            issuedAtUtc,
            expiresAtUtc,
            signingCredentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
