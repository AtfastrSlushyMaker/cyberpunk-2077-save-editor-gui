using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WolvenKit.Core.Extensions;
using WolvenKit.RED4.Save;
using WolvenKit.RED4.Save.IO;

namespace CP2077SaveEditor.SaveIO;

/// <summary>A save section that differs between two versions of a save.</summary>
public sealed record SectionChange(string Label, string NodeName, int OldSize, int NewSize);

/// <summary>Compares two sav.dat files section by section (on decompressed data).</summary>
public static class SaveDiff
{
    private static readonly Dictionary<string, string> s_labels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["StatsSystem"] = "Stats (attributes, skills, item stats)",
        ["ScriptableSystemsContainer"] = "Character progression and game systems",
        ["inventory"] = "Inventory",
        ["itemData"] = "Inventory",
        ["UniqueItemCounter"] = "Inventory",
        ["PersistencySystem2"] = "World state",
        ["FactsDB"] = "Quest facts",
        ["FactsTable"] = "Quest facts",
        ["questSystem"] = "Quest facts",
        ["CharacetrCustomization_Appearances"] = "Appearance",
        ["PlayerSystem"] = "Player",
        ["godModeSystem"] = "God mode",
        ["JournalManager"] = "Journal",
        ["ItemDropStorageManager"] = "Dropped items",
    };

    public static string LabelFor(string nodeName) => s_labels.TryGetValue(nodeName, out var label) ? label : nodeName;

    /// <summary>
    /// Returns the innermost changed sections: a section is only reported if none of the sections nested inside it
    /// changed too, so a change in StatsSystem is not also reported for every section containing it.
    /// </summary>
    public static List<SectionChange> Compare(byte[] original, byte[] modified)
    {
        var (nodesA, dataA) = Load(original);
        var (nodesB, dataB) = Load(modified);

        // Match sections by name and occurrence (the n-th "itemData" with the n-th "itemData"), so adding or
        // removing items only affects sections of that kind.
        static List<(NodeEntry Node, int Occurrence)> Keyed(List<NodeEntry> nodes)
        {
            var seen = new Dictionary<string, int>();
            return nodes.Select(n =>
            {
                seen[n.Name] = seen.TryGetValue(n.Name, out var c) ? c + 1 : 0;
                return (n, seen[n.Name]);
            }).ToList();
        }

        var lookupA = Keyed(nodesA).ToDictionary(k => (k.Node.Name, k.Occurrence), k => k.Node);
        var keyedB = Keyed(nodesB);

        var changedB = new List<(NodeEntry New, NodeEntry? Old)>();
        foreach (var (node, occurrence) in keyedB)
        {
            if (!lookupA.Remove((node.Name, occurrence), out var old))
            {
                changedB.Add((node, null)); // added
            }
            else if (!dataA.AsSpan(old.Offset, old.Size).SequenceEqual(dataB.AsSpan(node.Offset, node.Size)))
            {
                changedB.Add((node, old));
            }
        }

        bool Contains(NodeEntry outer, NodeEntry inner) =>
            !ReferenceEquals(outer, inner) && inner.Offset >= outer.Offset && inner.Offset + inner.Size <= outer.Offset + outer.Size;

        var result = changedB
            .Where(c => !changedB.Any(o => Contains(c.New, o.New)))
            .Select(c => new SectionChange(LabelFor(c.New.Name), c.New.Name, c.Old?.Size ?? 0, c.New.Size))
            .ToList();

        // sections left over in the original were removed
        result.AddRange(lookupA.Values.Select(n => new SectionChange(LabelFor(n.Name), n.Name, n.Size, 0)));
        return result;
    }

    /// <summary>Flat node table and decompressed data (node offsets are relative to it).</summary>
    private static (List<NodeEntry> Nodes, byte[] Data) Load(byte[] file)
    {
        using var reader = new CyberpunkSaveReader(new MemoryStream(file));
        reader.ReadFileInfo(out _);
        var br = reader.BaseReader;
        br.BaseStream.Seek(-8, SeekOrigin.End);
        var infoStart = br.ReadInt32();
        br.BaseStream.Seek(infoStart + 4, SeekOrigin.Begin);

        var nodes = new List<NodeEntry>();
        var count = br.ReadVLQInt32();
        for (var i = 0; i < count; i++)
        {
            nodes.Add(new NodeEntry
            {
                Name = br.ReadLengthPrefixedString(),
                NextId = br.ReadInt32(),
                ChildId = br.ReadInt32(),
                Offset = br.ReadInt32(),
                Size = br.ReadInt32(),
            });
        }

        using var data = Compression.Decompress(br, out _);
        using var copy = new MemoryStream();
        data.CopyTo(copy);
        return (nodes, copy.ToArray());
    }
}
