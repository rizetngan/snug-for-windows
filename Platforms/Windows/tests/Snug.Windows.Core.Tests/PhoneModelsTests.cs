using Snug.Windows.Core;

namespace Snug.Windows.Core.Tests;

public sealed class PhoneModelsTests
{
    private const string CatalogJson = """
    {
      "brands": [
        {
          "name": "Apple",
          "aliases": ["苹果"],
          "models": [
            { "name": "iPhone 17 Pro Max", "year": 2025, "screen": [1320, 2868], "points": 440 },
            { "name": "iPhone 16", "year": 2024, "screen": [1179, 2556], "points": 393 }
          ]
        },
        {
          "name": "华为",
          "aliases": ["HUAWEI"],
          "models": [
            { "name": "Mate 70 Pro", "year": 2024, "screen": [1316, 2832] }
          ]
        }
      ]
    }
    """;

    [Fact]
    public void CustomScreenNormalizesLandscapeInput()
    {
        var screen = PhoneScreen.Custom(2400, 1080);

        Assert.NotNull(screen);
        Assert.Equal(1080, screen.Width);
        Assert.Equal(2400, screen.Height);
        Assert.Null(PhoneScreen.Custom(0, 2400));
        Assert.Null(PhoneScreen.Custom(1080, 20001));
    }

    [Fact]
    public void CatalogLoadsSearchesAndResolvesModels()
    {
        var catalog = PhoneCatalog.FromJson(CatalogJson);

        Assert.Equal(2, catalog.Brands.Count);
        Assert.Equal("Apple iPhone 17 Pro Max", PhoneChoice.Standard.Title);
        Assert.Equal("华为", catalog.Search("huawei").Single().Name);
        Assert.Equal("Mate 70 Pro", catalog.Search("mate70").Single().Models.Single().Name);

        var resolved = catalog.Resolve(new PhoneChoice("Apple iPhone 17 Pro Max", new PhoneScreen(1000, 2000)));
        Assert.Equal(new PhoneScreen(1320, 2868, 440), resolved.Screen);
    }

    [Fact]
    public void AutomaticModelMatchingRequiresOneKnownPhoneAndExactSuffix()
    {
        var catalog = PhoneCatalog.FromJson(CatalogJson);
        Assert.Equal("Apple iPhone 17 Pro Max", catalog.MatchControllerName("明明的 iPhone 17 Pro Max")?.Id);
        Assert.Equal("华为 Mate 70 Pro", catalog.MatchControllerName("HUAWEI Mate 70 Pro")?.Id);
        Assert.Null(catalog.MatchControllerName("iPhone 16 Pro"));
        Assert.Null(catalog.MatchControllerName("iPhone"));
        Assert.Null(catalog.MatchSinglePhoneController([new("iPhone 16", ControllerKind.Desktop)]));
        Assert.Null(catalog.MatchSinglePhoneController([new("iPhone 16", ControllerKind.Phone), new("iPhone 17 Pro Max", ControllerKind.Phone)]));
        Assert.Equal("Apple iPhone 16", catalog.MatchSinglePhoneController([new("iPhone 16", ControllerKind.Phone)])?.Id);
    }

    [Fact]
    public void GenericPresetsProduceSupportedPortraitFits()
    {
        Assert.Equal(5, PhoneCatalog.GenericPresets.Count);
        foreach (var preset in PhoneCatalog.GenericPresets)
        {
            Assert.True(preset.Screen.IsValid);
            var fit = new ScreenFit(preset.Screen, 0);
            Assert.InRange(fit.BaseWidth, ScreenFit.MinWidth, ScreenFit.MaxWidth);
            Assert.True(fit.PixelSizeFor(fit.BaseWidth).Height >= 1600);
        }
    }

    [Fact]
    public void UpstreamPhoneModelAssetLoadsWithoutDataLoss()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "phone-models.json");
        using var stream = File.OpenRead(path);
        var catalog = PhoneCatalog.Load(stream);

        Assert.Equal(17, catalog.Brands.Count);
        Assert.True(catalog.Models.Count > 500);
        Assert.NotNull(catalog.Find(PhoneChoice.Standard.Title));
        Assert.NotNull(catalog.Search("huawei").FirstOrDefault());
    }

    // Preserve the original Snug catalog as the source of truth.
    // Each iPhone 13+ profile must be selectable and produce valid VDD modes.
    [Theory]
    [InlineData("iPhone 13")]
    [InlineData("iPhone 13 mini")]
    [InlineData("iPhone 13 Pro")]
    [InlineData("iPhone 13 Pro Max")]
    [InlineData("iPhone 14")]
    [InlineData("iPhone 14 Plus")]
    [InlineData("iPhone 14 Pro")]
    [InlineData("iPhone 14 Pro Max")]
    [InlineData("iPhone 15")]
    [InlineData("iPhone 15 Plus")]
    [InlineData("iPhone 15 Pro")]
    [InlineData("iPhone 15 Pro Max")]
    [InlineData("iPhone 16")]
    [InlineData("iPhone 16 Plus")]
    [InlineData("iPhone 16 Pro")]
    [InlineData("iPhone 16 Pro Max")]
    [InlineData("iPhone 16e")]
    [InlineData("iPhone 17")]
    [InlineData("iPhone 17 Pro")]
    [InlineData("iPhone 17 Pro Max")]
    [InlineData("iPhone 17e")]
    [InlineData("iPhone 18 Pro")]
    [InlineData("iPhone 18 Pro Max")]
    [InlineData("iPhone Air")]
    public void SupportedIphoneProfilesFromUpstreamGenerateDisplayModes(string name)
    {
        using var stream = File.OpenRead(
            Path.Combine(AppContext.BaseDirectory, "Assets", "phone-models.json"));
        var catalog = PhoneCatalog.Load(stream);

        var model = catalog.Find("Apple " + name);
        Assert.NotNull(model);

        var selected = catalog.Resolve(new PhoneChoice(model.Id, new PhoneScreen(1, 1)));
        Assert.Equal(model.Screen, selected.Screen);
        Assert.True(selected.Screen.IsValid);

        var fit = new ScreenFit(selected.Screen);
        Assert.True(fit.BaseWidth is >= ScreenFit.MinWidth and <= ScreenFit.MaxWidth);

        var modes = VddResolutionProfile.Create(selected);
        Assert.NotEmpty(modes);
        Assert.InRange(modes.Count, 1, 28);
        Assert.Equal(fit.BaseWidth * 2, modes[0].Width);
        Assert.Equal(2160, modes[^1].Width);
        Assert.All(modes, mode =>
        {
            Assert.True(mode.Width is >= 1080 and <= 2160);
            Assert.True(mode.Height is >= 1600 and <= 4320);
            Assert.Equal(60, mode.RefreshRate);
        });

        Assert.Single(catalog.Search("Apple " + name)
            .SelectMany(brand => brand.Models), item => item.Id == model.Id);
    }

    [Fact]
    public void Iphone13FamilySearchDoesNotRequireSpecificCasingOrSpaces()
    {
        using var stream = File.OpenRead(
            Path.Combine(AppContext.BaseDirectory, "Assets", "phone-models.json"));
        var catalog = PhoneCatalog.Load(stream);
        var results = catalog.Search("IPHONE13").SelectMany(brand => brand.Models).ToArray();

        Assert.Equal(4, results.Length);
        Assert.Contains(results, model => model.Name == "iPhone 13 mini");
        Assert.Contains(results, model => model.Name == "iPhone 13 Pro Max");
    }

    [Fact]
    public void CatalogRejectsInvalidAndDuplicateEntries()
    {
        const string invalid = """
        { "brands": [{ "name": "Bad", "models": [{ "name": "Landscape", "screen": [2400, 1080] }] }] }
        """;
        const string duplicate = """
        { "brands": [
          { "name": "Apple", "models": [{ "name": "iPhone", "screen": [1080, 2400] }] },
          { "name": "apple", "models": [{ "name": "Other", "screen": [1080, 2400] }] }
        ] }
        """;

        Assert.Throws<PhoneCatalogException>(() => PhoneCatalog.FromJson(invalid));
        Assert.Throws<PhoneCatalogException>(() => PhoneCatalog.FromJson(duplicate));
    }
}
