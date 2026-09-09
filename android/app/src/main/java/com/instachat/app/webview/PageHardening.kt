package com.instachat.app.webview

/**
 * JavaScript injected into every page Instagram creates.
 *
 * The native navigation guard (see DmNavigationPolicy) only sees real document
 * navigations. Instagram's web client is a React SPA, so most in-app clicks
 * (Home, Explore, Reels, a profile avatar, ...) are client-side route changes
 * that never trigger a navigation. This script closes that gap.
 */
object PageHardening {

    fun getScript(messageHandlerName: String = "InstaChatHost"): String = """
        (() => {
          'use strict';

          const INBOX = 'https://www.instagram.com/direct/inbox/';
          const ALLOWED = /^\/(direct|accounts|challenge|ajax|api|graphql|oauth)(\/|$)/i;
          const MESSAGE_HANDLER = '${messageHandlerName}';

          const pathAllowed = (p) => ALLOWED.test(p);

          const hideBlockedRailLinks = () => {
            try {
              document.querySelectorAll('nav').forEach((rail) => {
                rail.querySelectorAll('a[href]').forEach((a) => {
                  let u = null;
                  try { u = new URL(a.href, location.href); } catch (e) { return; }
                  if (u && u.origin === location.origin && !pathAllowed(u.pathname)) {
                    a.style.display = 'none';
                  }
                });
              });
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

          setInterval(guard, 500);

          // ---- In-app toasts ----
          const TOAST_HOLD_MS = 6000;
          let overlay = null;
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
                if (node.querySelector('[role="alert"], [role="status"], [aria-live]')) return;
                const text = (node.textContent || '').trim();
                if (text.length > 200) return;
                if (!text) { lastMirroredText.set(node, ''); return; }
                const rect = node.getBoundingClientRect();
                if (rect.width < 40 || rect.height < 12) return;
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

          // ---- Notch cover ----
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
              const bodyBg = getComputedStyle(document.body).backgroundColor;
              const htmlBg = getComputedStyle(document.documentElement).backgroundColor;
              const bg = bodyBg && bodyBg !== 'rgba(0, 0, 0, 0)' ? bodyBg : htmlBg;
              if (bg && bg !== 'rgba(0, 0, 0, 0)') notchCover.style.background = bg;
            } catch (e) { /* ignore */ }
          };
          setInterval(ensureNotchCover, 500);
          ensureNotchCover();

          // ---- Create button hiding ----
          const CREATE_ICON = /^(create|new post)$/i;
          const hideCreateButton = () => {
            try {
              document.querySelectorAll('svg[aria-label]').forEach((svg) => {
                if (!CREATE_ICON.test((svg.getAttribute('aria-label') || '').trim())) return;
                const item = svg.closest('a')
                  || svg.closest('div[role="button"]')
                  || svg.closest('button')
                  || svg.closest('li');
                if (item) item.style.display = 'none';
              });
              document.querySelectorAll('a, button, div[role="button"]').forEach((el) => {
                const label = el.getAttribute('aria-label') || el.getAttribute('title') || '';
                if (CREATE_ICON.test(label.trim())) el.style.display = 'none';
              });
            } catch (e) { /* ignore */ }
          };
          setInterval(hideCreateButton, 500);
          hideCreateButton();

          // ---- Title relay ----
          const sendTitle = () => {
            try {
              const title = document.title || '';
              if (window[MESSAGE_HANDLER] && typeof window[MESSAGE_HANDLER].postMessage === 'function') {
                window[MESSAGE_HANDLER].postMessage(JSON.stringify({ type: 'title', title: title }));
              }
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

          // ---- Force light mode (prevent Instagram dark theme) ----
          const forceLightMode = () => {
            try {
              let existingStyle = document.getElementById('ica-light-mode');
              if (existingStyle) existingStyle.remove();

              const style = document.createElement('style');
              style.id = 'ica-light-mode';
              style.textContent = [
                'html, body {',
                '  color-scheme: light !important;',
                '  background-color: #ffffff !important;',
                '}',
                ':root { --ig-primary-background: #ffffff; --ig-secondary-background: #fafafa; }',
                'textarea, input[type="text"], [contenteditable="true"],',
                '[role="textbox"], [aria-label="Message"],',
                '[aria-label="Message…"], [placeholder*="Message"] {',
                '  background-color: #fafafa !important;',
                '  color: #262626 !important;',
                '}',
                'div[style*="background-color"] {',
                '  /* handled below */',
                '}'
              ].join('\\n');
              (document.head || document.documentElement).appendChild(style);

              // Force colorScheme on documentElement
              document.documentElement.style.colorScheme = 'light';

              // Walk inline-styled elements and neutralise dark backgrounds
              document.querySelectorAll('div, section, main, aside, nav, header, footer').forEach((el) => {
                const bg = el.style.backgroundColor;
                if (!bg) return;
                // If the inline background is very dark, replace with white
                const m = bg.match(/\\d+/g);
                if (m && m.length >= 3) {
                  const r = parseInt(m[0]), g = parseInt(m[1]), b = parseInt(m[2]);
                  if (r < 60 && g < 60 && b < 80) {
                    el.style.backgroundColor = '#ffffff';
                  }
                }
              });
            } catch (e) { /* ignore */ }
          };
          setInterval(forceLightMode, 1000);
          forceLightMode();

          guard();
        })();
    """.trimIndent()
}
