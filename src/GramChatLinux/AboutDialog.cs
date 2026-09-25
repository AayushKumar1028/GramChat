using Gtk;

namespace GramChat;

/// <summary>Small non-resizable About dialog, mirroring the Windows one.</summary>
public sealed class AboutDialog : Window
{
    public AboutDialog(Window parent)
    {
        Title = "About GramChat";
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
        title.SetMarkup("<span size='x-large' weight='bold'>GramChat</span>");

        var version = new Label { Wrap = true, Halign = Align.Start };
        version.SetLabel($"Version {MainWindow.VersionLabel}");
        version.AddCssClass("dim-label");

        var description = new Label { Wrap = true, Halign = Align.Start };
        description.SetLabel("The full Instagram experience — feed, profiles, messages, follow requests, private accounts and settings — with Reels and Explore kept out.");

        var privacyHeader = new Label { Wrap = true, Halign = Align.Start };
        privacyHeader.SetMarkup("<span weight='bold'>Privacy</span>");

        var privacy = new Label { Wrap = true, Halign = Align.Start };
        privacy.SetLabel("• Your Instagram password is never seen or stored — you sign in on Instagram's own secure page.\n"
                       + "• Saved logins are stored in an AES-256 encrypted database (SQLCipher) whose key file is readable only by your user account.\n"
                       + "• Reels and Explore are blocked everywhere in the app; every other part of Instagram works normally.\n"
                       + "• Links that leave Instagram open in your default browser.\n"
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