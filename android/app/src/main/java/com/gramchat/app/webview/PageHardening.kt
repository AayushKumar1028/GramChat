package com.gramchat.app.webview

/**
 * JavaScript injected into every page Instagram creates.
 *
 * The native navigation guard (see [NavigationPolicy]) only sees real document
 * navigations. Instagram's web client is a React single-page app, so most
 * in-app clicks (a Reels icon, an in-feed reel, a profile's "Reels" tab) are
 * client-side route changes that never trigger a navigation. This script closes
 * that gap.
 *
 * It is deliberately event-driven: all DOM work is driven from one
 * MutationObserver collapsed into a single animation frame, with one slow 2s
 * backstop, instead of the several 250-500 ms intervals the chat-only build
 * ran. While a call is on screen it stops the cosmetic DOM work so the renderer
 * stays free for WebRTC, and it relays the call state to the shell (which keeps
 * the screen awake for the call).
 */
object PageHardening {

    const val DEFAULT_HANDLER = "GramChatHost"

    fun getScript(messageHandlerName: String = DEFAULT_HANDLER): String = """
        (() => {
          'use strict';

          const HOME = 'https://www.instagram.com/';
          const BLOCKED = /(^|\/)(reel|reels|reelsaudio|clips|explore)(\/|$)/i;
          const TAB_LABEL = /^(reels?|clips|explore)$/i;
          const TOAST_SELECTOR = '[role="alert"], [role="status"], [aria-live="assertive"], [aria-live="polite"]';
          const CALL_LABEL = /^(end call|leave call|join call|turn (off|on) video|switch to (video|audio)|mute audio|unmute audio|decline call|accept call)$/i;
          const TOAST_HOLD_MS = 6000;
          const MESSAGE_HANDLER = '${messageHandlerName}';

          const isBlocked = (p) => BLOCKED.test(p || '');

          let scheduled = false;
          const schedule = (fn) => {
            if (scheduled) return;
            scheduled = true;
            const run = () => { scheduled = false; try { fn(); } catch (e) { /* ignore */ } };
            if (window.requestAnimationFrame) window.requestAnimationFrame(run);
            else window.setTimeout(run, 16);
          };

          const post = (payload) => {
            try {
              const bridge = window[MESSAGE_HANDLER];
              if (bridge && typeof bridge.postMessage === 'function') {
                bridge.postMessage(JSON.stringify(payload));
              }
            } catch (e) { /* ignore */ }
          };

          // ---- routing guard ----
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

          // ---- page tidy: hide every Reels / Explore link or tab ----
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

          // ---- call mode ----
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

          // ---- in-app toasts ----
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
                if (node.querySelector(TOAST_SELECTOR)) return;
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

          // ---- title relay (change-only) ----
          let lastTitle = null;
          const sendTitle = () => {
            const title = document.title || '';
            if (title === lastTitle) return;
            lastTitle = title;
            post({ type: 'title', title: title });
          };

          // ---- one observer drives all of it ----
          let mo = null;
          const onDomChanged = () => {
            detectCall(false);
            if (callMode) return;
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

          setInterval(() => {
            guard();
            startObserver();
            detectCall(false);
            if (!callMode) { tidy(); scanForToasts(); }
            sendTitle();
          }, 2000);

          guard();
          startObserver();
          detectCall(true);
          if (!callMode) { tidy(); scanForToasts(); }
          sendTitle();
        })();
    """.trimIndent()
}
