document.addEventListener('DOMContentLoaded', function () {
    var nodes = Array.prototype.slice.call(document.querySelectorAll('.toast'));
    if (!window.bootstrap || !bootstrap.Toast) return;
    nodes.forEach(function (el) { new bootstrap.Toast(el).show(); });
});
