using Microsoft.Extensions.Configuration;
using Rebex;
using Rebex.IO.FileSystem;
using Rebex.Net;
using Rebex.Net.Servers;
using Rebex.Net.Servers.Core;
using Renci.SshNet;

Console.WriteLine("Starting application...");

SetRebexLicensingKey();

const int port = 2222;

var physicalRootDirectoryPath = Path.Combine(Path.GetTempPath(), "RebexDuplicateFileBug");
if (!Directory.Exists(physicalRootDirectoryPath))
{
    Directory.CreateDirectory(physicalRootDirectoryPath);
}

// This works as expected.
var successUser = new FileServerUser("test1", "test1", physicalRootDirectoryPath, "/");

// This does not work as expected.
var failUser = new FileServerUser("test2", "test2", new LocalFileSystemProvider(physicalRootDirectoryPath), "/");

try
{
    using var fileServer = ConfigureRebexFileServer();

    Console.WriteLine("Starting Rebex file server...");
    fileServer.Start();
    Console.WriteLine("Started Rebex file server.");

    // This works as expected.
    Console.WriteLine("Performing file uploads for success user...");
    await PerformFileUploads(successUser, "test1");

    // This does not work as expected.
    Console.WriteLine("Performing file uploads for fail user...");
    await PerformFileUploads(failUser, "test2");

    Console.WriteLine("Finished running application.");
}
catch (Exception ex)
{
    Console.WriteLine($"Error while running application: {ex.Message}");
}

void SetRebexLicensingKey()
{
    var configuration = new ConfigurationBuilder().AddUserSecrets<Program>().Build();

    Licensing.Key = configuration["rebex:licensingKey"];
}

FileServer ConfigureRebexFileServer()
{
    Console.WriteLine("Configuring Rebex file server...");

    var fileServer = new FileServer();

    fileServer.Settings.AllowedAuthenticationMethods = AuthenticationMethods.Password;
    fileServer.Keys.Add(SshPrivateKey.Generate());
    fileServer.Users.Add(successUser);
    fileServer.Users.Add(failUser);

    fileServer.Bind(port, new SftpModule());

    Console.WriteLine("Configured Rebex file server...");

    return fileServer;
}

static async Task PerformFileUploads(FileServerUser user, string password)
{
    using var sftpClient = new SftpClient("127.0.0.1", port, user.Name, password);
    await sftpClient.ConnectAsync(CancellationToken.None);

    string filename = $"{user.Name}.txt";

    sftpClient.UploadFile(new MemoryStream([1, 2, 3]), filename, canOverride: true);

    try
    {
        // This should raise an error indicating the file already exists on the file server.
        sftpClient.UploadFile(new MemoryStream([1, 2, 3]), filename, canOverride: false);
    }
    catch (Renci.SshNet.Common.SshException ex) when (ex.Message.Contains("exist"))
    {
        // Success - Expected exception.
    }
    catch (Renci.SshNet.Common.SshException ex)
    {
        // Error - Unexpected exception.
        Console.Error.WriteLine($"ERROR - Expected to catch exception indicating file already exists, but instead got exception with message: '{ex.Message}'.");
    }
}