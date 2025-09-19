// wwwroot/js/theme.js
(function () {
    var KEY = 'fe_theme';
    function apply(theme) {
        var t = (theme === 'dark') ? 'dark' : 'light';
        document.body.setAttribute('data-theme', t);
        document.body.setAttribute('data-bs-theme', t); // کمک به Bootstrap 5.3
        var btn = document.getElementById('themeToggle');
        if (btn) {
            btn.innerHTML = (t === 'dark') ? '<i class="bi bi-sun"></i>' : '<i class="bi bi-moon"></i>';
            btn.setAttribute('aria-label', (t === 'dark') ? 'Switch to light' : 'Switch to dark');
        }
    }

    // init on DOM ready
    document.addEventListener('DOMContentLoaded', function () {
        try {
            var saved = localStorage.getItem(KEY) || 'light';
            apply(saved);
        } catch (e) {
            apply('light'); // در صورت مشکل localStorage
        }

        var btn = document.getElementById('themeToggle');
        if (btn) {
            btn.addEventListener('click', function () {
                var cur = document.body.getAttribute('data-theme') || 'light';
                var next = (cur === 'light') ? 'dark' : 'light';
                try { localStorage.setItem(KEY, next); } catch (e) { /* ignore */ }
                apply(next);
            });
        }
    });
})();
