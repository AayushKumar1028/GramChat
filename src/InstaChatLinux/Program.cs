using Gtk;

namespace InstaChat;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // A unique application id makes GtkApplication a single-instance app
        // (like the Windows mutex): a second launch activates the existing
        // window instead of starting another copy, which is also required
        // because two instances cannot share one WebKitGTK profile.
        var app = Application.New("com.instachat.InstaChat", Gio.ApplicationFlags.FlagsNone);
        app.OnActivate += (_, _) => MainWindow.ShowOrFocus(app);
        return app.Run(args);
    }
}