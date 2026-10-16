using System.Collections.ObjectModel;
using Spat.Libraries.Core.CustomDictionary;
using Spat.Libraries.Core.Settings;
using Spat.Views;

namespace Spat.ViewModels;

public sealed partial class DictionaryViewModel : SettingsPageViewModel<DictionaryView>
{
    private readonly ISettingsService _settings;
    private readonly ICustomDictionaryService _dictionary;

    [ObservableProperty]
    private ObservableCollection<CustomDictionaryEntry> _dictionaryEntries = [];

    [ObservableProperty]
    private bool _hintFirstPassEnabled = true;

    [ObservableProperty]
    private bool _includeInPostProcessingEnabled = true;

    public DictionaryViewModel(ISettingsService settings, ICustomDictionaryService dictionary)
    {
        _settings = settings;
        _dictionary = dictionary;
    }

    protected override Task OnInitializeAsync()
    {
        var dictionary = _settings.Current.Dictionary;

        HintFirstPassEnabled = dictionary.HintFirstPassEnabled;
        IncludeInPostProcessingEnabled = dictionary.IncludeInPostProcessingEnabled;
        RefreshEntries();

        return Task.CompletedTask;
    }

    [RelayCommand]
    private void AddEntry()
    {
        DictionaryEntries.Add(new CustomDictionaryEntry());
    }

    // Blank or half-filled rows are dropped at save, so adding one costs nothing.
    [RelayCommand]
    private void RemoveEntry(CustomDictionaryEntry? entry)
    {
        if (entry is not null)
        {
            DictionaryEntries.Remove(entry);
        }
    }

    [RelayCommand]
    private void MoveEntryUp(CustomDictionaryEntry? entry)
    {
        var index = entry is null ? -1 : DictionaryEntries.IndexOf(entry);

        if (index > 0)
        {
            DictionaryEntries.Move(index, index - 1);
        }
    }

    [RelayCommand]
    private void MoveEntryDown(CustomDictionaryEntry? entry)
    {
        var index = entry is null ? -1 : DictionaryEntries.IndexOf(entry);

        if (index >= 0 && index < DictionaryEntries.Count - 1)
        {
            DictionaryEntries.Move(index, index + 1);
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var settings = _settings.Current;

        settings.Dictionary.HintFirstPassEnabled = HintFirstPassEnabled;
        settings.Dictionary.IncludeInPostProcessingEnabled = IncludeInPostProcessingEnabled;

        await _dictionary.SaveAllAsync(DictionaryEntries);

        // Incomplete rows are dropped at write, so the page has to show what actually survived.
        RefreshEntries();

        await _settings.SaveAsync();

        ReportSaved();
    }

    private void RefreshEntries()
    {
        DictionaryEntries = new ObservableCollection<CustomDictionaryEntry>(_dictionary.GetAll());
    }
}