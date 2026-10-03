// Self-hosted (no third-party/CDN) ES module used by Relio.Web.Time.BrowserTimeZoneReader to
// read the browser's IANA time zone id, so sign-up (#15) can default a new user's time zone to
// it. See the "Dates and time zones" section of AGENTS.md.
export function getBrowserTimeZone() {
    try {
        return Intl.DateTimeFormat().resolvedOptions().timeZone ?? null;
    } catch {
        return null;
    }
}
