namespace InstaChat.Services;

/// <summary>
/// JavaScript injected into every page Instagram creates.
/// </summary>
/// <remarks>
/// The native navigation guard (see <see cref="DmNavigationPolicy"/>) only sees real document
/// navigations. Instagram's web client is a React SPA, so most in-app clicks (Home, Explore,
/// Reels, a profile avatar, ...) are client-side route changes that never trigger a navigation.
/// This script closes that gap: it watches <c>location.pathname</c> (history hooks + interval)
/// and force-bounces the SPA back to the DM inbox whenever it leaves the chat experience.
///
/// It also adjusts the page cosmetically: the navigation rail is reduced to the Direct icon,
/// the top header strip that peeks out under the app's title bar (the "notch") is covered,
/// the Create button is hidden, and in-app toasts are mirrored and held on screen for a few
/// seconds so the user can actually read them. OS-level notifications (WebView2 toasts) are
/// unaffected - the <see cref="MainWindow"/> already auto-grants the Notifications permission.
/// </remarks>
public static class PageHardening
{
    public const string Script = """
        (() => {
          'use strict';

          const INBOX = 'https://www.instagram.com/direct/inbox/';
          const ALLOWED = /^\/(direct|accounts|challenge|ajax|api|graphql|oauth)(\/|$)/i;

          const pathAllowed = (p) => ALLOWED.test(p);

          const hideBlockedRailLinks = () => {
            try {
              // Only inspect the navigation rail(s), never the whole document:
              // blanket document-wide hiding was eating Instagram's in-app toasts
              // (they contain <a> elements too).
              document.querySelectorAll('nav').forEach((rail) => {
                rail.querySelectorAll('a[href]').forEach((a) => {
                  let u = null;
                  try { u = new URL(a.href, location.href); } catch (e) { return; }
                  if (u && u.origin === location.origin && !pathAllowed(u.pathname)) {
                    a.style.display = 'none';
                  }
                });
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
              hideBlockedRailLinks();
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

          // ---------------------------------------------------------------------
          // In-app toasts: Instagram's web client pops its own toast banners
          // ("Message sent", "Copied", ...) that auto-dismiss almost immediately.
          // Mirror every toast into a fixed overlay pinned to the bottom-right of
          // the window and hold it there for 6 seconds, so it stays readable even
          // after Instagram's own copy disappears.
          // ---------------------------------------------------------------------
          const TOAST_HOLD_MS = 6000;
          let overlay = null;
          // Instagram reuses the same live-region node for every toast, so track
          // each node's last mirrored text and mirror again when the text changes.
          const lastMirroredText = new WeakMap();

          const ensureOverlay = () => {
            if (overlay && overlay.isConnected) return overlay;
            overlay = document.createElement('div');
            overlay.id = 'ica-toast-overlay';
            overlay.setAttribute('aria-live', 'polite');
            overlay.style.cssText = [
              'position: fixed',
              'right: 16px',
              'bottom: 16px',
              'z-index: 2147483647',
              'display: flex',
              'flex-direction: column',
              'gap: 8px',
              'pointer-events: none',
              'max-width: 420px'
            ].join(';');
            try {
              (document.body || document.documentElement).appendChild(overlay);
            } catch (e) { return null; }
            return overlay;
          };

          const mirrorToast = (node) => {
            const host = ensureOverlay();
            if (!host) return;
            try {
              const card = document.createElement('div');
              card.textContent = (node.textContent || '').trim();
              if (!card.textContent) return;
              card.style.cssText = [
                'pointer-events: auto',
                'background: rgba(38, 38, 38, 0.95)',
                'color: #ffffff',
                'padding: 12px 16px',
                'border-radius: 10px',
                'font-size: 14px',
                'line-height: 1.4',
                'font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif',
                'box-shadow: 0 6px 24px rgba(0, 0, 0, 0.35)',
                'opacity: 1',
                'transition: opacity 0.4s ease'
              ].join(';');
              host.appendChild(card);

              setTimeout(() => {
                card.style.opacity = '0';
                setTimeout(() => card.remove(), 450);
              }, TOAST_HOLD_MS);
            } catch (e) { /* ignore */ }
          };

          const scanForToasts = () => {
            try {
              if (!document.body) return;
              document.querySelectorAll(
                '[role="alert"], [role="status"], [aria-live="assertive"], [aria-live="polite"]'
              ).forEach((node) => {
                if (node.id === 'ica-toast-overlay' || overlay?.contains(node)) return;
                // Skip containers that themselves contain a live region - the inner
                // node gets mirrored instead (avoids double toasts).
                if (node.querySelector('[role="alert"], [role="status"], [aria-live]')) return;
                const text = (node.textContent || '').trim();
                if (text.length > 200) return;
                // Empty text means the live region was just cleared - remember it.
                if (!text) { lastMirroredText.set(node, ''); return; }
                // Skip visually hidden live regions (screen-reader only) - real
                // toasts are sizeable banners, hidden ones are ~1px or clipped.
                const rect = node.getBoundingClientRect();
                if (rect.width < 40 || rect.height < 12) return;
                // Mirror only when the message actually changed.
                if (lastMirroredText.get(node) === text) return;
                lastMirroredText.set(node, text);
                mirrorToast(node);
              });
            } catch (e) { /* ignore */ }
          };

          try {
            const mo = new MutationObserver(() => scanForToasts());
            mo.observe(document.documentElement, { childList: true, subtree: true });
            setInterval(scanForToasts, 250);
            scanForToasts();
          } catch (e) { /* ignore */ }

          // ---------------------------------------------------------------------
          // The "notch": Instagram renders its own thin header strip at the very
          // top of the page (logo / search bar). It peeks out underneath the
          // app's native title bar and looks like a broken second title bar, so
          // cover it with an opaque bar in the page's background colour.
          // ---------------------------------------------------------------------
          let notchCover = null;
          const ensureNotchCover = () => {
            try {
              if (!document.body) return;
              if (!notchCover || !notchCover.isConnected) {
                notchCover = document.createElement('div');
                notchCover.id = 'ica-notch-cover';
                notchCover.style.cssText = [
                  'position: fixed',
                  'left: 0',
                  'top: 0',
                  'width: 100%',
                  'height: 48px',
                  'background: #ffffff',
                  'z-index: 2147483646',
                  'pointer-events: none'
                ].join(';');
                document.body.appendChild(notchCover);
              }
              // Keep it in sync with light/dark pages.
              const bodyBg = getComputedStyle(document.body).backgroundColor;
              const htmlBg = getComputedStyle(document.documentElement).backgroundColor;
              const bg = bodyBg && bodyBg !== 'rgba(0, 0, 0, 0)' ? bodyBg : htmlBg;
              if (bg && bg !== 'rgba(0, 0, 0, 0)') notchCover.style.background = bg;
            } catch (e) { /* ignore */ }
          };
          setInterval(ensureNotchCover, 500);
          ensureNotchCover();

          // ---------------------------------------------------------------------
          // Create button: Instagram's side rail has a "+ Create" item that opens
          // the post / reel / story composer. It has no place in a chat-only app.
          // ---------------------------------------------------------------------
          const CREATE_ICON = /^(create|new post)$/i;
          const hideCreateButton = () => {
            try {
              // Icon-based item: the rail icon is an <svg aria-label="Create"/"New post">.
              document.querySelectorAll('svg[aria-label]').forEach((svg) => {
                if (!CREATE_ICON.test((svg.getAttribute('aria-label') || '').trim())) return;
                const item = svg.closest('a')
                  || svg.closest('div[role="button"]')
                  || svg.closest('button')
                  || svg.closest('li');
                if (item) item.style.display = 'none';
              });
              // Text-based item ("Create" on wider layouts).
              document.querySelectorAll('a, button, div[role="button"]').forEach((el) => {
                const label = el.getAttribute('aria-label') || el.getAttribute('title') || '';
                if (CREATE_ICON.test(label.trim())) el.style.display = 'none';
              });
            } catch (e) { /* ignore */ }
          };
          setInterval(hideCreateButton, 500);
          hideCreateButton();

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
