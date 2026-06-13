using System.Security.Cryptography;
using System.Text;

namespace AxoPrint.Relay;

/// <summary>Single shared-secret check used for both the IPP and agent surfaces.</summary>
public sealed class TokenAuth(IConfiguration config)
{
    private readonly byte[] _token = Encoding.UTF8.GetBytes(
        config["Axo:Token"] ?? throw new InvalidOperationException(
            "Axo:Token is not configured. Set it via env AXO__TOKEN or appsettings."));

    public bool IsValid(string? presented)
    {
        if (string.IsNullOrEmpty(presented))
            return false;
        var bytes = Encoding.UTF8.GetBytes(presented);
        return CryptographicOperations.FixedTimeEquals(bytes, _token);
    }

    public bool IsValidBearer(string? authorizationHeader)
    {
        if (authorizationHeader is null ||
            !authorizationHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return false;
        return IsValid(authorizationHeader["Bearer ".Length..].Trim());
    }
}
