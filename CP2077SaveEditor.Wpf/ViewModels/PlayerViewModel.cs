using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CP2077SaveEditor.Editing;
using CP2077SaveEditor.Wpf.Services;
using static WolvenKit.RED4.Types.Enums;

namespace CP2077SaveEditor.Wpf.ViewModels;

/// <summary>One editable number on a screen (label, limits, value).</summary>
public sealed partial class StatRowViewModel : ObservableObject
{
    private readonly Action<long> _onChanged;
    private bool _loading;

    public StatRowViewModel(string label, long min, long max, Action<long> onChanged, string? hint = null)
    {
        Label = label;
        Minimum = min;
        Maximum = max;
        Hint = hint;
        _onChanged = onChanged;
    }

    public string Label { get; }
    public string? Hint { get; }
    public long Minimum { get; }
    public long Maximum { get; }

    [ObservableProperty]
    private long _value;

    partial void OnValueChanged(long value)
    {
        if (!_loading)
        {
            _onChanged(value);
        }
    }

    /// <summary>Sets the value without treating it as an edit.</summary>
    public void Load(long value)
    {
        _loading = true;
        Value = value;
        _loading = false;
    }
}

public sealed record LifePathOption(gamedataLifePath Value, string Name, string Image);

public sealed partial class PlayerViewModel : PageViewModel
{
    private static readonly Dictionary<gamedataStatType, string> s_attributeNames = new()
    {
        [gamedataStatType.Strength] = "Body",
        [gamedataStatType.Reflexes] = "Reflexes",
        [gamedataStatType.TechnicalAbility] = "Technical Ability",
        [gamedataStatType.Intelligence] = "Intelligence",
        [gamedataStatType.Cool] = "Cool",
    };

    private static readonly Dictionary<gamedataProficiencyType, (string Name, string Hint)> s_skillNames = new()
    {
        [gamedataProficiencyType.CoolSkill] = ("Headhunter", "Cool skill"),
        [gamedataProficiencyType.IntelligenceSkill] = ("Netrunner", "Intelligence skill"),
        [gamedataProficiencyType.ReflexesSkill] = ("Shinobi", "Reflexes skill"),
        [gamedataProficiencyType.StrengthSkill] = ("Solo", "Body skill"),
        [gamedataProficiencyType.TechnicalAbilitySkill] = ("Engineer", "Technical Ability skill"),
    };

    private EditorSession? _session;
    private PlayerSnapshot? _snapshot;
    private bool _loading;

    public override string Title => "Player";
    public override string Glyph => "";

    public ObservableCollection<StatRowViewModel> Character { get; } = new();
    public ObservableCollection<StatRowViewModel> Attributes { get; } = new();
    public ObservableCollection<StatRowViewModel> Skills { get; } = new();
    public ObservableCollection<StatRowViewModel> Points { get; } = new();

    public IReadOnlyList<LifePathOption> LifePaths { get; } = new[]
    {
        new LifePathOption(gamedataLifePath.StreetKid, "Street Kid", "/Assets/lifepath_streetkid.png"),
        new LifePathOption(gamedataLifePath.Nomad, "Nomad", "/Assets/lifepath_nomad.png"),
        new LifePathOption(gamedataLifePath.Corporate, "Corpo", "/Assets/lifepath_corpo.png"),
    };

    [ObservableProperty]
    private LifePathOption? _selectedLifePath;

    [ObservableProperty]
    private StatRowViewModel? _money;

    [ObservableProperty]
    private string? _error;

    public override void OnSessionChanged(EditorSession? session)
    {
        base.OnSessionChanged(session);
        _session = session;
        _snapshot = null;
        Error = null;
        if (session == null)
        {
            return;
        }

        try
        {
            _snapshot = PlayerEditor.Read(session.Save);
            Build();
        }
        catch (Exception e)
        {
            Error = "Player data could not be read from this save: " + e.Message;
        }
    }

    private void Build()
    {
        var s = _snapshot!;
        Character.Clear();
        Attributes.Clear();
        Skills.Clear();
        Points.Clear();

        Character.Add(new StatRowViewModel("Level", 1, PlayerEditor.MaxLevel, v => Edit(x => x.Level = (int)v)));
        Character.Add(new StatRowViewModel("Street Cred", 1, PlayerEditor.MaxStreetCred, v => Edit(x => x.StreetCred = (int)v)));

        foreach (var type in PlayerEditor.AttributeOrder.Where(s.Attributes.ContainsKey))
        {
            Attributes.Add(new StatRowViewModel(s_attributeNames[type], PlayerEditor.MinAttribute, PlayerEditor.MaxAttribute, v => Edit(x => x.Attributes[type] = (int)v)));
        }
        foreach (var type in PlayerEditor.SkillOrder.Where(s.Skills.ContainsKey))
        {
            var (name, hint) = s_skillNames[type];
            Skills.Add(new StatRowViewModel(name, 1, PlayerEditor.MaxSkill, v => Edit(x => x.Skills[type] = (int)v), hint));
        }

        Points.Add(new StatRowViewModel("Attribute points", 0, 999, v => Edit(x => x.AttributePoints = (int)v)));
        Points.Add(new StatRowViewModel("Perk points", 0, 999, v => Edit(x => x.PerkPoints = (int)v)));
        Points.Add(new StatRowViewModel("Relic points", 0, 999, v => Edit(x => x.RelicPoints = (int)v)));

        Money = s.Money == null ? null : new StatRowViewModel("Eurodollars", 0, uint.MaxValue, v => Edit(x => x.Money = (uint)v));
        Reload();
    }

    /// <summary>Pushes the snapshot's values into the rows without triggering edits.</summary>
    private void Reload()
    {
        var s = _snapshot!;
        _loading = true;
        Character[0].Load(s.Level);
        Character[1].Load(s.StreetCred);
        var i = 0;
        foreach (var type in PlayerEditor.AttributeOrder.Where(s.Attributes.ContainsKey)) Attributes[i++].Load(s.Attributes[type]);
        i = 0;
        foreach (var type in PlayerEditor.SkillOrder.Where(s.Skills.ContainsKey)) Skills[i++].Load(s.Skills[type]);
        Points[0].Load(s.AttributePoints);
        Points[1].Load(s.PerkPoints);
        Points[2].Load(s.RelicPoints);
        Money?.Load(s.Money ?? 0);
        SelectedLifePath = LifePaths.FirstOrDefault(l => l.Value == s.LifePath);
        _loading = false;
    }

    partial void OnSelectedLifePathChanged(LifePathOption? value)
    {
        if (!_loading && value != null)
        {
            Edit(x => x.LifePath = value.Value);
        }
    }

    private void Edit(Action<PlayerSnapshot> change)
    {
        if (_session == null || _snapshot == null)
        {
            return;
        }
        change(_snapshot);
        PlayerEditor.Apply(_session.Save, _snapshot);
        _session.MarkChanged();
    }

    private void ApplyPreset(Action<PlayerSnapshot> preset)
    {
        if (_snapshot == null)
        {
            return;
        }
        Edit(preset);
        Reload();
    }

    [RelayCommand] private void MaxEverything() => ApplyPreset(PlayerPresets.MaxEverything);
    [RelayCommand] private void MaxLevel() => ApplyPreset(PlayerPresets.MaxLevelAndStreetCred);
    [RelayCommand] private void MaxAttributes() => ApplyPreset(PlayerPresets.MaxAttributes);
    [RelayCommand] private void MaxSkills() => ApplyPreset(PlayerPresets.MaxSkills);
    [RelayCommand] private void RefundAttributes() => ApplyPreset(PlayerPresets.RefundAttributes);
    [RelayCommand] private void AddMoney(string amount) => ApplyPreset(s => s.Money = (uint)Math.Min(uint.MaxValue, (long)(s.Money ?? 0) + long.Parse(amount)));
}
