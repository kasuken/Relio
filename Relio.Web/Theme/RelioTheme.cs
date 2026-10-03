using MudBlazor;

namespace Relio.Web.Theme;

/// <summary>
/// The MudBlazor theme built from the Relio design system tokens
/// (docs/design-system/tokens.json). Keep the two in sync: when a token changes,
/// update it here and in wwwroot/app.css.
/// </summary>
public static class RelioTheme
{
    private static readonly string[] Serif = ["Alegreya", "Iowan Old Style", "Georgia", "serif"];
    private static readonly string[] Sans = ["Hanken Grotesk", "system-ui", "-apple-system", "Segoe UI", "sans-serif"];

    public static MudTheme Theme { get; } = new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = "#263A63",                // pen
            PrimaryContrastText = "#FFFFFF",    // on-pen
            Secondary = "#7A3E6D",              // plum
            SecondaryContrastText = "#FFFFFF",
            Tertiary = "#76560A",               // pencil
            TertiaryContrastText = "#FFFFFF",
            Info = "#263A63",
            InfoContrastText = "#FFFFFF",
            Success = "#2F6A3E",
            SuccessContrastText = "#FFFFFF",
            Warning = "#9A4712",
            WarningContrastText = "#FFFFFF",
            Error = "#B3261E",
            ErrorContrastText = "#FFFFFF",
            Background = "#F1F2EE",             // paper
            BackgroundGray = "#E7E9E3",         // surface-sunken
            Surface = "#FFFFFF",                // surface
            AppbarBackground = "#F1F2EE",
            AppbarText = "#1C2333",
            DrawerBackground = "#F1F2EE",
            DrawerText = "#1C2333",
            DrawerIcon = "#545C6C",
            TextPrimary = "#1C2333",            // text
            TextSecondary = "#545C6C",          // text-muted
            TextDisabled = "rgba(84,92,108,0.6)",
            ActionDefault = "#545C6C",
            ActionDisabled = "rgba(84,92,108,0.6)",
            ActionDisabledBackground = "#E7E9E3",
            LinesDefault = "#D6D9D1",           // line
            LinesInputs = "#80867C",            // line-strong
            Divider = "#D6D9D1",
            DividerLight = "#E7E9E3",
            TableLines = "#D6D9D1",
            TableHover = "#E7E9E3",
            Skeleton = "#E7E9E3",
        },
        PaletteDark = new PaletteDark
        {
            Primary = "#A9BDF2",
            PrimaryContrastText = "#12161F",
            Secondary = "#DDA6D0",
            SecondaryContrastText = "#12161F",
            Tertiary = "#E3BC63",
            TertiaryContrastText = "#12161F",
            Info = "#A9BDF2",
            InfoContrastText = "#12161F",
            Success = "#8BCB9C",
            SuccessContrastText = "#12161F",
            Warning = "#F2A86F",
            WarningContrastText = "#12161F",
            Error = "#F4958D",
            ErrorContrastText = "#12161F",
            Background = "#12161F",
            BackgroundGray = "#0D1017",
            Surface = "#1A1F2B",
            AppbarBackground = "#12161F",
            AppbarText = "#E6E8EE",
            DrawerBackground = "#12161F",
            DrawerText = "#E6E8EE",
            DrawerIcon = "#A3A9B8",
            TextPrimary = "#E6E8EE",
            TextSecondary = "#A3A9B8",
            TextDisabled = "rgba(163,169,184,0.6)",
            ActionDefault = "#A3A9B8",
            ActionDisabled = "rgba(163,169,184,0.6)",
            ActionDisabledBackground = "#0D1017",
            LinesDefault = "#2C3241",
            LinesInputs = "#6B7387",
            Divider = "#2C3241",
            DividerLight = "#0D1017",
            TableLines = "#2C3241",
            TableHover = "#0D1017",
            Skeleton = "#2C3241",
        },
        Typography = new Typography
        {
            Default = new DefaultTypography { FontFamily = Sans, FontSize = "0.9375rem", FontWeight = "400", LineHeight = "1.4667", LetterSpacing = "normal" },
            // display: a person's name on their profile
            H1 = new H1Typography { FontFamily = Serif, FontSize = "2.75rem", FontWeight = "500", LineHeight = "1.0909", LetterSpacing = "-0.01em" },
            // heading-1: page titles
            H2 = new H2Typography { FontFamily = Serif, FontSize = "2rem", FontWeight = "500", LineHeight = "1.1875", LetterSpacing = "normal" },
            // heading-2: dashboard sections, dialog titles
            H3 = new H3Typography { FontFamily = Serif, FontSize = "1.5rem", FontWeight = "500", LineHeight = "1.25", LetterSpacing = "normal" },
            // title: panel titles
            H4 = new H4Typography { FontFamily = Sans, FontSize = "1.125rem", FontWeight = "600", LineHeight = "1.3333", LetterSpacing = "normal" },
            H5 = new H5Typography { FontFamily = Sans, FontSize = "1rem", FontWeight = "600", LineHeight = "1.375", LetterSpacing = "normal" },
            H6 = new H6Typography { FontFamily = Sans, FontSize = "0.9375rem", FontWeight = "600", LineHeight = "1.4667", LetterSpacing = "normal" },
            Subtitle1 = new Subtitle1Typography { FontFamily = Sans, FontSize = "0.9375rem", FontWeight = "600", LineHeight = "1.4667", LetterSpacing = "normal" },
            Subtitle2 = new Subtitle2Typography { FontFamily = Sans, FontSize = "0.875rem", FontWeight = "600", LineHeight = "1.4286", LetterSpacing = "normal" },
            // body
            Body1 = new Body1Typography { FontFamily = Sans, FontSize = "0.9375rem", FontWeight = "400", LineHeight = "1.4667", LetterSpacing = "normal" },
            Body2 = new Body2Typography { FontFamily = Sans, FontSize = "0.875rem", FontWeight = "400", LineHeight = "1.4286", LetterSpacing = "normal" },
            // label: sentence case, never uppercase
            Button = new ButtonTypography { FontFamily = Sans, FontSize = "0.875rem", FontWeight = "600", LineHeight = "1.4286", LetterSpacing = "normal", TextTransform = "none" },
            // caption
            Caption = new CaptionTypography { FontFamily = Sans, FontSize = "0.75rem", FontWeight = "500", LineHeight = "1.3333", LetterSpacing = "0.01em" },
            Overline = new OverlineTypography { FontFamily = Sans, FontSize = "0.75rem", FontWeight = "600", LineHeight = "1.3333", LetterSpacing = "0.01em", TextTransform = "none" },
        },
        LayoutProperties = new LayoutProperties
        {
            DefaultBorderRadius = "8px",   // radius-md
            DrawerWidthLeft = "248px",     // nav-width
        },
    };
}
