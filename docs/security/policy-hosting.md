# Review-gated public policy hosting

Relio does not ship policy wording. The `/privacy`, `/terms` and `/acceptable-use` routes are
anonymous, static-SSR shells for documents supplied and approved by the operator of the running
instance. They are disabled by default. Enabling a route does not draft, validate or legally approve
its contents.

## Source-code link

Every account, public and authenticated shell displays a source-code link. The top-level
`SourceCodeUrl` setting defaults in code to `https://github.com/kasuken/Relio`, which is suitable
for an unmodified standard release. Operators of modified builds must point it to the source for
the code they actually run; an upstream link alone is not sufficient for a modified build under
Relio's AGPL distribution.

Only absolute HTTP(S) URLs without credentials, query strings or fragments are accepted. HTTPS is
required outside Development. The link is independent of the policy-hosting switch.

## Enabling a document

Provide the document as an absolute external UTF-8 text file, outside the application's content
root, and set a title, unique description, version, UTC review date and explicit human-review
attestation for each enabled document. Files are read once during startup; a missing, unreadable,
empty, oversized or invalid UTF-8 document, or incomplete/unattested configuration, prevents the
application from starting. No policy text or file path is included in startup errors.

The content is rendered as encoded plain text with line breaks preserved. HTML and scripts in a
file are displayed as text, never executed. Supply the complete text that should be published.
Do not set `HumanReviewAttested` until a human has reviewed that exact document version. The flag
records an operator's attestation; it is not evidence that Relio performed legal review.

Example default configuration (policies remain off; do not put policy wording or secrets in
committed application settings):

```json
{
  "SourceCodeUrl": "https://github.com/kasuken/Relio",
  "Seo": {
    "PublicOrigin": "https://relio.example.invalid",
    "SiteName": "Relio"
  },
  "HostedFeatures": {
    "Policies": {
      "Enabled": false,
      "IndexingEnabled": false,
      "Privacy": {
        "Enabled": false,
        "ContentFile": "",
        "Title": "",
        "Description": "",
        "Version": "",
        "HumanReviewAttested": false,
        "ReviewedAtUtc": null
      },
      "Terms": {
        "Enabled": false,
        "ContentFile": "",
        "Title": "",
        "Description": "",
        "Version": "",
        "HumanReviewAttested": false,
        "ReviewedAtUtc": null
      },
      "AcceptableUse": {
        "Enabled": false,
        "ContentFile": "",
        "Title": "",
        "Description": "",
        "Version": "",
        "HumanReviewAttested": false,
        "ReviewedAtUtc": null
      }
    }
  }
}
```

Before enabling a document, provide its external UTF-8 file and instance-appropriate metadata,
then set `HumanReviewAttested` to `true` and record its UTC review time only after a human has
reviewed that exact version. An enabled document without that attestation intentionally prevents
startup. Configuration can also be provided through the platform's configuration store or
environment variables, for example
`HostedFeatures__Policies__Privacy__ContentFile` and `Seo__PublicOrigin`.

When policy hosting is off, the routes answer anonymous 404 responses, policy links are omitted,
and the sitemap is unavailable. The public origin is required only when at least one policy is
enabled. Canonical and social URLs use that configured origin, never the incoming `Host` header.
`robots.txt` only advertises enabled policy routes when indexing is explicitly enabled in
Production; Development and indexing-disabled deployments do not advertise a sitemap.

The static marketing shell and shared metadata/crawler infrastructure here are only the portion
needed to host reviewed policy documents. They do not implement the broader marketing journeys
tracked by issues #86, #87 and #92, and do not change the protected `/` route.
