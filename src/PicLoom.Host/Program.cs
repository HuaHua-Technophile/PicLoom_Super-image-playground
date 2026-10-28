using PicLoom.Host.Infrastructure;
using PicLoom.Host.Ui;

namespace PicLoom.Host;

internal static class Program
{
    private const string SmokeHostSwitch = "--smoke-host";

    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        var smokeHost = args.Any(IsSmokeHostSwitch);
        var apiArgs = args.Where(argument => !IsSmokeHostSwitch(argument)).ToArray();
        if (smokeHost)
        {
            return RunSmokeHost(apiArgs);
        }

        try
        {
            return RunTrayHost(apiArgs);
        }
        catch
        {
            StartupDialogs.ShowFatalFailure();
            return 1;
        }
    }

    private static bool IsSmokeHostSwitch(string argument) =>
        string.Equals(argument, SmokeHostSwitch, StringComparison.OrdinalIgnoreCase);

    private static int RunSmokeHost(string[] args)
    {
        using var controller = new ApiHostController();
        try
        {
            Task.Run(() => controller.StartAsync(args)).GetAwaiter().GetResult();
            if (!controller.ConfirmListening(TimeSpan.FromSeconds(10)))
            {
                Console.Error.WriteLine("PicLoom host smoke test failed: API is not listening.");
                return 1;
            }

            Console.Error.WriteLine("PicLoom host smoke test passed.");
            return 0;
        }
        catch
        {
            Console.Error.WriteLine("PicLoom host smoke test failed.");
            return 1;
        }
        finally
        {
            controller.Stop();
        }
    }

    private static int RunTrayHost(string[] args)
    {
        // The mutex stays owned by this STA thread; API startup runs on the thread pool.
        using var instance = new SingleInstanceCoordinator();
        if (!instance.TryAcquire())
        {
            if (instance.TrySignalOpen(TimeSpan.FromSeconds(15)))
            {
                return 0;
            }

            StartupDialogs.ShowAlreadyRunning();
            return 1;
        }

        using var controller = new ApiHostController();
        try
        {
            Task.Run(() => controller.StartAsync(args)).GetAwaiter().GetResult();
        }
        catch
        {
            StartupDialogs.ShowStartupFailure();
            return 1;
        }

        using var dispatcher = new UiDispatcher();
        using var openRequests = new OpenRequestServer(
            instance.PipeName,
            () => dispatcher.Post(() => OpenInBrowser(controller)));
        openRequests.Start();

        using var tray = new TrayApplicationContext(() => OpenInBrowser(controller));
        OpenInBrowser(controller);
        try
        {
            Application.Run(tray);
        }
        finally
        {
            tray.HideTray();
            openRequests.Stop();
            controller.Stop();
        }

        return 0;
    }

    private static void OpenInBrowser(ApiHostController controller)
    {
        try
        {
            controller.OpenInBrowser();
        }
        catch
        {
            StartupDialogs.ShowOpenFailure();
        }
    }
}
