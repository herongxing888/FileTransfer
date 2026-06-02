using System.IO;
using FileTransfer.App.ViewModels;
using FileTransfer.Core.Config;
using FileTransfer.Core.Crypto;
using FileTransfer.Core;

namespace FileTransfer.App.Composition;

public sealed record BootArtifacts(
    AppConfig Config,
    string ConfigPath,
    ISecretProtector Protector,
    IPairingHost? PairingHost,
    INodeHost? NodeHost,
    bool IsPaired);

public static class BootSequence
{
    public static BootArtifacts Build(ISecretProtector protector)
    {
        var configPath = AppConfig.DefaultPath;
        var config = AppConfig.Load(configPath, protector) ?? CreateInitialConfig(protector, configPath);

        if (config.IsPaired)
        {
            var cert = config.GetCertificate(protector);
            var fp = Fingerprint.Compute(cert.RawData);
            var node = new NodeHost(new NodeOptions
            {
                DeviceName = config.DeviceName,
                OwnCertificate = cert,
                PeerFingerprint = config.PeerFingerprint!,
                ReceiveDirectory = config.ReceiveDirectory,
            }, fp);
            return new BootArtifacts(config, configPath, protector, null, node, IsPaired: true);
        }
        else
        {
            var pairing = new PairingServiceHost(new FileTransfer.Core.Pairing.PairingServiceOptions
            {
                DeviceName = config.DeviceName,
                OwnCertificate = config.GetCertificate(protector),
            });
            return new BootArtifacts(config, configPath, protector, pairing, null, IsPaired: false);
        }
    }

    private static AppConfig CreateInitialConfig(ISecretProtector protector, string path)
    {
        var config = new AppConfig();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var cert = CertificateFactory.CreateSelfSigned($"FileTransfer-{config.DeviceName}");
        config.SetCertificate(cert, protector);
        config.Save(path, protector);
        return config;
    }
}
