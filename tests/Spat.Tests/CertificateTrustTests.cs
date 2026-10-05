using Microsoft.Extensions.Logging.Abstractions;
using Spat.Libraries.Native.Windows;

namespace Spat.Tests;

public class CertificateTrustTests
{
    // The public half of the Bitbound code-signing cert is embedded in the native library so the
    // app can offer to trust it; these guard the resource name and packaging.
    [Fact]
    public void EmbeddedCertificateIsThePublicKeyOnlyBitboundCert()
    {
        var cert = BitboundCertificateTrustService.PublisherCertificate;

        Assert.Equal("CN=Bitbound", cert.Subject);
        Assert.False(cert.HasPrivateKey);
        Assert.Equal("18FC41D0A2E8190B6BEDCF70CEF1DAF18298BFA8", cert.Thumbprint);
    }

    [Fact]
    public void TrustServiceReadsTrustStateWithoutWriting()
    {
        var service = new BitboundCertificateTrustService(NullLogger<BitboundCertificateTrustService>.Instance);

        // Read-only against the Root stores; must not throw on machines where the cert is absent.
        _ = service.IsPublisherTrusted();
    }
}