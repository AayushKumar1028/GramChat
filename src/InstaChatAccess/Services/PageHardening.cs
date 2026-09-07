namespace InstaChatAccess.Services;

/// <summary>
/// JavaScript injected into every page Instagram creates.
/// </summary>
/// <remarks>
/// The native navigation guard (see <see cref="DmNavigationPolicy"/>) only sees real document
/// navigations. Instagram's web client is a React SPA, so most in-app clicks (Home, Explore,
/// Reels, a profile avatar, ...) are client-side route changes that never trigger a navigation.
/// This script closes that gap: it watches <c>location.pathname</c> (history hooks + interval)
/// and force-bounces the SPA back to the DM inbox whenever it leaves the chat experience.
/// It also hides the distracting links in the navigation rail and relays the document title
/// (which contains the unread-message count) to the host window.
/// </remarks>
public static class PageHardening
{
    public const string Script = """
        (() => {
          'use strict';

          const INBOX = 'https://www.instagram.com/direct/inbox/';
          const ALLOWED = /^\/(direct|accounts|challenge|ajax|api|graphql|oauth)(\/|$)/i;

          const pathAllowed = (p) => ALLOWED.test(p);

          const hideBlockedNavLinks = () => {
            try {
              const rail = document.querySelector('nav') ?? document;
              rail.querySelectorAll('a[href]').forEach((a) => {
                let u = null;
                try { u = new URL(a.href, location.href); } catch (e) { return; }
                if (u && u.origin === location.origin && !pathAllowed(u.pathname)) {
                  a.style.display = 'none';
                }
              });
              // The Instagram logo links to the home feed.
              document.querySelectorAll('svg[aria-label="Instagram"]').forEach((svg) => {
                const a = svg.closest('a');
                if (a) a.style.display = 'none';
              });
            } catch (e) { /* ignore */ }
          };

          const guard = () => {
            try {
              if (!pathAllowed(location.pathname)) {
                location.replace(INBOX);
                return;
              }
              hideBlockedNavLinks();
            } catch (e) { /* ignore */ }
          };

          // Client-side route changes go through the History API - intercept them.
          const wrap = (fn) => function () {
            const result = fn.apply(this, arguments);
            setTimeout(guard, 0);
            return result;
          };
          try {
            history.pushState = wrap(history.pushState);
            history.replaceState = wrap(history.replaceState);
            window.addEventListener('popstate', () => setTimeout(guard, 0));
          } catch (e) { /* ignore */ }

          // Backstop: correct any route change we did not observe.
          setInterval(guard, 500);

          // Relay the document title (includes unread count, e.g. "(2) Inbox - Direct") to the host.
          const sendTitle = () => {
            try {
              window.chrome?.webview?.postMessage({ type: 'title', title: document.title || '' });
            } catch (e) { /* ignore */ }
          };
          try {
            const titleEl = document.querySelector('title');
            if (titleEl) {
              new MutationObserver(sendTitle).observe(titleEl, { subtree: true, childList: true, characterData: true });
            }
          } catch (e) { /* ignore */ }
          setInterval(sendTitle, 1000);
          sendTitle();

          guard();
        })();
        """;
}
