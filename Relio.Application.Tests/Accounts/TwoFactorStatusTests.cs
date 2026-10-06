using Relio.Application.Accounts;

namespace Relio.Application.Tests.Accounts;

/// <summary>
/// The low-recovery-codes rule (issue #20): the warning only ever applies while two-factor
/// authentication is on, and starts at three codes left.
/// </summary>
public class TwoFactorStatusTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void RecoveryCodesLow_when_enabled_and_at_or_below_three(int codesLeft)
    {
        new TwoFactorStatus(IsEnabled: true, codesLeft).RecoveryCodesLow.Should().BeTrue();
    }

    [Theory]
    [InlineData(4)]
    [InlineData(10)]
    public void RecoveryCodesLow_is_false_with_more_than_three_left(int codesLeft)
    {
        new TwoFactorStatus(IsEnabled: true, codesLeft).RecoveryCodesLow.Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void RecoveryCodesLow_is_false_when_two_factor_is_off(int codesLeft)
    {
        new TwoFactorStatus(IsEnabled: false, codesLeft).RecoveryCodesLow.Should().BeFalse();
    }

    [Fact]
    public void The_threshold_is_three()
    {
        TwoFactorStatus.LowRecoveryCodeThreshold.Should().Be(3);
    }
}
