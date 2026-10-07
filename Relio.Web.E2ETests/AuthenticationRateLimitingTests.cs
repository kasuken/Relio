using System.Net;
using Microsoft.Playwright;
using Relio.Data.Seeding;
using Relio.Web.E2ETests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Relio.Web.E2ETests;

[Collection(RelioAppCollection.Name)]
public sealed class AuthenticationRateLimitingTests(RelioAppFixture fixture)
{
    [Fact]
    public async Task Login_and_two_factor_posts_share_a_per_peer_budget_and_return_retry_after()
    {
        await using var variant = CreateLimitedVariant(
            ("Security__RateLimiting__LoginPermitLimit", "2"));
        var page = await variant.NewPageAsync();

        var unknown = await SubmitLoginAsync(page, $"unknown-{Guid.NewGuid():N}@example.com");
        unknown.Status.Should().Be((int)HttpStatusCode.OK);
        await Expect(page.GetByTestId("login-error"))
            .ToHaveTextAsync("Email or password is incorrect.");

        var known = await SubmitLoginAsync(page, DemoDataSeeder.DemoEmail);
        known.Status.Should().Be((int)HttpStatusCode.OK);
        await Expect(page.GetByTestId("login-error"))
            .ToHaveTextAsync("Email or password is incorrect.");

        var blockedLogin = await SubmitLoginAsync(page, $"another-{Guid.NewGuid():N}@example.com");
        await AssertRateLimitedAsync(page, blockedLogin);

        var blockedAuthenticator = await SubmitHiddenFormAsync(
            page,
            "/Account/LoginWith2fa?returnUrl=%2F&rememberMe=false",
            "/Account/LoginWith2fa",
            "[data-testid='login-2fa-code']",
            "123456");
        await AssertRateLimitedAsync(page, blockedAuthenticator);

        var blockedRecovery = await SubmitHiddenFormAsync(
            page,
            "/Account/LoginWithRecoveryCode?returnUrl=%2F&rememberMe=false",
            "/Account/LoginWithRecoveryCode",
            "[data-testid='login-recovery-code']",
            "ABCDE-FGHIJ");
        await AssertRateLimitedAsync(page, blockedRecovery);

        var getLogin = await page.GotoAsync("/Account/Login");
        getLogin.Should().NotBeNull();
        getLogin!.Status.Should().Be((int)HttpStatusCode.OK);

        using var client = new HttpClient { BaseAddress = new Uri(variant.Factory.ServerAddress) };
        using var unrelatedPost = await client.PostAsync("/Account/NotALimitedForm", new StringContent(string.Empty));
        unrelatedPost.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Canonical_and_case_insensitive_trailing_slash_login_paths_share_one_budget()
    {
        await using var variant = CreateLimitedVariant(
            ("Security__RateLimiting__LoginPermitLimit", "1"));
        using var client = CreateClient(variant);

        using var canonical = await PostEmptyFormAsync(client, "/Account/Login");
        canonical.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);

        using var alias = await PostEmptyFormAsync(client, "/aCcOuNt/lOgIn/");
        await AssertRateLimitedAsync(alias);
    }

    [Fact]
    public async Task Delete_account_reauthentication_and_its_alias_use_the_bounded_login_budget()
    {
        await using var variant = CreateLimitedVariant(
            ("Security__RateLimiting__LoginPermitLimit", "2"));
        using var client = CreateClient(variant);

        using var login = await PostEmptyFormAsync(client, "/Account/Login");
        login.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);

        using var canonical = await PostEmptyFormAsync(client, "/Account/Manage/DeleteAccount");
        canonical.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);

        using var alias = await PostEmptyFormAsync(client, "/account/manage/deleteaccount/");
        await AssertRateLimitedAsync(alias);
    }

    [Fact]
    public async Task Login_prefix_paths_are_not_limited_as_login_aliases()
    {
        await using var variant = CreateLimitedVariant(
            ("Security__RateLimiting__LoginPermitLimit", "1"));
        using var client = CreateClient(variant);

        using var login = await PostEmptyFormAsync(client, "/Account/Login");
        login.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);

        foreach (var path in new[]
                 {
                     "/Account/LoginExtra",
                     "/Account/Login/Other",
                     "/Account/Manage/DeleteAccountExtra",
                 })
        {
            using var unrelated = await PostEmptyFormAsync(client, path);
            unrelated.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
        }
    }

    [Fact]
    public async Task Registration_uses_its_configured_post_budget()
    {
        await using var variant = CreateLimitedVariant(
            ("Security__RateLimiting__RegistrationPermitLimit", "1"));
        var page = await variant.NewPageAsync();

        var first = await SubmitRegistrationAsync(page, $"weak-{Guid.NewGuid():N}@example.com");
        first.Status.Should().Be((int)HttpStatusCode.OK);
        await Expect(page.GetByTestId("register-error")).ToBeVisibleAsync();

        var second = await SubmitRegistrationAsync(page, $"weak-{Guid.NewGuid():N}@example.com");
        await AssertRateLimitedAsync(page, second);
    }

    [Fact]
    public async Task Forgot_and_reset_password_posts_share_their_configured_budget()
    {
        await using var variant = CreateLimitedVariant(
            ("Security__RateLimiting__PasswordResetPermitLimit", "1"));
        var page = await variant.NewPageAsync();

        var forgot = await SubmitForgotPasswordAsync(page, $"unknown-{Guid.NewGuid():N}@example.com");
        forgot.Status.Should().Be((int)HttpStatusCode.Redirect);

        var reset = await SubmitHiddenFormAsync(
            page,
            "/Account/ResetPassword?userId=synthetic-user&code=synthetic-token",
            "/Account/ResetPassword",
            "[data-testid='reset-password-new']",
            "Strong-Password-123!",
            ("[data-testid='reset-password-confirm']", "Strong-Password-123!"));
        await AssertRateLimitedAsync(page, reset);
    }

    private VariantApp CreateLimitedVariant(params (string Name, string Value)[] settings) =>
        VariantApp.Create(
            fixture,
            settings.ToDictionary(setting => setting.Name, setting => (string?)setting.Value));

    private static async Task<IResponse> SubmitLoginAsync(IPage page, string email)
    {
        await page.GotoAsync("/Account/Login");
        await page.GetByTestId("login-email").FillAsync(email);
        await page.GetByTestId("login-password").FillAsync("Incorrect-Password-123!");
        return await SubmitByClickAsync(page, "/Account/Login", "login-submit");
    }

    private static async Task<IResponse> SubmitRegistrationAsync(IPage page, string email)
    {
        await page.GotoAsync("/Account/Register");
        await page.GetByTestId("register-email").FillAsync(email);
        await page.GetByTestId("register-password").FillAsync("weak");
        await page.GetByTestId("register-confirm-password").FillAsync("weak");
        return await SubmitByClickAsync(page, "/Account/Register", "register-submit");
    }

    private static async Task<IResponse> SubmitForgotPasswordAsync(IPage page, string email)
    {
        await page.GotoAsync("/Account/ForgotPassword");
        await page.GetByTestId("forgot-password-email").FillAsync(email);
        return await SubmitByClickAsync(page, "/Account/ForgotPassword", "forgot-password-submit");
    }

    private static async Task<IResponse> SubmitByClickAsync(IPage page, string path, string submitTestId)
    {
        var responseTask = WaitForPostResponseAsync(page, path);
        await page.GetByTestId(submitTestId).ClickAsync();
        return await responseTask;
    }

    private static async Task<IResponse> SubmitHiddenFormAsync(
        IPage page,
        string navigationPath,
        string postPath,
        string inputSelector,
        string value,
        params (string Selector, string Value)[] additionalValues)
    {
        await page.GotoAsync(navigationPath);
        var input = page.Locator(inputSelector);
        await input.EvaluateAsync("(element, value) => { element.value = value; }", value);
        foreach (var (selector, additionalValue) in additionalValues)
        {
            await page.Locator(selector).EvaluateAsync(
                "(element, value) => { element.value = value; }",
                additionalValue);
        }

        var responseTask = WaitForPostResponseAsync(page, postPath);
        await input.EvaluateAsync("element => element.form.requestSubmit()");
        return await responseTask;
    }

    private static Task<IResponse> WaitForPostResponseAsync(IPage page, string path) =>
        page.WaitForResponseAsync(response =>
            string.Equals(response.Request.Method, "POST", StringComparison.OrdinalIgnoreCase)
            && string.Equals(
                new Uri(response.Url).AbsolutePath,
                path,
                StringComparison.OrdinalIgnoreCase));

    private static HttpClient CreateClient(VariantApp variant) => new(new HttpClientHandler
    {
        AllowAutoRedirect = false,
    })
    {
        BaseAddress = new Uri(variant.Factory.ServerAddress),
    };

    private static Task<HttpResponseMessage> PostEmptyFormAsync(HttpClient client, string path) =>
        client.PostAsync(path, new StringContent(string.Empty));

    private static async Task AssertRateLimitedAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        response.Headers.TryGetValues("Retry-After", out var retryAfterValues).Should().BeTrue();
        int.TryParse(retryAfterValues!.Single(), out var seconds).Should().BeTrue();
        seconds.Should().BeGreaterThan(0);
        response.Headers.TryGetValues("Cache-Control", out var cacheControl).Should().BeTrue();
        cacheControl!.Single().Should().Contain("no-store");
        response.Headers.TryGetValues("X-Content-Type-Options", out var contentTypeOptions).Should().BeTrue();
        contentTypeOptions!.Single().Should().Be("nosniff");
        response.Headers.TryGetValues("Referrer-Policy", out var referrerPolicy).Should().BeTrue();
        referrerPolicy!.Single().Should().Be("no-referrer");
        response.Headers.TryGetValues("X-Frame-Options", out var frameOptions).Should().BeTrue();
        frameOptions!.Single().Should().Be("DENY");
        response.Headers.TryGetValues("Content-Security-Policy", out var contentSecurityPolicy).Should().BeTrue();
        contentSecurityPolicy!.Single().Should().Contain("frame-ancestors 'none'");
        (await response.Content.ReadAsStringAsync())
            .Should().Be("Too many attempts. Please wait before trying again.");
    }

    private static async Task AssertRateLimitedAsync(IPage page, IResponse response)
    {
        response.Status.Should().Be((int)HttpStatusCode.TooManyRequests);
        var headers = await response.AllHeadersAsync();
        var retryAfter = GetHeader(headers, "Retry-After");
        int.TryParse(retryAfter, out var seconds).Should().BeTrue();
        seconds.Should().BeGreaterThan(0);
        GetHeader(headers, "Cache-Control").Should().Contain("no-store");
        GetHeader(headers, "X-Content-Type-Options").Should().Be("nosniff");
        GetHeader(headers, "Referrer-Policy").Should().Be("no-referrer");
        GetHeader(headers, "X-Frame-Options").Should().Be("DENY");
        GetHeader(headers, "Content-Security-Policy").Should().Contain("frame-ancestors 'none'");
        (await page.Locator("body").TextContentAsync())
            .Should().Be("Too many attempts. Please wait before trying again.");
    }

    private static string GetHeader(IReadOnlyDictionary<string, string> headers, string name) =>
        headers.Single(header => string.Equals(header.Key, name, StringComparison.OrdinalIgnoreCase)).Value;
}
