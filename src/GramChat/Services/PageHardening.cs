namespace GramChat.Services;

/// <summary>
/// JavaScript injected into every page Instagram creates.
/// </summary>
/// <remarks>
/// The native navigation guard (see <see cref="NavigationPolicy"/>) only sees real document
/// navigations. Instagram's web client is a React single-page app, so most in-app clicks (a
/// Reels icon, an in-feed reel, a profile's "Reels" tab) are client-side route changes that
/// never trigger a navigation. This script closes that gap: it watches the History API and
/// force-returns the SPA to the Home feed whenever it lands on a Reels or Explore route, and it
/// hides every link/tab that points at one.
///
/// It is deliberately event-driven. The chat-only build ran four separate <c>setInterval</c>
/// loops (500 ms x2, 250 ms, 1000 ms); this version drives all of the DOM work from a single
/// <c>MutationObserver</c> collapsed into one animation frame, with one slow 2 s backstop for
/// route changes the observer cannot see. While a voice/video call is on screen it stops the
/// cosmetic DOM work entirely so the renderer's main thread stays free for WebRTC, and it
/// notifies the host so the shell can keep the screen awake during the call.
/// </remarks>
public static class PageHardening
{
    public const string Script = """
        (() => {
          'use strict';

          const HOME = 'https://www.instagram.com/';
          // Reels, Explore and every reel-adjacent route. Matched per path
          // segment so it also catches a profile's /username/reels/ tab and
          // /reels/audio/... .
          const BLOCKED = /(^|\/)(reel|reels|reelsaudio|clips|explore)(\/|$)/i;
          const TAB_LABEL = /^(reels?|clips|explore)$/i;
          const TOAST_SELECTOR = '[role="alert"], [role="status"], [aria-live="assertive"], [aria-live="polite"]';
          const CALL_LABEL = /^(end call|leave call|join call|turn (off|on) video|switch to (video|audio)|mute audio|unmute audio|decline call|accept call)$/i;
          const TOAST_HOLD_MS = 6000;

          const isBlocked = (p) => BLOCKED.test(p || '');

          // ---- tiny scheduler: collapse bursts of DOM work into one frame ----
          let scheduled = false;
          const schedule = (fn) => {
            if (scheduled) return;
            scheduled = true;
            const run = () => { scheduled = false; try { fn(); } catch (e) { /* ignore */ } };
            if (window.requestAnimationFrame) window.requestAnimationFrame(run);
            else window.setTimeout(run, 16);
          };

          // ---- host bridge: one place that knows how to talk to the shell ----
          const post = (payload) => {
            try { window.chrome?.webview?.postMessage(payload); } catch (e) { /* ignore */ }
          };

          // ================= routing guard =================
          const guard = () => {
            if (isBlocked(location.pathname)) {
              location.replace(HOME);
              return true;
            }
            return false;
          };

          const wrap = (fn) => function () {
            const result = fn.apply(this, arguments);
            setTimeout(guard, 0);
            return result;
          };
          try {
            history.pushState = wrap(history.pushState);
            history.replaceState = wrap(history.replaceState);
            window.addEventListener('popstate', () => setTimeout(guard, 0));
            window.addEventListener('hashchange', () => setTimeout(guard, 0));
          } catch (e) { /* ignore */ }

          // ================= page tidy =================
          // Hide every link/tab that points at Reels or Explore: the rail icons,
          // an in-feed reel card, a profile's "Reels" tab. Unrelated navigation
          // is left alone (the old chat-only build hid all of it).
          const tidy = () => {
            try {
              document.querySelectorAll('a[href]').forEach((a) => {
                let u = null;
                try { u = new URL(a.href, location.href); } catch (e) { return; }
                if (u.origin !== location.origin || !isBlocked(u.pathname)) return;
                const item = a.closest('li')
                  || a.closest('article')
                  || a.closest('[role="tab"]')
                  || a;
                item.style.display = 'none';
              });
              document.querySelectorAll('[role="tab"], [role="menuitem"]').forEach((el) => {
                if (TAB_LABEL.test((el.textContent || '').trim())) el.style.display = 'none';
              });
            } catch (e) { /* ignore */ }
          };

          // ================= call mode =================
          // While a call is on screen we stop the cosmetic DOM work entirely so
          // the renderer's main thread stays free for WebRTC, and tell the shell
          // so it can keep the screen awake / hold audio focus during the call.
          let callMode = false;
          let lastCallCheck = 0;
          const postCallState = (active) => post({ type: 'call', active: active });
          const detectCall = (force) => {
            const now = Date.now();
            if (!force && now - lastCallCheck < 750) return;
            lastCallCheck = now;
            let active = false;
            try {
              active = Array.from(document.querySelectorAll('[aria-label]')).some((el) => {
                const label = (el.getAttribute('aria-label') || '').trim();
                if (!CALL_LABEL.test(label)) return false;
                const r = el.getBoundingClientRect();
                return r.width > 0 && r.height > 0;
              });
            } catch (e) { active = false; }
            if (active !== callMode) {
              callMode = active;
              postCallState(active);
            }
          };

          // ================= in-app toasts =================
          // Instagram's own toast banners auto-dismiss almost immediately; mirror
          // every one into a fixed overlay and hold it for a few seconds so it
          // stays readable. OS-level (WebView2/WebKit) notifications are
          // unaffected - the shell already auto-grants the Notifications
          // permission.
          let overlay = null;
          const lastMirroredText = new WeakMap();

          const ensureOverlay = () => {
            if (overlay && overlay.isConnected) return overlay;
            overlay = document.createElement('div');
            overlay.id = 'gcm-toast-overlay';
            overlay.setAttribute('aria-live', 'polite');
            overlay.style.cssText = [
              'position: fixed', 'right: 16px', 'bottom: 16px', 'z-index: 2147483647',
              'display: flex', 'flex-direction: column', 'gap: 8px',
              'pointer-events: none', 'max-width: 420px'
            ].join(';');
            try { (document.body || document.documentElement).appendChild(overlay); } catch (e) { return null; }
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
                'pointer-events: auto', 'background: rgba(38, 38, 38, 0.95)', 'color: #ffffff',
                'padding: 12px 16px', 'border-radius: 10px', 'font-size: 14px', 'line-height: 1.4',
                'font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif',
                'box-shadow: 0 6px 24px rgba(0, 0, 0, 0.35)', 'opacity: 1', 'transition: opacity 0.4s ease'
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
              document.querySelectorAll(TOAST_SELECTOR).forEach((node) => {
                if (node.id === 'gcm-toast-overlay' || overlay?.contains(node)) return;
                // Containers that hold a live region are skipped - the inner node
                // gets mirrored instead (avoids double toasts).
                if (node.querySelector(TOAST_SELECTOR)) return;
                const text = (node.textContent || '').trim();
                if (text.length > 200) return;
                if (!text) { lastMirroredText.set(node, ''); return; }
                const rect = node.getBoundingClientRect();
                if (rect.width < 40 || rect.height < 12) return;   // screen-reader only
                if (lastMirroredText.get(node) === text) return;
                lastMirroredText.set(node, text);
                mirrorToast(node);
              });
            } catch (e) { /* ignore */ }
          };

          // ================= title relay =================
          // Relay Instagram's document title (it carries the unread count) to the
          // shell, but only when it actually changes.
          let lastTitle = null;
          const sendTitle = () => {
            const title = document.title || '';
            if (title === lastTitle) return;
            lastTitle = title;
            post({ type: 'title', title: title });
          };

          // ================= one observer drives all of it =================
          let mo = null;
          const onDomChanged = () => {
            detectCall(false);
            if (callMode) return;   // keep the main thread free during calls
            tidy();
            scanForToasts();
          };
          const startObserver = () => {
            if (mo || !document.documentElement) return;
            try {
              mo = new MutationObserver(() => schedule(onDomChanged));
              mo.observe(document.documentElement, { childList: true, subtree: true, characterData: true });
            } catch (e) { /* ignore */ }
          };
          try {
            const titleEl = document.querySelector('title');
            if (titleEl) {
              new MutationObserver(sendTitle).observe(titleEl, { subtree: true, childList: true, characterData: true });
            }
          } catch (e) { /* ignore */ }

          // Slow backstop: correct a route change we never observed, re-attach
          // the observer after a full reload, and refresh the title. A single
          // timer replaces the four separate setInterval loops of the old build.
          setInterval(() => {
            guard();
            startObserver();
            detectCall(false);
            if (!callMode) { tidy(); scanForToasts(); }
            sendTitle();
          }, 2000);

          // Initial pass.
          guard();
          startObserver();
          detectCall(true);
          if (!callMode) { tidy(); scanForToasts(); }
          sendTitle();
        })();
        """;
}
