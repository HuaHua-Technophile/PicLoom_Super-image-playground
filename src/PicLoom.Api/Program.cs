using System.Text;
using PicLoom.Api;

// The API is normally launched by scripts/start.ps1 with its stdout redirected to
// artifacts/run/api.out.log. Without an explicit encoding that file is written in the console code
// page (GBK on zh-CN Windows), so any multi-byte character — the truncated prompt marker "…", a
// model alias, an upstream error body — becomes invalid UTF-8: the log stops being readable text and
// grep even treats it as a binary file. Pin the output encoding to UTF-8 (no BOM) before anything
// writes to the console, so redirection and an attached console behave the same way.
var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
try
{
    Console.OutputEncoding = utf8;
}
catch (IOException)
{
    // No console attached (service host / test host); the writer below still applies.
}
catch (PlatformNotSupportedException)
{
    // Platform without console code pages; the writer below still applies.
}

Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), utf8) { AutoFlush = true });

var openBrowser = args.Contains("--open-browser");
var developmentUi = args.Contains("--dev-ui");
var serverArgs = args.Where(arg => arg is not ("--open-browser" or "--dev-ui")).ToArray();
await using var app = await ApiApplication.CreateAsync(serverArgs);
await app.StartAsync();
if (openBrowser) ApiApplication.OpenBrowser(app.Services, developmentUi);
await app.WaitForShutdownAsync();

public partial class Program;
