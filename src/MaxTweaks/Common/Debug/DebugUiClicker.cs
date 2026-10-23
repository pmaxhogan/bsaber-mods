using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MaxTweaks.Core.Debugging;
using UnityEngine;
using UnityEngine.EventSystems;

namespace MaxTweaks.Common.Debugging;

/// <summary>
/// UI inspection and clicking for the debug channel (find-*, click-*, table, search, textinfo,
/// patches). FPFC mouse aiming in menus is unreliable (the pointer is offset from the screen centre
/// and the window can lose focus), so these act on the UI objects directly: a click sends
/// pointer down/up/click events to the nearest click handler, like a real pointer would.
/// Only reachable through DebugCommandService, which exists only with --debug.
/// </summary>
internal static class DebugUiClicker
{
    private const int MaxListed = 20;

    public static string Find(UiQueryRequest request)
    {
        var hits = Collect(request.Kind, request.Needle);
        return hits.Count == 0 ? "no match" : $"{hits.Count} match(es): " + List(hits);
    }

    public static string Click(UiQueryRequest request)
    {
        var hits = Collect(request.Kind, request.Needle);
        if (hits.Count == 0)
        {
            throw new DebugCommandException($"no {request.Kind.ToString().ToLowerInvariant()} matches '{request.Needle}'");
        }

        if (request.Index >= hits.Count)
        {
            throw new DebugCommandException($"index {request.Index} out of range; {hits.Count} match(es): " + List(hits));
        }

        var go = hits[request.Index].Go;
        var handler = ExecuteEvents.GetEventHandler<IPointerClickHandler>(go);

        // Labels often sit beside (not under) the clickable: look for a handler in the parents' subtrees.
        for (var up = go.transform.parent; handler == null && up != null && up != go.transform.root; up = up.parent)
        {
            var mb = up.GetComponentsInChildren<MonoBehaviour>().FirstOrDefault(m => m is IPointerClickHandler && m.isActiveAndEnabled);
            handler = mb != null ? mb.gameObject : null;
        }

        if (handler == null)
        {
            throw new DebugCommandException("no click handler near " + Describe(go));
        }

        var data = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
        ExecuteEvents.Execute(handler, data, ExecuteEvents.pointerDownHandler);
        ExecuteEvents.Execute(handler, data, ExecuteEvents.pointerUpHandler);
        ExecuteEvents.Execute(handler, data, ExecuteEvents.pointerClickHandler);
        return $"clicked {Describe(handler)} via '{hits[request.Index].Label}' ({hits.Count} match(es))";
    }

    /// <summary>Lists the Harmony patches on every overload of Type.Method (who patched what).</summary>
    public static string Patches(string typeName, string methodName)
    {
        var type = HarmonyLib.AccessTools.TypeByName(typeName) ?? throw new DebugCommandException("no type " + typeName);
        var parts = new List<string>();
        foreach (var method in HarmonyLib.AccessTools.GetDeclaredMethods(type).Where(m => m.Name == methodName))
        {
            var info = HarmonyLib.Harmony.GetPatchInfo(method);
            if (info == null)
            {
                parts.Add(method + ": unpatched");
                continue;
            }

            parts.Add(method + ": prefixes=" + string.Join(",", info.Prefixes.Select(p => p.owner + "/" + p.priority + "/" + p.PatchMethod.DeclaringType?.Name))
                + " postfixes=" + string.Join(",", info.Postfixes.Select(p => p.owner + "/" + p.priority + "/" + p.PatchMethod.DeclaringType?.Name))
                + " transpilers=" + string.Join(",", info.Transpilers.Select(p => p.owner)));
        }

        return parts.Count == 0 ? $"{type.FullName} has no method {methodName}" : string.Join(" || ", parts);
    }

    /// <summary>Rendering details of active labels containing the text (why is it invisible?).</summary>
    public static string TextInfo(string needle)
    {
        var parts = new List<string>();
        foreach (var t in Resources.FindObjectsOfTypeAll<TMPro.TMP_Text>())
        {
            if (t == null || !t.gameObject.activeInHierarchy || (t.text ?? string.Empty).IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            if (parts.Count >= MaxListed)
            {
                parts.Add("...");
                break;
            }

            var rect = (RectTransform)t.transform;
            var canvas = t.canvas;
            var material = t.fontSharedMaterial;
            parts.Add($"'{t.text}' type={t.GetType().Name} enabled={t.enabled} color={t.color} alpha={t.alpha} size={t.fontSize}"
                + $" rect={rect.rect.size} font={(t.font != null ? t.font.name : "null")}"
                + $" mat={(material != null ? material.name + "/" + material.shader.name : "null")}"
                + $" pos={t.transform.position} lossy={t.transform.lossyScale} layer={t.gameObject.layer}"
                + $" canvas={(canvas != null ? canvas.name + "/" + canvas.renderMode + "/" + canvas.sortingOrder : "null")}"
                + $" chars={t.textInfo?.characterCount} crAlpha={t.canvasRenderer?.GetAlpha()} cull={t.canvasRenderer?.cull}");
        }

        var cam = Camera.main;
        if (cam != null)
        {
            parts.Add($"camera {cam.name} pos={cam.transform.position} fwd={cam.transform.forward} mask={cam.cullingMask}");
        }

        return parts.Count == 0 ? "no match" : string.Join(" | ", parts);
    }

    /// <summary>Row counts of the active song list (total, official = non-custom), for Hide OSTs.</summary>
    public static string Table()
    {
        var field = typeof(LevelCollectionTableView).GetField("_beatmapLevels", BindingFlags.NonPublic | BindingFlags.Instance);
        var parts = new List<string>();
        foreach (var table in Resources.FindObjectsOfTypeAll<LevelCollectionTableView>())
        {
            if (table == null || !table.gameObject.activeInHierarchy)
            {
                continue;
            }

            var all = (field?.GetValue(table) as IEnumerable)?.Cast<BeatmapLevel>().ToList() ?? new List<BeatmapLevel>();
            var official = all.Where(l => !l.levelID.StartsWith("custom_level_", StringComparison.Ordinal)).ToList();
            parts.Add($"table rows={all.Count} official={official.Count} firstOfficial=[{string.Join(",", official.Take(5).Select(l => l.levelID))}] first=[{string.Join(",", all.Take(3).Select(l => l.songName))}]");
        }

        return parts.Count == 0 ? "no active table" : string.Join(" | ", parts);
    }

    /// <summary>Types into every active input field (the song search) and fires its value-changed events.</summary>
    public static string Search(string text)
    {
        const BindingFlags Fields = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        var done = new List<string>();
        foreach (var input in Resources.FindObjectsOfTypeAll<HMUI.InputFieldView>())
        {
            if (input == null || !input.gameObject.activeInHierarchy)
            {
                continue;
            }

            input.SetText(text);
            var fired = new List<string>();
            for (var type = input.GetType(); type != null; type = type.BaseType)
            {
                foreach (var field in type.GetFields(Fields))
                {
                    if (field.Name.IndexOf("ValueChanged", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    var value = field.GetValue(input);
                    if (value is Delegate d)
                    {
                        d.DynamicInvoke(input);
                        fired.Add(field.Name);
                    }
                    else if (value != null)
                    {
                        var invoke = value.GetType().GetMethods().FirstOrDefault(m => m.Name == "Invoke" && m.GetParameters().Length == 1);
                        if (invoke != null)
                        {
                            invoke.Invoke(value, new object[] { invoke.GetParameters()[0].ParameterType == typeof(string) ? text : input });
                            fired.Add(field.Name + "(" + value.GetType().Name + ")");
                        }
                    }
                }
            }

            done.Add(input.name + " fired=" + string.Join("/", fired));
        }

        return done.Count == 0 ? "no active input field" : $"set '{text}' on " + string.Join(",", done);
    }

    private static List<(GameObject Go, string Label)> Collect(UiMatchKind kind, string needle)
    {
        bool Matches(string? s) => (s ?? string.Empty).IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        var result = new List<(GameObject, string)>();
        switch (kind)
        {
            case UiMatchKind.Hint:
                foreach (var h in Resources.FindObjectsOfTypeAll<HMUI.HoverHint>())
                {
                    if (h != null && h.gameObject.activeInHierarchy && Matches(h.text))
                    {
                        result.Add((h.gameObject, h.text ?? string.Empty));
                    }
                }

                break;
            case UiMatchKind.Name:
                foreach (var tr in Resources.FindObjectsOfTypeAll<RectTransform>())
                {
                    if (tr != null && tr.gameObject.activeInHierarchy && Matches(tr.name))
                    {
                        result.Add((tr.gameObject, tr.name));
                    }
                }

                break;
            default:
                foreach (var t in Resources.FindObjectsOfTypeAll<TMPro.TMP_Text>())
                {
                    if (t != null && t.gameObject.activeInHierarchy && Matches(t.text))
                    {
                        result.Add((t.gameObject, t.text ?? string.Empty));
                    }
                }

                break;
        }

        return result;
    }

    private static string List(List<(GameObject Go, string Label)> hits) =>
        string.Join(" | ", hits.Take(MaxListed).Select((h, i) => $"[{i}] {Describe(h.Go)} '{h.Label}'"))
        + (hits.Count > MaxListed ? $" | ... {hits.Count - MaxListed} more" : string.Empty);

    private static string Describe(GameObject go)
    {
        var names = new List<string>();
        var t = go.transform;
        for (var i = 0; i < 4 && t != null; i++, t = t.parent)
        {
            names.Add(t.name);
        }

        return string.Join("<", names);
    }
}
