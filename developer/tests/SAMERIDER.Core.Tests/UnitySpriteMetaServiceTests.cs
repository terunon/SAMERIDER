using System.Text.RegularExpressions;
using SAMERIDER.Core.Services;
using SkiaSharp;

namespace SAMERIDER.Core.Tests;

public sealed class UnitySpriteMetaServiceTests
{
    [Fact]
    public void GeneratesUnityTextureImporterWithEveryGridCellInRowMajorOrder()
    {
        var meta = UnitySpriteMetaService.Generate("SkillFire.png", 10, 20, 4, 3);

        Assert.Matches("(?m)^  serializedVersion: 13$", meta);
        Assert.Matches("(?m)^  spriteMode: 2$", meta);
        Assert.Matches("(?m)^  spriteMeshType: 0$", meta);
        Assert.Matches("(?m)^  textureType: 8$", meta);
        Assert.Matches("(?m)^  nPOTScale: 0$", meta);
        var entries = Regex.Matches(meta, "(?ms)^    - serializedVersion: 2\\n      name: '([^']+)'\\n      rect:\\n        serializedVersion: 2\\n        x: (\\d+)\\n        y: (\\d+)\\n        width: (\\d+)\\n        height: (\\d+)");
        Assert.Equal(12, entries.Count);
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            var row = index / 4;
            var column = index % 4;
            Assert.Equal($"SkillFire_{index}", entry.Groups[1].Value);
            Assert.Equal(column * 10, int.Parse(entry.Groups[2].Value));
            Assert.Equal((2 - row) * 20, int.Parse(entry.Groups[3].Value));
            Assert.Equal(10, int.Parse(entry.Groups[4].Value));
            Assert.Equal(20, int.Parse(entry.Groups[5].Value));
        }
        Assert.Equal(12, Regex.Matches(meta, "(?m)^      internalID: -?\\d+$").Count);
        Assert.Equal(12, Regex.Matches(meta, "(?m)^      spriteID: [0-9a-f]{32}$").Count);
        Assert.Equal(12, Regex.Matches(meta, "(?m)^      'SkillFire_\\d+': -?\\d+$").Count);
        Assert.Contains("\"gridCellCount\":{\"x\":4.0,\"y\":3.0}", meta);
        Assert.Contains("\"gridSpriteSize\":{\"x\":10.0,\"y\":20.0}", meta);
    }

    [Fact]
    public void RaisesTextureSizeLimitToAvoidRescalingLargeValidSheets()
    {
        var meta = UnitySpriteMetaService.Generate("large.png", 2048, 2048, 2, 1);
        Assert.Contains("  maxTextureSize: 4096", meta);
        Assert.Contains("        width: 2048", meta);
    }

    [Fact]
    public void FreshExportsUseDifferentRootGuids()
    {
        var first = UnitySpriteMetaService.Generate("actor.png", 8, 8, 1, 1);
        var second = UnitySpriteMetaService.Generate("actor.png", 8, 8, 1, 1);

        Assert.NotEqual(GuidFrom(first), GuidFrom(second));
    }

    [Fact]
    public void ExistingMetaPreservesGuidAndActiveSpriteIdsByNameOnRegeneration()
    {
        var original = UnitySpriteMetaService.Generate("actor.png", 8, 8, 2, 1);
        var regenerated = UnitySpriteMetaService.Generate("actor.png", 16, 16, 2, 1, original);

        Assert.Equal(GuidFrom(original), GuidFrom(regenerated));
        Assert.Equal(SpriteIds(original), SpriteIds(regenerated));
        Assert.Equal(InternalIds(original), InternalIds(regenerated));
        Assert.Contains("        width: 16", regenerated);
        Assert.Contains("        height: 16", regenerated);
    }

    [Fact]
    public void NewGenerationDoesNotCopyStaleInternalNameEntries()
    {
        const string existing = """
            guid: 00112233445566778899aabbccddeeff
            TextureImporter:
              internalIDToNameTable:
              - first:
                  213: 123456789
                second: Stale_99
              spriteSheet:
                sprites:
                - serializedVersion: 2
                  name: Active_0
                  spriteID: aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa
                  internalID: 987654321
                outline: []
                spriteID: bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb
            """;

        var generated = UnitySpriteMetaService.Generate("Fresh.png", 8, 8, 2, 1, existing);

        Assert.Contains("second: 'Fresh_0'", generated);
        Assert.Contains("second: 'Fresh_1'", generated);
        Assert.DoesNotContain("Stale_99", generated);
        Assert.DoesNotContain("123456789", generated);
    }

    [Fact]
    public void YAMLQuotesSpriteNamesContainingApostrophe()
    {
        var meta = UnitySpriteMetaService.Generate("hero's.png", 8, 8, 1, 1);

        Assert.Contains("name: 'hero''s_0'", meta);
        Assert.Contains("second: 'hero''s_0'", meta);
        Assert.Contains("'hero''s_0':", meta);
    }

    [Theory]
    [InlineData("../sprite.png", 8, 8, 1, 1)]
    [InlineData("sprite.jpg", 8, 8, 1, 1)]
    [InlineData("bad\"name.png", 8, 8, 1, 1)]
    [InlineData("sprite.png", 0, 8, 1, 1)]
    [InlineData("sprite.png", 8, 0, 1, 1)]
    [InlineData("sprite.png", 8, 8, 0, 1)]
    [InlineData("sprite.png", 8, 8, 1, 0)]
    [InlineData("sprite.png", 8192, 8192, 2, 1)]
    [InlineData("sprite.png", 1, 1, 101, 100)]
    public void RejectsUnsafeNamesAndInvalidOrOversizedDimensions(string fileName, int cellWidth, int cellHeight, int columns, int rows)
    {
        Assert.ThrowsAny<ArgumentException>(() => UnitySpriteMetaService.Generate(fileName, cellWidth, cellHeight, columns, rows));
    }

    [Fact]
    public void RejectsExistingMetadataWithoutAValidGuid()
    {
        Assert.Throws<FormatException>(() => UnitySpriteMetaService.Generate("sprite.png", 8, 8, 1, 1, "guid: invalid"));
    }

    [Fact]
    public void SavesPngAndMatchingMetaAndPreservesGuidOnRepeat()
    {
        var directory = CreateTempDirectory();
        try
        {
            var pngPath = Path.Combine(directory, "sheet.png");
            var metaPath = pngPath + ".meta";
            using var image = new SKBitmap(40, 20, SKColorType.Bgra8888, SKAlphaType.Premul);
            image.Erase(SKColors.Transparent);

            UnitySpriteMetaService.SavePngWithMeta(image, pngPath, metaPath, "sheet.png", 10, 10);
            var firstMeta = File.ReadAllText(metaPath);
            var firstGuid = GuidFrom(firstMeta);
            using (var decoded = SKBitmap.Decode(pngPath))
                Assert.Equal((40, 20), (decoded!.Width, decoded.Height));

            UnitySpriteMetaService.SavePngWithMeta(image, pngPath, metaPath, "sheet.png", 10, 10);

            var secondMeta = File.ReadAllText(metaPath);
            Assert.Equal(firstGuid, GuidFrom(secondMeta));
            Assert.Equal(SpriteIds(firstMeta), SpriteIds(secondMeta));
            Assert.Equal(8, SpriteIds(secondMeta).Length);
            Assert.Contains("\"gridCellCount\":{\"x\":4.0,\"y\":2.0}", secondMeta);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void RejectsNonMultipleDimensionsBeforeReplacingExistingPng()
    {
        var directory = CreateTempDirectory();
        try
        {
            var pngPath = Path.Combine(directory, "sheet.png");
            var metaPath = pngPath + ".meta";
            byte[] existingPng = [1, 2, 3, 4];
            var existingMeta = "existing meta text";
            File.WriteAllBytes(pngPath, existingPng);
            File.WriteAllText(metaPath, existingMeta);
            using var image = new SKBitmap(9, 8);

            Assert.Throws<ArgumentException>(() => UnitySpriteMetaService.SavePngWithMeta(image, pngPath, metaPath, "sheet.png", 4, 4));

            Assert.Equal(existingPng, File.ReadAllBytes(pngPath));
            Assert.Equal(existingMeta, File.ReadAllText(metaPath));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void RollsBackMetadataIfPngDestinationCannotBeReplaced()
    {
        var directory = CreateTempDirectory();
        try
        {
            var pngPath = Path.Combine(directory, "sheet.png");
            var metaPath = pngPath + ".meta";
            Directory.CreateDirectory(pngPath);
            var oldMeta = UnitySpriteMetaService.Generate("sheet.png", 4, 4, 1, 1);
            File.WriteAllText(metaPath, oldMeta);
            using var image = new SKBitmap(8, 4, SKColorType.Bgra8888, SKAlphaType.Premul);

            Assert.Throws<IOException>(() => UnitySpriteMetaService.SavePngWithMeta(image, pngPath, metaPath, "sheet.png", 4, 4));

            Assert.Equal(oldMeta, File.ReadAllText(metaPath));
            Assert.True(Directory.Exists(pngPath));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static string GuidFrom(string meta) => Regex.Match(meta, "(?m)^guid: ([0-9a-f]{32})$").Groups[1].Value;

    private static string[] SpriteIds(string meta) => Regex.Matches(meta, "(?m)^      spriteID: ([0-9a-f]{32})$")
        .Select(match => match.Groups[1].Value).ToArray();

    private static string[] InternalIds(string meta) => Regex.Matches(meta, "(?m)^      internalID: (-?\\d+)$")
        .Select(match => match.Groups[1].Value).ToArray();

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"UnitySpriteMetaServiceTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
