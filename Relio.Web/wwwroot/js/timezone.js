// Self-hosted (no third-party/CDN) ES module used by Relio.Web.Time.BrowserTimeZoneReader to
// read the browser's IANA time zone id, so account settings (#18) can suggest it as the user's
// time zone (sign-up, #15, duplicates this one line in a plain script - see Register.razor). See
// the "Dates and time zones" section of AGENTS.md.
export function getBrowserTimeZone() {
    try {
        return Intl.DateTimeFormat().resolvedOptions().timeZone ?? null;
    } catch {
        return null;
    }
}
