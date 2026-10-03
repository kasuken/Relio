// Relio's light/dark preference: per-browser storage only. Per-user, server-side persistence
// comes with accounts (epic #14); until then this is the whole story.
//
// Loaded as a plain, self-hosted script (no third-party requests) as the first thing in
// App.razor's <head>, so the stored (or system) preference is applied before first paint -
// avoiding a flash of the wrong theme - and so Relio.Web/Theme/JsThemeModeStore.cs can call
// into it later, from C#, as the user changes their preference.
(function () {
    "use strict";

    var STORAGE_KEY = "relio-theme";

    function getPreference() {
        try {
            return localStorage.getItem(STORAGE_KEY) || "system";
        } catch {
            return "system";
        }
    }

    function setPreference(mode) {
        try {
            localStorage.setItem(STORAGE_KEY, mode);
        } catch {
            // Storage can be unavailable (private browsing, blocked cookies). The in-memory
            // preference for this circuit still works; it just will not persist.
        }
    }

    function systemPrefersDark() {
        return !!(window.matchMedia && window.matchMedia("(prefers-color-scheme: dark)").matches);
    }

    function applyResolvedTheme(isDark) {
        document.documentElement.setAttribute("data-theme", isDark ? "dark" : "light");
    }

    window.Relio = window.Relio || {};
    window.Relio.theme = {
        getPreference: getPreference,
        setPreference: setPreference,
        applyResolvedTheme: applyResolvedTheme,
    };

    var stored = getPreference();
    applyResolvedTheme(stored === "dark" || (stored === "system" && systemPrefersDark()));

    // Flips once MainLayout's circuit has connected and finished its first interactive render
    // (see Components/Layout/MainLayout.razor, OnAfterRenderAsync). A cheap, stable signal for
    // E2E tests (Relio.Web.E2ETests) to wait on instead of guessing with timeouts.
    window.Relio.app = {
        markInteractive: function () {
            document.documentElement.setAttribute("data-app-ready", "true");
        },
    };
})();
