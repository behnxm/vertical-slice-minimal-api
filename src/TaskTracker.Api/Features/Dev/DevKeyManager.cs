using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace TaskTracker.Api.Features.Dev;

/// <summary>
/// Manages local ECDSA P-256 key pair for the development-only token issuer.
/// Persists key to disk in a git-ignored directory to maintain stability across server restarts.
/// </summary>
public sealed class DevKeyManager
{
    public const string KeyId = "tasktracker-dev-key";
    private readonly ECDsaSecurityKey _securityKey;
    private readonly ECDsa _ecdsa;

    public DevKeyManager(string keyPath)
    {
        var directory = Path.GetDirectoryName(keyPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (File.Exists(keyPath))
        {
            var pem = File.ReadAllText(keyPath);
            _ecdsa = ECDsa.Create();
            _ecdsa.ImportFromPem(pem);
        }
        else
        {
            _ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var pem = _ecdsa.ExportECPrivateKeyPem();
            File.WriteAllText(keyPath, pem);
        }

        _securityKey = new ECDsaSecurityKey(_ecdsa)
        {
            KeyId = KeyId
        };
    }

    public ECDsaSecurityKey SigningKey => _securityKey;

    public JsonWebKey GetPublicJwk()
    {
        var parameters = _ecdsa.ExportParameters(false);
        return new JsonWebKey
        {
            Kty = JsonWebAlgorithmsKeyTypes.EllipticCurve,
            Crv = JsonWebKeyECTypes.P256,
            X = Base64UrlEncoder.Encode(parameters.Q.X!),
            Y = Base64UrlEncoder.Encode(parameters.Q.Y!),
            Kid = KeyId,
            Use = "sig",
            Alg = SecurityAlgorithms.EcdsaSha256
        };
    }
}
