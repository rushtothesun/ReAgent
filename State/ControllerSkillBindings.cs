using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ExileCore2;
using ExileCore2.PoEMemory;
using ExileCore2.Shared.Nodes;

namespace ReAgent.State;

[Api]
public class ControllerSkillBindings
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(1);
    private static DateTime _lastRefresh = DateTime.MinValue;
    private static IReadOnlyList<ControllerBindingCell> _primaryBindings = [];
    private static IReadOnlyList<ControllerBindingCell> _secondaryBindings = [];
    private static int? _cachedHudRootIndex;

    private readonly GameController _controller;

    public ControllerSkillBindings(GameController controller)
    {
        _controller = controller;
    }

    [Api]
    public HotkeyNodeV2.ControllerKey PrimaryKeyForTexture(string textureName) =>
        FindKey(GetBindings().Primary, textureName);

    [Api]
    public HotkeyNodeV2.ControllerKey SecondaryKeyForTexture(string textureName) =>
        FindKey(GetBindings().Secondary, textureName);

    [Api]
    public bool HasPrimaryTexture(string textureName) => PrimaryKeyForTexture(textureName) != HotkeyNodeV2.ControllerKey.None;

    [Api]
    public bool HasSecondaryTexture(string textureName) => SecondaryKeyForTexture(textureName) != HotkeyNodeV2.ControllerKey.None;

    internal IReadOnlyList<ControllerBindingCell> PrimaryCells => GetBindings().Primary;

    internal IReadOnlyList<ControllerBindingCell> SecondaryCells => GetBindings().Secondary;

    private static HotkeyNodeV2.ControllerKey FindKey(IReadOnlyList<ControllerBindingCell> bindings, string textureName)
    {
        if (string.IsNullOrWhiteSpace(textureName))
        {
            return HotkeyNodeV2.ControllerKey.None;
        }

        foreach (var binding in bindings)
        {
            if (string.IsNullOrWhiteSpace(binding.Texture))
            {
                continue;
            }

            if (IsTextureMatch(binding.Texture, textureName))
            {
                return binding.Key;
            }
        }

        return HotkeyNodeV2.ControllerKey.None;
    }

    private (IReadOnlyList<ControllerBindingCell> Primary, IReadOnlyList<ControllerBindingCell> Secondary) GetBindings()
    {
        if (DateTime.UtcNow - _lastRefresh > CacheDuration)
        {
            _primaryBindings = ReadHudBindings(primary: true);
            _secondaryBindings = ReadHudBindings(primary: false);
            _lastRefresh = DateTime.UtcNow;
        }

        return (_primaryBindings, _secondaryBindings);
    }

    private Element GetHudSkillBar()
    {
        var ui = _controller?.IngameState?.IngameUi;
        if (ui == null || !ui.IsValid)
        {
            return null;
        }

        if (_cachedHudRootIndex is { } cachedIndex)
        {
            var cachedRoot = GetChild(ui, cachedIndex, 5, 0, 0);
            if (IsHudSkillBar(cachedRoot))
            {
                return cachedRoot;
            }
        }

        for (var rootIndex = 0; rootIndex < ui.ChildCount; rootIndex++)
        {
            var candidate = GetChild(ui, rootIndex, 5, 0, 0);
            if (!IsHudSkillBar(candidate))
            {
                continue;
            }

            _cachedHudRootIndex = rootIndex;
            return candidate;
        }

        return null;
    }

    private IReadOnlyList<ControllerBindingCell> ReadHudBindings(bool primary)
    {
        var bindings = new List<ControllerBindingCell>();
        var children = GetHudSkillBar()?.Children;
        if (children == null)
        {
            return bindings;
        }

        var barIndex = primary ? 0 : 1;
        for (var cellIndex = 1; cellIndex <= 12 && cellIndex < children.Count; cellIndex++)
        {
            var key = CellIndexToControllerKey(cellIndex);
            if (key == HotkeyNodeV2.ControllerKey.None)
            {
                continue;
            }

            var barCell = GetChild(children[cellIndex], barIndex);
            var addedTexture = false;
            var cellTextures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var texture in FindTextures(barCell).Where(IsBindingTexture))
            {
                if (!cellTextures.Add(texture))
                {
                    continue;
                }

                bindings.Add(new ControllerBindingCell(cellIndex, key, texture));
                addedTexture = true;
            }

            if (!addedTexture)
            {
                bindings.Add(new ControllerBindingCell(cellIndex, key, string.Empty));
            }
        }

        return bindings;
    }

    private static IEnumerable<string> FindTextures(Element element)
    {
        if (element == null || !element.IsValid)
        {
            yield break;
        }

        if (!string.IsNullOrWhiteSpace(element.TextureName))
        {
            yield return element.TextureName;
        }

        var children = element.Children;
        if (children == null)
        {
            yield break;
        }

        foreach (var child in children)
        {
            foreach (var texture in FindTextures(child))
            {
                yield return texture;
            }
        }
    }

    private static bool IsBindingTexture(string textureName) =>
        textureName.Contains("SkillIcons", StringComparison.OrdinalIgnoreCase);

    private static bool IsHudSkillBar(Element element) =>
        element is { IsValid: true, IsVisible: true, ChildCount: >= 13 } &&
        HasSkillIconDescendant(element, depthRemaining: 4);

    private static bool HasSkillIconDescendant(Element element, int depthRemaining)
    {
        if (element == null || !element.IsValid || depthRemaining < 0)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(element.TextureName) && IsBindingTexture(element.TextureName))
        {
            return true;
        }

        var children = element.Children;
        if (children == null)
        {
            return false;
        }

        foreach (var child in children)
        {
            if (HasSkillIconDescendant(child, depthRemaining - 1))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsTextureMatch(string actualTexture, string requestedTexture)
    {
        if (string.IsNullOrWhiteSpace(actualTexture) || string.IsNullOrWhiteSpace(requestedTexture))
        {
            return false;
        }

        if (actualTexture.Equals(requestedTexture, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var actualFile = Path.GetFileName(actualTexture);
        var requestedFile = Path.GetFileName(requestedTexture);
        if (!string.IsNullOrWhiteSpace(actualFile) &&
            actualFile.Equals(requestedFile, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return actualTexture.EndsWith(requestedTexture, StringComparison.OrdinalIgnoreCase);
    }

    private static Element GetChild(Element element, params int[] indexes)
    {
        foreach (var index in indexes)
        {
            var children = element?.Children;
            if (children == null || index < 0 || index >= children.Count)
            {
                return null;
            }

            element = children[index];
        }

        return element;
    }

    private static HotkeyNodeV2.ControllerKey CellIndexToControllerKey(int cellIndex) => cellIndex switch
    {
        1 => HotkeyNodeV2.ControllerKey.LTrigger,
        2 => HotkeyNodeV2.ControllerKey.Lb,
        3 => HotkeyNodeV2.ControllerKey.Up,
        4 => HotkeyNodeV2.ControllerKey.Left,
        5 => HotkeyNodeV2.ControllerKey.Down,
        6 => HotkeyNodeV2.ControllerKey.Right,
        7 => HotkeyNodeV2.ControllerKey.A,
        8 => HotkeyNodeV2.ControllerKey.X,
        9 => HotkeyNodeV2.ControllerKey.Y,
        10 => HotkeyNodeV2.ControllerKey.B,
        11 => HotkeyNodeV2.ControllerKey.Rb,
        12 => HotkeyNodeV2.ControllerKey.RTrigger,
        _ => HotkeyNodeV2.ControllerKey.None
    };
}

internal sealed class ControllerBindingCell
{
    public ControllerBindingCell(int cell, HotkeyNodeV2.ControllerKey key, string texture)
    {
        Cell = cell;
        Key = key;
        Texture = texture;
        TextureFileName = Path.GetFileName(texture);
    }

    public int Cell { get; }
    public HotkeyNodeV2.ControllerKey Key { get; }
    public string Texture { get; }
    public string TextureFileName { get; }
}
