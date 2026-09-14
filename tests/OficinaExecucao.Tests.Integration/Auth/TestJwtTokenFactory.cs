using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using OficinaExecucao.Application.Configuration;

namespace OficinaExecucao.Tests.Integration.Auth;

public static class TestJwtTokenFactory
{
    public static string GerarToken(IJwtSettings settings)
    {
        var credenciais = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.SecretKey)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: settings.Issuer,
            audience: settings.Audience,
            claims: [new Claim(ClaimTypes.Name, "teste")],
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: credenciais);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
