using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Serialization;
using FileTransfer.Core.Crypto;

namespace FileTransfer.Core.Config;

public sealed class AppConfig
{
    public string DeviceName { get; set; } = Environment.MachineName;
    public string ReceiveDirectory { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "FileTransfer");
    public bool AutoStart { get; set; }

    public string? PeerFingerprint { get; set; }
    public string? PeerDeviceName { get; set; }

    /// Base64 of the DPAPI-protected PFX blob holding this machine's cert + private key.
    public string? ProtectedCertificate { get; set; }

    [JsonIgnore]
    public bool IsPaired => !string.IsNullOrEmpty(PeerFingerprint);

    public void SetCertificate(X509Certificate2 cert, ISecretProtector protector)
    {
        byte[] pfx = CertificateFactory.ExportPfx(cert);
        ProtectedCertificate = Convert.ToBase64String(protector.Protect(pfx));
    }

    public X509Certificate2 GetCertificate(ISecretProtector protector)
    {
        if (string.IsNullOrEmpty(ProtectedCertificate))
            throw new InvalidOperationException("No certificate stored in config.");
        byte[] pfx = protector.Unprotect(Convert.FromBase64String(ProtectedCertificate));
        return CertificateFactory.ImportPfx(pfx);
    }

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public void Save(string path, ISecretProtector protector)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, Json));
    }

    /// Loads config, or returns null if the file does not exist.
    public static AppConfig? Load(string path, ISecretProtector protector)
    {
        if (!File.Exists(path)) return null;
        return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path), Json)
               ?? throw new InvalidDataException("Config file is empty or corrupt.");
    }

    /// Default config path: %APPDATA%\FileTransfer\config.json
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "FileTransfer", "config.json");
}
