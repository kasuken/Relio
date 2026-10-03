using Relio.Web.Theme;

namespace Relio.Web.Tests.Theme;

public class ThemeModeStateTests
{
    [Fact]
    public async Task InitializeAsync_with_no_stored_preference_follows_the_system_setting()
    {
        var store = new FakeThemeModeStore { StoredPreference = ThemeMode.System };
        var state = new ThemeModeState(store);

        await state.InitializeAsync(systemIsDark: true);

        state.Mode.Should().Be(ThemeMode.System);
        state.IsDark.Should().BeTrue();
        store.LastApplied.Should().BeTrue();
    }

    [Theory]
    [InlineData(ThemeMode.Light, false)]
    [InlineData(ThemeMode.Dark, true)]
    public async Task InitializeAsync_with_a_stored_preference_ignores_the_system_setting(ThemeMode stored, bool expectedIsDark)
    {
        var store = new FakeThemeModeStore { StoredPreference = stored };
        var state = new ThemeModeState(store);

        // System reports the opposite of the stored preference, to prove it is ignored.
        await state.InitializeAsync(systemIsDark: !expectedIsDark);

        state.Mode.Should().Be(stored);
        state.IsDark.Should().Be(expectedIsDark);
    }

    [Fact]
    public async Task SetModeAsync_persists_the_choice_and_raises_Changed()
    {
        var store = new FakeThemeModeStore();
        var state = new ThemeModeState(store);
        var raised = 0;
        state.Changed += () => raised++;

        await state.SetModeAsync(ThemeMode.Dark);

        state.Mode.Should().Be(ThemeMode.Dark);
        state.IsDark.Should().BeTrue();
        store.StoredPreference.Should().Be(ThemeMode.Dark);
        raised.Should().Be(1);
    }

    [Fact]
    public async Task SetSystemPreferenceAsync_updates_IsDark_only_while_mode_is_System()
    {
        var store = new FakeThemeModeStore();
        var state = new ThemeModeState(store);
        await state.InitializeAsync(systemIsDark: false);

        await state.SetSystemPreferenceAsync(isDark: true);

        state.IsDark.Should().BeTrue();
    }

    [Fact]
    public async Task SetSystemPreferenceAsync_is_a_no_op_once_the_user_chose_Light_or_Dark()
    {
        var store = new FakeThemeModeStore();
        var state = new ThemeModeState(store);
        await state.SetModeAsync(ThemeMode.Light);
        var callsBefore = store.ApplyCallCount;
        var raised = 0;
        state.Changed += () => raised++;

        await state.SetSystemPreferenceAsync(isDark: true);

        state.IsDark.Should().BeFalse("the user explicitly chose Light");
        store.ApplyCallCount.Should().Be(callsBefore);
        raised.Should().Be(0);
    }
}
