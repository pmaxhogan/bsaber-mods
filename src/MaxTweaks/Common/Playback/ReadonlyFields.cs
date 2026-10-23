using System;
using System.Collections.Generic;
using System.Reflection;

namespace MaxTweaks.Common.Playback;

/// <summary>
/// Reflection access to (possibly readonly) instance fields by name, searched up the type hierarchy
/// from the object's runtime type. The publicized game assemblies lift access but not initonly, so
/// readonly fields (BeatmapCallbacksController._startFilterTime, BeatmapObjectSpawnController.InitData
/// fields, GameplayCoreSceneSetupData.beatmapKey, ...) are written through FieldInfo.SetValue, which
/// Mono allows for instance fields. FieldInfos are cached; not for per-frame use on hot paths.
/// </summary>
internal static class ReadonlyFields
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    private static readonly Dictionary<(Type, string), FieldInfo?> Cache = new();

    public static FieldInfo? Find(Type type, string name)
    {
        var key = (type, name);
        if (Cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        FieldInfo? found = null;
        for (var t = type; t != null && found == null; t = t.BaseType)
        {
            found = t.GetField(name, Flags);
        }

        Cache[key] = found;
        return found;
    }

    /// <summary>Sets field <paramref name="name"/> on <paramref name="target"/>; throws if it does not exist.</summary>
    public static void Set(object target, string name, object? value)
    {
        var field = Find(target.GetType(), name) ?? throw new MissingFieldException(target.GetType().FullName, name);
        field.SetValue(target, value);
    }

    /// <summary>Reads field <paramref name="name"/> (default when missing or of another type).</summary>
    public static T? Get<T>(object target, string name)
    {
        var field = Find(target.GetType(), name);
        return field != null && field.GetValue(target) is T value ? value : default;
    }

    /// <summary>
    /// Sets an auto-property's value through its backing field ("&lt;name&gt;k__BackingField"), falling
    /// back to a field called <paramref name="name"/>.
    /// </summary>
    public static void SetAutoProperty(object target, string name, object? value)
    {
        var field = Find(target.GetType(), $"<{name}>k__BackingField") ?? Find(target.GetType(), name)
            ?? throw new MissingFieldException(target.GetType().FullName, name);
        field.SetValue(target, value);
    }
}
