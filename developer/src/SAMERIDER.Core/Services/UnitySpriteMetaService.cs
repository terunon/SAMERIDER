using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using SkiaSharp;

namespace SAMERIDER.Core.Services;

/// <summary>Generates Unity TextureImporter metadata for a regular sprite sheet.</summary>
public static class UnitySpriteMetaService
{
    private const int MaximumTextureSide = 16_384;
    private const long MaximumTexturePixels = 64_000_000;
    private static readonly Regex GuidLine = new(@"(?m)^guid:\s*([0-9a-fA-F]{32})\s*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex RootSpriteIdLine = new(@"(?m)^    spriteID:\s*([0-9a-fA-F]{32})\s*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SpriteBlockLine = new(@"^    - serializedVersion: 2\s*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SpriteNameLine = new(@"(?m)^      name:\s*(.+?)\s*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SpriteIdLine = new(@"(?m)^      spriteID:\s*([0-9a-fA-F]{32})\s*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex InternalIdLine = new(@"(?m)^      internalID:\s*(-?\d+)\s*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string Generate(string pngFileName, int cellWidth, int cellHeight, int columns, int rows, string? existingMeta = null)
    {
        Validate(pngFileName, cellWidth, cellHeight, columns, rows);
        var stem = Path.GetFileNameWithoutExtension(pngFileName);
        var guid = ParseGuid(existingMeta);
        guid ??= Guid.NewGuid().ToString("N");
        var existingSprites = ParseSprites(existingMeta);
        var rootSpriteId = ParseRootSpriteId(existingMeta) ?? DeterministicHex(guid, "root", 16);
        var sprites = new List<SpriteIdentity>(checked(columns * rows));
        var usedInternalIds = new HashSet<long>();
        var usedSpriteIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var row = 0; row < rows; row++)
        for (var column = 0; column < columns; column++)
        {
            var index = row * columns + column;
            var name = $"{stem}_{index}";
            if (existingSprites.TryGetValue(name, out var previous) &&
                previous.InternalId != 0 && IsGuid(previous.SpriteId) &&
                usedInternalIds.Add(previous.InternalId) && usedSpriteIds.Add(previous.SpriteId))
            {
                sprites.Add(new SpriteIdentity(name, previous.InternalId, previous.SpriteId));
                continue;
            }

            var salt = 0;
            long internalId;
            do
            {
                var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{guid}:{name}:internal:{salt++}"));
                internalId = BinaryPrimitives.ReadInt64LittleEndian(hash) & long.MaxValue;
            } while (internalId == 0 || !usedInternalIds.Add(internalId));

            salt = 0;
            string spriteId;
            do
            {
                spriteId = DeterministicHex(guid, $"{name}:sprite:{salt++}", 16);
            } while (!usedSpriteIds.Add(spriteId));
            sprites.Add(new SpriteIdentity(name, internalId, spriteId));
        }

        var maxTextureSize = NextPowerOfTwo(Math.Max(2048, Math.Max(cellWidth * columns, cellHeight * rows)));
        return BuildYaml(guid, rootSpriteId, sprites, cellWidth, cellHeight, columns, rows, maxTextureSize);
    }

    public static void SavePngWithMeta(SKBitmap image, string pngPath, string metaPath, string pngFileName, int cellWidth, int cellHeight)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentException.ThrowIfNullOrWhiteSpace(pngPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(metaPath);
        if (cellWidth <= 0 || cellHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(cellWidth), "セルサイズは1以上で指定してください。");

        var fullPngPath = Path.GetFullPath(pngPath);
        var fullMetaPath = Path.GetFullPath(metaPath);
        var pathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (string.Equals(fullPngPath, fullMetaPath, pathComparison))
            throw new ArgumentException("PNGとUnityメタデータの出力先は別々に指定してください。", nameof(metaPath));
        if (image.Width % cellWidth != 0 || image.Height % cellHeight != 0)
            throw new ArgumentException("PNGサイズはセルサイズの整数倍にしてください。", nameof(image));

        var columns = image.Width / cellWidth;
        var rows = image.Height / cellHeight;
        var existingMetaBytes = File.Exists(fullMetaPath) ? File.ReadAllBytes(fullMetaPath) : null;
        string? existingMeta = null;
        if (existingMetaBytes is not null)
        {
            using var reader = new StreamReader(new MemoryStream(existingMetaBytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            existingMeta = reader.ReadToEnd();
        }
        var generatedMeta = Generate(pngFileName, cellWidth, cellHeight, columns, rows, existingMeta);
        using var pngData = SpriteImageService.EncodePng(image);

        var pngDirectory = Path.GetDirectoryName(fullPngPath)!;
        var metaDirectory = Path.GetDirectoryName(fullMetaPath)!;
        Directory.CreateDirectory(pngDirectory);
        Directory.CreateDirectory(metaDirectory);
        var token = Guid.NewGuid().ToString("N");
        var stagedPngPath = Path.Combine(pngDirectory, $".{Path.GetFileName(fullPngPath)}.{token}.tmp");
        var stagedMetaPath = Path.Combine(metaDirectory, $".{Path.GetFileName(fullMetaPath)}.{token}.tmp");
        var rollbackMetaPath = Path.Combine(metaDirectory, $".{Path.GetFileName(fullMetaPath)}.{token}.rollback.tmp");
        try
        {
            using (var stream = File.Create(stagedPngPath)) pngData.SaveTo(stream);
            File.WriteAllText(stagedMetaPath, generatedMeta, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(stagedMetaPath, fullMetaPath, overwrite: true);
            try
            {
                File.Move(stagedPngPath, fullPngPath, overwrite: true);
            }
            catch (Exception pngCommitException)
            {
                try
                {
                    if (existingMetaBytes is null)
                        File.Delete(fullMetaPath);
                    else
                    {
                        File.WriteAllBytes(rollbackMetaPath, existingMetaBytes);
                        File.Move(rollbackMetaPath, fullMetaPath, overwrite: true);
                    }
                }
                catch (Exception rollbackException)
                {
                    throw new AggregateException("PNGの配置に失敗し、元のUnityメタデータも復元できませんでした。", pngCommitException, rollbackException);
                }
                throw;
            }
        }
        finally
        {
            TryDelete(stagedPngPath);
            TryDelete(stagedMetaPath);
            TryDelete(rollbackMetaPath);
        }
    }

    private static void Validate(string pngFileName, int cellWidth, int cellHeight, int columns, int rows)
    {
        if (!ProjectValidator.IsSafePngOutputFileName(pngFileName) ||
            !string.Equals(Path.GetFileName(pngFileName), pngFileName, StringComparison.Ordinal) ||
            !string.Equals(Path.GetExtension(pngFileName), ".png", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("安全なPNGファイル名を指定してください。", nameof(pngFileName));
        if (cellWidth <= 0 || cellHeight <= 0 || columns <= 0 || rows <= 0)
            throw new ArgumentOutOfRangeException(nameof(cellWidth), "セルサイズと分割数は1以上で指定してください。");
        var cellCount = (long)columns * rows;
        var sheetWidth = (long)cellWidth * columns;
        var sheetHeight = (long)cellHeight * rows;
        if (cellCount > ProjectValidator.MaximumGridCellCount || sheetWidth > MaximumTextureSide || sheetHeight > MaximumTextureSide ||
            sheetWidth * sheetHeight > MaximumTexturePixels)
            throw new ArgumentOutOfRangeException(nameof(columns), "スプライトシートが上限を超えています。");
    }

    private static string? ParseGuid(string? existingMeta)
    {
        if (string.IsNullOrEmpty(existingMeta)) return null;
        var match = GuidLine.Match(existingMeta);
        if (!match.Success)
            throw new FormatException("既存Unityメタデータに有効なGUIDがありません。");
        return match.Groups[1].Value.ToLowerInvariant();
    }

    private static string? ParseRootSpriteId(string? existingMeta)
    {
        if (string.IsNullOrEmpty(existingMeta)) return null;
        var match = RootSpriteIdLine.Match(existingMeta);
        return match.Success ? match.Groups[1].Value.ToLowerInvariant() : null;
    }

    private static Dictionary<string, SpriteIdentity> ParseSprites(string? existingMeta)
    {
        var sprites = new Dictionary<string, SpriteIdentity>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(existingMeta)) return sprites;
        var lines = existingMeta.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var inSpriteList = false;
        StringBuilder? block = null;
        void AddBlock()
        {
            if (block is null) return;
            var text = block.ToString();
            var nameMatch = SpriteNameLine.Match(text);
            var spriteIdMatch = SpriteIdLine.Match(text);
            var internalIdMatch = InternalIdLine.Match(text);
            if (nameMatch.Success && spriteIdMatch.Success && internalIdMatch.Success &&
                long.TryParse(internalIdMatch.Groups[1].Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var internalId))
                sprites[UnquoteYaml(nameMatch.Groups[1].Value)] = new SpriteIdentity(
                    UnquoteYaml(nameMatch.Groups[1].Value), internalId, spriteIdMatch.Groups[1].Value.ToLowerInvariant());
            block = null;
        }

        foreach (var line in lines)
        {
            if (line == "    sprites:") { inSpriteList = true; continue; }
            if (!inSpriteList) continue;
            if (SpriteBlockLine.IsMatch(line))
            {
                AddBlock();
                block = new StringBuilder().AppendLine(line);
            }
            else if (block is not null && line.StartsWith("      ", StringComparison.Ordinal))
            {
                block.AppendLine(line);
            }
            else if (block is not null && line.StartsWith("    ", StringComparison.Ordinal))
            {
                AddBlock();
                inSpriteList = false;
            }
        }
        AddBlock();
        return sprites;
    }

    private static string UnquoteYaml(string value)
    {
        if (value.Length >= 2 && value[0] == '\'' && value[^1] == '\'')
            return value[1..^1].Replace("''", "'", StringComparison.Ordinal);
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            return value[1..^1].Replace("\\\"", "\"", StringComparison.Ordinal).Replace("\\\\", "\\", StringComparison.Ordinal);
        return value;
    }

    private static bool IsGuid(string value) => value.Length == 32 && value.All(Uri.IsHexDigit);

    private static string DeterministicHex(string guid, string name, int bytes) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{guid}:{name}")))[..(bytes * 2)].ToLowerInvariant();

    private static int NextPowerOfTwo(int value)
    {
        var result = 1;
        while (result < value) result <<= 1;
        return result;
    }

    private static string BuildYaml(string guid, string rootSpriteId, IReadOnlyList<SpriteIdentity> sprites,
        int cellWidth, int cellHeight, int columns, int rows, int maxTextureSize)
    {
        var yaml = new StringBuilder(2048 + sprites.Count * 520);
        yaml.AppendLine("fileFormatVersion: 2");
        yaml.Append("guid: ").AppendLine(guid);
        yaml.AppendLine("TextureImporter:");
        yaml.AppendLine("  internalIDToNameTable:");
        foreach (var sprite in sprites)
        {
            yaml.AppendLine("  - first:");
            yaml.Append("      213: ").AppendLine(sprite.InternalId.ToString(CultureInfo.InvariantCulture));
            yaml.Append("    second: ").AppendLine(QuoteYaml(sprite.Name));
        }
        yaml.AppendLine("  externalObjects: {}");
        yaml.AppendLine("  serializedVersion: 13");
        yaml.AppendLine("  mipmaps:");
        yaml.AppendLine("    mipMapMode: 0");
        yaml.AppendLine("    enableMipMap: 0");
        yaml.AppendLine("    sRGBTexture: 1");
        yaml.AppendLine("    linearTexture: 0");
        yaml.AppendLine("    fadeOut: 0");
        yaml.AppendLine("    borderMipMap: 0");
        yaml.AppendLine("    mipMapsPreserveCoverage: 0");
        yaml.AppendLine("    alphaTestReferenceValue: 0.5");
        yaml.AppendLine("    mipMapFadeDistanceStart: 1");
        yaml.AppendLine("    mipMapFadeDistanceEnd: 3");
        yaml.AppendLine("  bumpmap:");
        yaml.AppendLine("    convertToNormalMap: 0");
        yaml.AppendLine("    externalNormalMap: 0");
        yaml.AppendLine("    heightScale: 0.25");
        yaml.AppendLine("    normalMapFilter: 0");
        yaml.AppendLine("    flipGreenChannel: 0");
        yaml.AppendLine("  isReadable: 0");
        yaml.AppendLine("  streamingMipmaps: 0");
        yaml.AppendLine("  streamingMipmapsPriority: 0");
        yaml.AppendLine("  vTOnly: 0");
        yaml.AppendLine("  ignoreMipmapLimit: 0");
        yaml.AppendLine("  grayScaleToAlpha: 0");
        yaml.AppendLine("  generateCubemap: 6");
        yaml.AppendLine("  cubemapConvolution: 0");
        yaml.AppendLine("  seamlessCubemap: 0");
        yaml.AppendLine("  textureFormat: 1");
        yaml.Append("  maxTextureSize: ").AppendLine(maxTextureSize.ToString(CultureInfo.InvariantCulture));
        yaml.AppendLine("  textureSettings:");
        yaml.AppendLine("    serializedVersion: 2");
        yaml.AppendLine("    filterMode: 1");
        yaml.AppendLine("    aniso: 1");
        yaml.AppendLine("    mipBias: 0");
        yaml.AppendLine("    wrapU: 1");
        yaml.AppendLine("    wrapV: 1");
        yaml.AppendLine("    wrapW: 1");
        yaml.AppendLine("  nPOTScale: 0");
        yaml.AppendLine("  lightmap: 0");
        yaml.AppendLine("  compressionQuality: 50");
        yaml.AppendLine("  spriteMode: 2");
        yaml.AppendLine("  spriteExtrude: 1");
        yaml.AppendLine("  spriteMeshType: 0");
        yaml.AppendLine("  alignment: 0");
        yaml.AppendLine("  spritePivot: {x: 0.5, y: 0.5}");
        yaml.AppendLine("  spritePixelsToUnits: 100");
        yaml.AppendLine("  spriteBorder: {x: 0, y: 0, z: 0, w: 0}");
        yaml.AppendLine("  spriteGenerateFallbackPhysicsShape: 1");
        yaml.AppendLine("  alphaUsage: 1");
        yaml.AppendLine("  alphaIsTransparency: 1");
        yaml.AppendLine("  spriteTessellationMethod: 0");
        yaml.AppendLine("  spriteTessellationDetail: -1");
        yaml.AppendLine("  spriteGeometrySubdivision: -1");
        yaml.AppendLine("  textureType: 8");
        yaml.AppendLine("  textureShape: 1");
        yaml.AppendLine("  singleChannelComponent: 0");
        yaml.AppendLine("  flipbookRows: 1");
        yaml.AppendLine("  flipbookColumns: 1");
        yaml.AppendLine("  maxTextureSizeSet: 0");
        yaml.AppendLine("  compressionQualitySet: 0");
        yaml.AppendLine("  textureFormatSet: 0");
        yaml.AppendLine("  ignorePngGamma: 0");
        yaml.AppendLine("  applyGammaDecoding: 0");
        yaml.AppendLine("  swizzle: 50462976");
        yaml.AppendLine("  cookieLightType: 0");
        yaml.AppendLine("  platformSettings:");
        AppendPlatformSettings(yaml, "DefaultTexturePlatform", maxTextureSize, false);
        AppendPlatformSettings(yaml, "Standalone", maxTextureSize, true);
        yaml.AppendLine("  spriteSheet:");
        yaml.AppendLine("    serializedVersion: 2");
        yaml.AppendLine("    sprites:");
        for (var index = 0; index < sprites.Count; index++)
        {
            var sprite = sprites[index];
            var row = index / columns;
            var column = index % columns;
            yaml.AppendLine("    - serializedVersion: 2");
            yaml.Append("      name: ").AppendLine(QuoteYaml(sprite.Name));
            yaml.AppendLine("      rect:");
            yaml.AppendLine("        serializedVersion: 2");
            yaml.Append("        x: ").AppendLine(((long)column * cellWidth).ToString(CultureInfo.InvariantCulture));
            yaml.Append("        y: ").AppendLine(((long)(rows - 1 - row) * cellHeight).ToString(CultureInfo.InvariantCulture));
            yaml.Append("        width: ").AppendLine(cellWidth.ToString(CultureInfo.InvariantCulture));
            yaml.Append("        height: ").AppendLine(cellHeight.ToString(CultureInfo.InvariantCulture));
            yaml.AppendLine("      alignment: 0");
            yaml.AppendLine("      pivot: {x: 0.5, y: 0.5}");
            yaml.AppendLine("      border: {x: 0, y: 0, z: 0, w: 0}");
            yaml.AppendLine("      customData: ");
            yaml.AppendLine("      outline: []");
            yaml.AppendLine("      physicsShape: []");
            yaml.AppendLine("      tessellationDetail: -1");
            yaml.AppendLine("      bones: []");
            yaml.Append("      spriteID: ").AppendLine(sprite.SpriteId);
            yaml.Append("      internalID: ").AppendLine(sprite.InternalId.ToString(CultureInfo.InvariantCulture));
            yaml.AppendLine("      vertices: []");
            yaml.AppendLine("      indices: ");
            yaml.AppendLine("      edges: []");
            yaml.AppendLine("      weights: []");
        }
        yaml.AppendLine("    outline: []");
        yaml.AppendLine("    customData: ");
        yaml.AppendLine("    physicsShape: []");
        yaml.AppendLine("    bones: []");
        yaml.Append("    spriteID: ").AppendLine(rootSpriteId);
        yaml.AppendLine("    internalID: 0");
        yaml.AppendLine("    vertices: []");
        yaml.AppendLine("    indices: ");
        yaml.AppendLine("    edges: []");
        yaml.AppendLine("    weights: []");
        yaml.AppendLine("    secondaryTextures: []");
        yaml.AppendLine("    spriteCustomMetadata:");
        yaml.AppendLine("      entries:");
        yaml.AppendLine("      - key: SpriteEditor.SliceSettings");
        yaml.Append("        value: ").AppendLine(QuoteYaml($"{{\"sliceOnImport\":false,\"gridCellCount\":{{\"x\":{columns}.0,\"y\":{rows}.0}},\"gridSpriteSize\":{{\"x\":{cellWidth}.0,\"y\":{cellHeight}.0}},\"gridSpriteOffset\":{{\"x\":0.0,\"y\":0.0}},\"gridSpritePadding\":{{\"x\":0.0,\"y\":0.0}},\"pivot\":{{\"x\":0.5,\"y\":0.5}},\"pivotPixels\":{{\"x\":0.0,\"y\":0.0}},\"autoSlicingMethod\":0,\"spriteAlignment\":0,\"pivotUnitMode\":0,\"slicingType\":2,\"keepEmptyRects\":false,\"isAlternate\":false}}"));
        yaml.AppendLine("    nameFileIdTable:");
        foreach (var sprite in sprites)
            yaml.Append("      ").Append(QuoteYaml(sprite.Name)).Append(": ").AppendLine(sprite.InternalId.ToString(CultureInfo.InvariantCulture));
        yaml.AppendLine("  mipmapLimitGroupName: ");
        yaml.AppendLine("  pSDRemoveMatte: 0");
        yaml.AppendLine("  userData: ");
        yaml.AppendLine("  assetBundleName: ");
        yaml.AppendLine("  assetBundleVariant: ");
        return yaml.ToString();
    }

    private static void AppendPlatformSettings(StringBuilder yaml, string buildTarget, int maxTextureSize, bool standalone)
    {
        yaml.AppendLine("  - serializedVersion: 4");
        yaml.Append("    buildTarget: ").AppendLine(buildTarget);
        yaml.Append("    maxTextureSize: ").AppendLine(maxTextureSize.ToString(CultureInfo.InvariantCulture));
        yaml.AppendLine("    resizeAlgorithm: 0");
        yaml.AppendLine("    textureFormat: -1");
        yaml.AppendLine("    textureCompression: 1");
        yaml.AppendLine("    compressionQuality: 50");
        yaml.Append("    crunchedCompression: ").AppendLine(standalone ? "0" : "1");
        yaml.AppendLine("    allowsAlphaSplitting: 0");
        yaml.AppendLine("    overridden: 0");
        yaml.AppendLine("    ignorePlatformSupport: 0");
        yaml.AppendLine("    androidETC2FallbackOverride: 0");
        yaml.AppendLine("    forceMaximumCompressionQuality_BC6H_BC7: 0");
    }

    private static string QuoteYaml(string value) => $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }


    private readonly record struct SpriteIdentity(string Name, long InternalId, string SpriteId);
}
