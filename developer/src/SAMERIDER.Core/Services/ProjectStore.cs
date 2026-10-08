using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.Concurrent;
using SAMERIDER.Core.Models;

namespace SAMERIDER.Core.Services;

public static class ProjectStore
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SaveLocks = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };
    private static readonly ProjectJsonSerializerContext JsonContext = new(JsonOptions);

    public static async Task SaveAsync(SpriteProject project, string jsonPath, CancellationToken cancellationToken = default)
    {
        var fileName = Path.GetFileName(jsonPath);
        if (!ProjectValidator.IsSafeFileName(fileName))
            throw new InvalidDataException("ファイル名には半角文字を使用してください。ファイル名に使えない記号や全角文字は使用できません。");
        if (!string.Equals(Path.GetExtension(fileName), ".json", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("プロジェクトファイルの拡張子は .json にしてください。");
        var directory = Path.GetDirectoryName(Path.GetFullPath(jsonPath))!;
        Directory.CreateDirectory(directory);
        var fullPath = Path.GetFullPath(jsonPath);
        var saveLock = SaveLocks.GetOrAdd(fullPath, static _ => new SemaphoreSlim(1, 1));
        await saveLock.WaitAsync(cancellationToken);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(jsonPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            ProjectValidator.Validate(project, directory);
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                await stream.WriteAsync(SerializeToUtf8Bytes(project), cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            // Close the exclusive temp-file handle before replacing the destination.
            // Keeping it alive through File.Move causes a sharing violation on Windows.
            File.Move(temporaryPath, jsonPath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            saveLock.Release();
        }
    }

    public static async Task<SpriteProject> LoadAsync(string jsonPath, CancellationToken cancellationToken = default)
    {
        if (!ProjectValidator.IsSafeFileName(Path.GetFileName(jsonPath)))
            throw new InvalidDataException("ファイル名には半角文字を使用してください。ファイル名に使えない記号や全角文字は使用できません。");
        if (!string.Equals(Path.GetExtension(jsonPath), ".json", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("プロジェクトファイルの拡張子は .json にしてください。");
        var json = await File.ReadAllBytesAsync(jsonPath, cancellationToken);
        var project = Deserialize(json);
        ProjectValidator.Validate(project, Path.GetDirectoryName(Path.GetFullPath(jsonPath)));
        return project;
    }

    public static byte[] SerializeToUtf8Bytes(SpriteProject project)
    {
        ProjectValidator.Validate(project);
        return JsonSerializer.SerializeToUtf8Bytes(project, JsonContext.SpriteProject);
    }

    public static SpriteProject Deserialize(ReadOnlyMemory<byte> json)
    {
        using var document = JsonDocument.Parse(json);
        RequireProperties(document.RootElement, "formatVersion", "title", "importFolderName", "cellWidth", "cellHeight", "columns", "rows", "assets", "cells");
        foreach (var asset in document.RootElement.GetProperty("assets").EnumerateArray())
            RequireProperties(asset, "relativePath", "width", "height");
        foreach (var cell in document.RootElement.GetProperty("cells").EnumerateArray())
        {
            RequireProperties(cell, "x", "y", "assetPath", "sourceRect", "offsetX", "offsetY", "displayName");
            RequireProperties(cell.GetProperty("sourceRect"), "x", "y", "width", "height");
        }
        var project = document.RootElement.Deserialize(JsonContext.SpriteProject)
            ?? throw new InvalidDataException("プロジェクトファイルが空です。");
        ProjectValidator.Validate(project);
        return project;
    }

    private static void RequireProperties(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("プロジェクトファイルのデータ形式が正しくありません。");
        foreach (var name in names)
            if (!element.TryGetProperty(name, out _))
                throw new InvalidDataException($"プロジェクトファイルに必須項目「{name}」がありません。");
    }
}

[JsonSerializable(typeof(SpriteProject))]
internal partial class ProjectJsonSerializerContext : JsonSerializerContext
{
}
