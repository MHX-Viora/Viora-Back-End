namespace viora_BE.Sso;

public static class SsoCertificateProvisioningCommand
{
    public static int Run(string[] args)
    {
        if (args.Length != 1 && !(args.Length == 3 && args[1] == "--output"))
        {
            Console.Error.WriteLine("Usage: --provision-sso-certificates [--output PRIVATE_DIRECTORY]");
            return 2;
        }
        try
        {
            var directory = args.Length == 3 ? args[2] : Path.Combine(".sso-certificates", "current");
            _ = SsoCertificateProvisioningService.Provision(directory);
            Console.WriteLine("SSO certificate bundle is ready. Import the two .pfx.b64 files as Render Secret Files and the values in render.env as private Environment variables. Retain this bundle securely; rerunning reuses it.");
            return 0;
        }
        catch (Exception error) when (error is Microsoft.Extensions.Options.OptionsValidationException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            Console.Error.WriteLine("SSO provisioning failed. Check private directory permissions and existing certificate validity. Existing keys were not replaced. See docs/operations/sso-certificates-render.md.");
            return 1;
        }
    }
}
