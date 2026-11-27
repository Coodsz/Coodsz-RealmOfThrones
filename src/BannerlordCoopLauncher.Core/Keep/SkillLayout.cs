// The layout follows CoopKeep's skill sheet (https://github.com/Aderimo/bannerlord-coopkeep,
// src/CoopKeep.App/ViewModels/SkillSheet.cs).
//
// Copyright (c) 2026 aderimo
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

namespace BannerlordCoopLauncher.Core.Keep;

/// <summary>The game's character screen: six attributes, each with its three skills, in the game's order.</summary>
public static class SkillLayout
{
    public const int MaxSkill = 330;
    public const int MaxFocus = 5;

    public static readonly IReadOnlyList<(string Attribute, string Short, string Name, string[] Skills)> Columns = new[]
    {
        ("vigor", "VIG", "Vigor", new[] { "OneHanded", "TwoHanded", "Polearm" }),
        ("control", "CTR", "Control", new[] { "Bow", "Crossbow", "Throwing" }),
        ("endurance", "END", "Endurance", new[] { "Riding", "Athletics", "Crafting" }),
        ("cunning", "CNG", "Cunning", new[] { "Scouting", "Tactics", "Roguery" }),
        ("social", "SOC", "Social", new[] { "Charm", "Leadership", "Trade" }),
        ("intelligence", "INT", "Intelligence", new[] { "Steward", "Medicine", "Engineering" }),
    };

    /// <summary>The English skill names the game uses (Crafting shows as Smithing).</summary>
    public static string SkillName(string id) => id switch
    {
        "OneHanded" => "One Handed",
        "TwoHanded" => "Two Handed",
        "Polearm" => "Polearm",
        "Bow" => "Bow",
        "Crossbow" => "Crossbow",
        "Throwing" => "Throwing",
        "Riding" => "Riding",
        "Athletics" => "Athletics",
        "Crafting" => "Smithing",
        "Scouting" => "Scouting",
        "Tactics" => "Tactics",
        "Roguery" => "Roguery",
        "Charm" => "Charm",
        "Leadership" => "Leadership",
        "Trade" => "Trade",
        "Steward" => "Steward",
        "Medicine" => "Medicine",
        "Engineering" => "Engineering",
        _ => id,
    };

    /// <summary>Two letters for the skill badge when no picture has been built.</summary>
    public static string Monogram(string id) => id switch
    {
        "OneHanded" => "1H",
        "TwoHanded" => "2H",
        "Crafting" => "Sm",
        "Crossbow" => "Cb",
        _ => id.Length >= 2 ? id[..2] : id,
    };

    /// <summary>"+10", "-1".</summary>
    public static string Signed(int n) => n > 0 ? "+" + n : n.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
