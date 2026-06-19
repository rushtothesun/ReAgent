using System;
using System.Linq;
using ExileCore2.PoEMemory.Components;

namespace ReAgent.ExileAuras;

public sealed partial class ExileAurasModule
{
    private ExileAuraIconSource ResolveIconSource(string sourceName)
    {
        if (string.IsNullOrWhiteSpace(sourceName))
        {
            return new ExileAuraIconSource("", "");
        }

        var player = GameController?.Player;
        if (player == null || !player.IsValid)
        {
            return new ExileAuraIconSource(sourceName, "");
        }

        if (player.TryGetComponent<Buffs>(out var buffs))
        {
            var buff = (buffs.BuffsList ?? [])
                .FirstOrDefault(x => string.Equals(x.Name, sourceName, StringComparison.OrdinalIgnoreCase));
            if (buff != null)
            {
                var ddsFile = ResolveBuffIconDdsFile(buff);
                if (!string.IsNullOrWhiteSpace(ddsFile))
                {
                    return new ExileAuraIconSource(
                        string.IsNullOrWhiteSpace(buff.DisplayName) ? sourceName : buff.DisplayName,
                        ddsFile);
                }
            }
        }

        if (player.TryGetComponent<Actor>(out var actor))
        {
            var skill = actor.ActorSkills?
                .FirstOrDefault(x => string.Equals(x.Name, sourceName, StringComparison.OrdinalIgnoreCase));
            if (skill != null)
            {
                return new ExileAuraIconSource(
                    sourceName,
                    skill.EffectsPerLevel?.GrantedEffect?.ActiveSkill?.IconDdsFile ?? string.Empty);
            }
        }

        return new ExileAuraIconSource(sourceName, "");
    }

    private static string ResolveBuffIconDdsFile(Buff buff)
    {
        var buffVisualDdsFile = buff.BuffDefinition?.BuffVisual?.DdsFile;
        if (!string.IsNullOrWhiteSpace(buffVisualDdsFile))
        {
            return buffVisualDdsFile;
        }

        return buff.SourceSkill?.EffectsPerLevel?.GrantedEffect?.ActiveSkill?.IconDdsFile ?? string.Empty;
    }
}
