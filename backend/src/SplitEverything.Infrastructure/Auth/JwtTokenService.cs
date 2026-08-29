using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using SplitEverything.Application.Abstractions;
using SplitEverything.Domain.Entities;

namespace SplitEverything.Infrastructure.Auth;

public interface IJwtTokenService
{
    (string Token, DateTimeOffset ExpiresAt) CreateAccessToken(User user, string? deviceId);

    /// <summary>
    /// Returns the plaintext to hand the client and the hash to store. The plaintext
    /// is never persisted, so a database leak does not yield usable sessions.
    /// </summary>
    (string Token, string Hash, DateTimeOffset ExpiresAt) CreateRefreshToken();

    string HashRefreshToken(string token);
}

public sealed class JwtTokenService(AuthOptions options, IClock clock) : IJwtTokenService
{
    public (string Token, DateTimeOffset ExpiresAt) CreateAccessToken(User user, string? deviceId)
    {
        if (options.JwtSigningKey.Length < 32)
            throw new InvalidOperationException("Auth:JwtSigningKey must be at least 32 characters.");

        var expiresAt = clock.UtcNow.AddMinutes(options.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Subject, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new("name", user.DisplayName),
            new(JwtRegisteredClaimNames.JwtId, Guid.CreateVersion7().ToString())
        };

        if (!string.IsNullOrWhiteSpace(deviceId)) claims.Add(new Claim("device", deviceId));

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.JwtSigningKey)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: options.JwtIssuer,
            audience: options.JwtAudience,
            claims: claims,
            notBefore: clock.UtcNow.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }

    public (string Token, string Hash, DateTimeOffset ExpiresAt) CreateRefreshToken()
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

        return (token, HashRefreshToken(token), clock.UtcNow.AddDays(options.RefreshTokenDays));
    }

    public string HashRefreshToken(string token)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}

/// <summary>Claim names spelled out, so the shape of our token is readable here.</summary>
internal static class JwtRegisteredClaimNames
{
    public const string Subject = "sub";
    public const string Email = "email";
    public const string JwtId = "jti";
}
