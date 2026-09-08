using Gtk;

namespace InstaChat;

/// <summary>Small non-resizable About dialog, mirroring the Windows one.</summary>
public sealed class AboutDialog : Window
{
    public AboutDialog(Window parent)
    {
        Title = "About InstaChat";
        Resizable = false;
        Modal = true;
        SetTransientFor(parent);
        SetDefaultSize(440, 0);

        var box = Gtk.Box.New(Orientation.Vertical, 10);
        box.MarginTop = 24;
        box.MarginBottom = 24;
        box.MarginStart = 24;
        box.MarginEnd = 24;

        var title = new Label { Wrap = true, Halign = Align.Start };
        title.SetMarkup("<span size='x-large' weight='bold'>InstaChat</span>");

        var version = new Label { Wrap = true, Halign = Align.Start };
        version.SetLabel($"Version {MainWindow.VersionLabel}");
        version.AddCssClass("dim-label");

        var description = new Label { Wrap = true, Halign = Align.Start };
        description.SetLabel("Chat-only Instagram. Direct messages without Reels, Explore, Feed or Stories.");

        var privacyHeader = new Label { Wrap = true, Halign = Align.Start };
        privacyHeader.SetMarkup("<span weight='bold'>Privacy</span>");

        var privacy = new Label { Wrap = true, Halign = Align.Start };
        privacy.SetLabel("• Your Instagram password is never seen or stored — you sign in on Instagram's own secure page.\n"
                       + "• Session cookies are kept in a dedicated local browser profile under ~/.local/share/InstaChat.\n"
                       + "• The app only loads the Direct Messaging experience; navigation to any other part of Instagram is blocked.\n"
                       + "• No analytics, no advertising SDKs, no data collection.");

        var close = Gtk.Button.NewWithLabel("Close");
        close.Halign = Align.End;
        close.OnClicked += (_, _) => Close();

        box.Append(title);
        box.Append(version);
        box.Append(description);
        box.Append(privacyHeader);
        box.Append(privacy);
        box.Append(close);
        SetChild(box);
    }
}