// wwwroot/js/site.js

(function () {
    function setIcon(mode) {
        var btn = document.getElementById('themeToggle');
        if (!btn) return;
        btn.innerHTML = (mode === 'dark')
            ? '<i class="bi bi-sun"></i>'
            : '<i class="bi bi-moon"></i>';
    }

    function applyTheme(mode) {
        var m = (mode === 'dark') ? 'dark' : 'light';
        document.body.setAttribute('data-theme', m);
        try { localStorage.setItem('fe_theme', m); } catch (e) { }
        setIcon(m);
    }

    function initTheme() {
        var saved = null;
        try { saved = localStorage.getItem('fe_theme'); } catch (e) { }
        applyTheme(saved === 'dark' ? 'dark' : 'light');

        var btn = document.getElementById('themeToggle');
        if (btn && !btn.dataset.bound) {
            btn.addEventListener('click', function () {
                var cur = document.body.getAttribute('data-theme') || 'light';
                applyTheme(cur === 'light' ? 'dark' : 'light');
            });
            btn.dataset.bound = '1';
        }
    }

    function initToasts() {
        if (!window.bootstrap) return;
        document.querySelectorAll('.toast').forEach(function (el) {
            try { new bootstrap.Toast(el).show(); } catch (e) { }
        });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', function () {
            initTheme();
            initToasts();
        });
    } else {
        initTheme();
        initToasts();
    }
})();
