// SQL Server Backup Suite website — progressive enhancement only; every page works without JS.
(function () {
    'use strict';

    // Mobile navigation
    var toggle = document.querySelector('[data-nav-toggle]');
    var nav = document.getElementById('site-nav');
    if (toggle && nav) {
        toggle.addEventListener('click', function () {
            var open = nav.classList.toggle('is-open');
            toggle.setAttribute('aria-expanded', open ? 'true' : 'false');
            toggle.setAttribute('aria-label', open ? 'Close menu' : 'Open menu');
        });
    }

    // Screenshot lightbox (without JS the link simply opens the full-size image)
    var dialog = document.querySelector('[data-lightbox-dialog]');
    var dialogImage = document.querySelector('[data-lightbox-image]');
    if (dialog && dialogImage && typeof dialog.showModal === 'function') {
        document.addEventListener('click', function (e) {
            var link = e.target.closest('[data-lightbox]');
            if (!link) return;
            e.preventDefault();
            var img = link.querySelector('img');
            dialogImage.src = link.getAttribute('href');
            dialogImage.alt = img ? img.alt : '';
            dialog.showModal();
        });
        dialog.addEventListener('click', function (e) {
            if (e.target === dialog || e.target.closest('[data-lightbox-close]')) dialog.close();
        });
        dialog.addEventListener('close', function () { dialogImage.removeAttribute('src'); });
    }

    // Google Analytics consent (Consent Mode v2); the banner exists only when consent is required
    var banner = document.querySelector('[data-consent-banner]');
    var storageKey = 'sqlbackup-consent';
    function readConsent() {
        try { return localStorage.getItem(storageKey); } catch (e) { return null; }
    }
    function saveConsent(value) {
        try { localStorage.setItem(storageKey, value); } catch (e) { /* private mode */ }
        if (typeof window.gtag === 'function') {
            window.gtag('consent', 'update', { analytics_storage: value === 'granted' ? 'granted' : 'denied' });
        }
    }
    if (banner) {
        if (!readConsent()) banner.hidden = false;
        banner.addEventListener('click', function (e) {
            var button = e.target.closest('[data-consent]');
            if (!button) return;
            saveConsent(button.getAttribute('data-consent'));
            banner.hidden = true;
        });
        document.addEventListener('click', function (e) {
            if (e.target.closest('[data-consent-open]')) banner.hidden = false;
        });
    }

    // Analytics events: <a data-ga-event="name"> on click, <div data-ga-pageview-event="name"> on load
    function track(name, params) {
        if (typeof window.gtag === 'function') window.gtag('event', name, params || {});
    }
    document.addEventListener('click', function (e) {
        var el = e.target.closest('[data-ga-event]');
        if (el) track(el.getAttribute('data-ga-event'), { link_url: el.href || undefined, link_text: (el.textContent || '').trim() });
    });
    document.querySelectorAll('[data-ga-pageview-event]').forEach(function (el) {
        track(el.getAttribute('data-ga-pageview-event'));
    });
})();
